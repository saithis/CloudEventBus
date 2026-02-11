using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ratatoskr.RabbitMq;

/// <summary>
/// Handles safe migration of RabbitMQ topology (queues and exchanges) when configuration changes.
/// </summary>
/// <remarks>
/// SAFETY FEATURES:
/// - Creates backup queues before deleting originals
/// - Multiple shovel passes to catch race conditions
/// - Message count validation at each step
/// - Preserves backup queue if migration fails after original deletion
/// - Never deletes backup queue containing messages
/// 
/// LIMITATIONS:
/// - Cannot prevent message loss from concurrent publishers during migration
/// - Exchange migrations lose all bindings (must be recreated)
/// - Best practice: Pause producers/consumers before migration
/// 
/// RECOVERY:
/// - Failed migrations leave backup queues for manual recovery
/// - Check logs for specific failure stage and message counts
/// - Backup queues are named: {originalQueue}.backup
/// </remarks>
public class TopologyMigrator(
    RabbitMqConnectionManager connectionManager,
    ILogger<TopologyMigrator> logger)
{
    private const string BackupSuffix = ".backup";

    public async Task EnsureQueueMigrationAsync(
        string queueName, 
        IDictionary<string, object?> targetArgs,
        CancellationToken cancellationToken)
    {
        // 1. Check if migration is needed
        bool migrationNeeded = false;
        try
        {
            await using var checkChannel = await connectionManager.CreateChannelAsync(true, cancellationToken);
            // Try to declare with target arguments to see if it matches
            await checkChannel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: targetArgs,
                cancellationToken: cancellationToken);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
        {
            logger.LogWarning("Queue '{Queue}' mismatch detected. Initiating migration...", queueName);
            migrationNeeded = true;
        }

        if (migrationNeeded)
        {
            await MigrateQueueInternalAsync(queueName, targetArgs, cancellationToken);
        }
    }

    private async Task MigrateQueueInternalAsync(
        string queueName,
        IDictionary<string, object?> targetArgs,
        CancellationToken cancellationToken)
    {
        var backupQueueName = $"{queueName}{BackupSuffix}";
        
        await using var channel = await connectionManager.CreateChannelAsync(true, cancellationToken);

        // Track migration state for recovery decisions
        bool backupQueueCreated = false;
        bool originalQueueDeleted = false;
        bool newQueueCreated = false;
        uint initialMessageCount = 0;
        int movedToBackup = 0;

        try
        {
            // 0. Verify backup queue doesn't already exist
            try
            {
                var existing = await channel.QueueDeclarePassiveAsync(backupQueueName, cancellationToken);
                throw new InvalidOperationException(
                    $"Backup queue '{backupQueueName}' already exists with {existing.MessageCount} messages. " +
                    "This may indicate a previous failed migration. Manual recovery required: " +
                    "1) Check if messages are valid, 2) Manually restore or delete backup queue.");
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                // Good - backup queue doesn't exist, we can proceed
                // Need to reopen channel since passive declare closes it on 404
                await channel.CloseAsync(cancellationToken);
            }
            
            // Recreate channel after the passive declare check
            await using var workChannel = await connectionManager.CreateChannelAsync(true, cancellationToken);

            // Get initial message count for validation
            var originalQueueInfo = await workChannel.QueueDeclarePassiveAsync(queueName, cancellationToken);
            initialMessageCount = originalQueueInfo.MessageCount;
            
            logger.LogWarning(
                "WARNING: Queue migration will cause a brief service interruption. " +
                "Queue '{Queue}' has {MessageCount} messages. " +
                "Any messages published during migration may be lost if producers are not paused.",
                queueName, initialMessageCount);

            logger.LogInformation("Creating backup queue '{BackupQueue}'", backupQueueName);
            
            // 1. Create Backup Queue (Quorum to be safe)
            var backupArgs = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
            await workChannel.QueueDeclareAsync(
                queue: backupQueueName, 
                durable: true, 
                exclusive: false, 
                autoDelete: false, 
                arguments: backupArgs, 
                cancellationToken: cancellationToken);
            backupQueueCreated = true;

            // 2. Shovel messages: Original -> Backup (multiple passes to catch race conditions)
            logger.LogInformation("Shoveling messages from '{Source}' to '{Dest}'", queueName, backupQueueName);
            
            // First pass
            movedToBackup = await ShovelMessagesAsync(workChannel, queueName, backupQueueName, cancellationToken);
            logger.LogInformation("First pass: Moved {MessageCount} messages to backup queue", movedToBackup);
            
            // Second pass to catch any messages that arrived during first pass
            var additionalMessages = await ShovelMessagesAsync(workChannel, queueName, backupQueueName, cancellationToken);
            if (additionalMessages > 0)
            {
                logger.LogWarning(
                    "Caught {AdditionalCount} messages that arrived during migration. " +
                    "Total messages in backup: {TotalCount}. " +
                    "This indicates producers are still active during migration.",
                    additionalMessages, movedToBackup + additionalMessages);
                movedToBackup += additionalMessages;
            }
            
            // Verify backup queue has expected message count
            var backupQueueInfo = await workChannel.QueueDeclarePassiveAsync(backupQueueName, cancellationToken);
            if (backupQueueInfo.MessageCount != movedToBackup)
            {
                throw new InvalidOperationException(
                    $"Message count mismatch in backup queue. Expected {movedToBackup}, got {backupQueueInfo.MessageCount}. Aborting migration.");
            }
            
            // 3. Delete Original Queue
            logger.LogInformation("Deleting original queue '{Queue}'", queueName);
            await workChannel.QueueDeleteAsync(queue: queueName, ifUnused: false, ifEmpty: false, cancellationToken: cancellationToken);
            originalQueueDeleted = true;
            
            // 4. Create New Queue with Target Args
            logger.LogInformation("Creating new queue '{Queue}' with target arguments", queueName);
            await workChannel.QueueDeclareAsync(
                queue: queueName, 
                durable: true, 
                exclusive: false, 
                autoDelete: false, 
                arguments: targetArgs, 
                cancellationToken: cancellationToken);
            newQueueCreated = true;
                
            // 5. Shovel messages: Backup -> New
            logger.LogInformation("Shoveling messages from '{Source}' to '{Dest}'", backupQueueName, queueName);
            var restoredCount = await ShovelMessagesAsync(workChannel, backupQueueName, queueName, cancellationToken);
            
            // Verify all messages were restored
            if (restoredCount != movedToBackup)
            {
                logger.LogError(
                    "Message count mismatch during restoration. Moved to backup: {BackupCount}, Restored: {RestoredCount}. " +
                    "CRITICAL: Backup queue '{BackupQueue}' retained for manual inspection.",
                    movedToBackup, restoredCount, backupQueueName);
                throw new InvalidOperationException(
                    $"Failed to restore all messages. Expected {movedToBackup}, restored {restoredCount}. " +
                    $"Backup queue '{backupQueueName}' has been preserved for manual recovery.");
            }
            
            // 6. Final verification before deleting backup
            var finalBackupInfo = await workChannel.QueueDeclarePassiveAsync(backupQueueName, cancellationToken);
            if (finalBackupInfo.MessageCount > 0)
            {
                logger.LogError(
                    "Backup queue still contains {MessageCount} messages after restoration. " +
                    "Backup queue '{BackupQueue}' retained for manual inspection.",
                    finalBackupInfo.MessageCount, backupQueueName);
                throw new InvalidOperationException(
                    $"Backup queue still has {finalBackupInfo.MessageCount} messages. Manual verification required.");
            }
            
            // 7. Delete Backup Queue (only if empty)
            logger.LogInformation("Deleting backup queue '{BackupQueue}'", backupQueueName);
            await workChannel.QueueDeleteAsync(queue: backupQueueName, ifUnused: false, ifEmpty: false, cancellationToken: cancellationToken);
            
            logger.LogInformation(
                "Migration of queue '{Queue}' completed successfully. " +
                "Migrated {MessageCount} messages (initial count was {InitialCount}).",
                queueName, restoredCount, initialMessageCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Migration of queue '{Queue}' failed at stage: " +
                "BackupCreated={BackupCreated}, OriginalDeleted={OriginalDeleted}, NewCreated={NewCreated}",
                queueName, backupQueueCreated, originalQueueDeleted, newQueueCreated);
            
            // CRITICAL: Only cleanup backup queue if original queue still exists
            // If original was deleted, backup contains all the data and MUST be preserved
            if (backupQueueCreated && !originalQueueDeleted)
            {
                logger.LogInformation(
                    "Original queue still exists. Safe to cleanup backup queue '{BackupQueue}'.",
                    backupQueueName);
                
                try
                {
                    await using var cleanupChannel = await connectionManager.CreateChannelAsync(true, CancellationToken.None);
                    var backupInfo = await cleanupChannel.QueueDeclarePassiveAsync(backupQueueName, CancellationToken.None);
                    
                    if (backupInfo.MessageCount == 0)
                    {
                        await cleanupChannel.QueueDeleteAsync(backupQueueName, ifUnused: false, ifEmpty: false, CancellationToken.None);
                        logger.LogInformation("Cleaned up empty backup queue '{BackupQueue}'", backupQueueName);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Backup queue '{BackupQueue}' has {MessageCount} messages. NOT deleting for safety. Manual cleanup required.",
                            backupQueueName, backupInfo.MessageCount);
                    }
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(cleanupEx, 
                        "Failed to cleanup backup queue '{BackupQueue}'. Manual cleanup may be required.", 
                        backupQueueName);
                }
            }
            else if (originalQueueDeleted)
            {
                logger.LogCritical(
                    "CRITICAL: Original queue was deleted but migration failed. " +
                    "Backup queue '{BackupQueue}' contains {MessageCount} messages and MUST NOT be deleted. " +
                    "Manual recovery required: Rename backup queue to '{OriginalQueue}' or investigate new queue '{NewQueue}'.",
                    backupQueueName, movedToBackup, queueName, newQueueCreated ? queueName : "not created");
            }
            
            throw;
        }
    }
    
    private async Task<int> ShovelMessagesAsync(IChannel channel, string sourceQueue, string destQueue, CancellationToken cancellationToken)
    {
        int messageCount = 0;
        int consecutiveEmptyChecks = 0;
        const int MaxEmptyChecks = 3; // Check multiple times to ensure queue is truly empty
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await channel.BasicGetAsync(sourceQueue, false, cancellationToken);
            if (result == null)
            {
                // Queue appears empty, but check a few times to ensure no race conditions
                consecutiveEmptyChecks++;
                if (consecutiveEmptyChecks >= MaxEmptyChecks)
                {
                    break; // Queue is truly empty
                }
                
                // Small delay before rechecking (allows any in-flight messages to arrive)
                await Task.Delay(100, cancellationToken);
                continue;
            }

            // Reset empty check counter since we got a message
            consecutiveEmptyChecks = 0;

            try 
            {
                // Publish to destination
                // Use default exchange with routing key = destQueue
                var props = new BasicProperties(result.BasicProperties);
                
                await channel.BasicPublishAsync(
                    exchange: "", 
                    routingKey: destQueue, 
                    mandatory: true, 
                    basicProperties: props, 
                    body: result.Body, 
                    cancellationToken: cancellationToken);
                
                // Only ack after successful publish
                await channel.BasicAckAsync(result.DeliveryTag, false, cancellationToken);
                messageCount++;
                
                // Log progress periodically
                if (messageCount % 100 == 0)
                {
                    logger.LogDebug("Shoveled {Count} messages from '{Source}' to '{Dest}'", 
                        messageCount, sourceQueue, destQueue);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, 
                    "Failed to shovel message #{MessageNum} from '{Source}' to '{Dest}'. " +
                    "Message will be nacked and requeued. Migration aborted.",
                    messageCount + 1, sourceQueue, destQueue);
                
                // Requeue the message so it's not lost
                await channel.BasicNackAsync(result.DeliveryTag, false, requeue: true, cancellationToken);
                throw;
            }
        }
        
        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Shovel operation cancelled after moving {MessageCount} messages from '{Source}' to '{Dest}'. " +
                "Remaining messages are still in source queue.",
                messageCount, sourceQueue, destQueue);
        }
        
        return messageCount;
    }

    public async Task EnsureExchangeMigrationAsync(string exchangeName, string targetType, CancellationToken cancellationToken)
    {
         bool migrationNeeded = false;
         try
         {
             await using var checkChannel = await connectionManager.CreateChannelAsync(true, cancellationToken);
             await checkChannel.ExchangeDeclareAsync(
                 exchange: exchangeName, 
                 type: targetType, 
                 durable: true, 
                 autoDelete: false, 
                 arguments: null, 
                 cancellationToken: cancellationToken);
         }
         catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406) 
         {
             logger.LogWarning("Exchange '{Exchange}' type mismatch. Recreating...", exchangeName);
             migrationNeeded = true;
         }

         if (migrationNeeded)
         {
             await using var channel = await connectionManager.CreateChannelAsync(true, cancellationToken);
             
             logger.LogWarning(
                 "WARNING: Deleting exchange '{Exchange}'. " +
                 "This will remove ALL bindings to queues. " +
                 "Any in-flight messages may be lost. " +
                 "Ensure producers/consumers are paused before proceeding.",
                 exchangeName);
             
             // Note: Unlike queues, exchanges don't store messages, so we can't preserve them.
             // However, we lose bindings which must be recreated.
             await channel.ExchangeDeleteAsync(exchange: exchangeName, ifUnused: false, cancellationToken: cancellationToken);
             await channel.ExchangeDeclareAsync(
                 exchange: exchangeName, 
                 type: targetType, 
                 durable: true, 
                 autoDelete: false, 
                 arguments: null, 
                 cancellationToken: cancellationToken);
             
             logger.LogWarning(
                 "Exchange '{Exchange}' migrated to type '{Type}'. " +
                 "IMPORTANT: All previous bindings have been removed. " +
                 "They will be recreated when topology manager runs, but messages may be lost in the interim.",
                 exchangeName, targetType);
         }
    }
}
