using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

/// <summary>
/// Core outbox message processing logic shared between production and testing.
/// This ensures tests use the EXACT SAME logic as production.
/// </summary>
internal class OutboxMessageProcessor<TDbContext> where TDbContext : DbContext, IOutboxDbContext
{
    private readonly TDbContext _dbContext;
    private readonly IMessageSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxOptions _options;
    private readonly ILogger _logger;

    public OutboxMessageProcessor(
        TDbContext dbContext,
        IMessageSender sender,
        TimeProvider timeProvider,
        OutboxOptions options,
        ILogger logger)
    {
        _dbContext = dbContext;
        _sender = sender;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single batch of outbox messages.
    /// Returns the number of messages successfully processed.
    /// </summary>
    /// <param name="includeStuckMessageDetection">Whether to check for stuck messages (only needed in production background processing)</param>
    public async Task<int> ProcessBatchAsync(
        bool includeStuckMessageDetection,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        
        // Build the query for pending messages
        var query = _dbContext.Set<OutboxMessageEntity>()
            .Where(x => x.ProcessedAt == null 
                     && !x.IsPoisoned
                     && (x.NextAttemptAt == null || x.NextAttemptAt <= now));
        
        // Add stuck message detection if needed (only for production background processing)
        if (includeStuckMessageDetection)
        {
            var stuckThreshold = now - _options.StuckMessageThreshold;
            query = query.Where(x => x.ProcessingStartedAt == null || x.ProcessingStartedAt < stuckThreshold);
        }
        
        var messages = await query
            .OrderBy(x => x.CreatedAt)
            .Take(_options.BatchSize)
            .ToArrayAsync(cancellationToken);

        _logger.LogInformation("Found {Count} messages to send", messages.Length);
        
        if (messages.Length == 0)
            return 0;

        // Mark all as processing before sending
        foreach (var message in messages)
        {
            message.MarkAsProcessing(_timeProvider);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var processedCount = 0;
        
        // Process each message with error handling
        foreach (var message in messages)
        {
            try
            {
                _logger.LogInformation("Processing message '{Id}'", message.Id);
                await _sender.SendAsync(message.Content, message.GetProperties(), cancellationToken);
                message.MarkAsProcessed(_timeProvider);
                processedCount++;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send message '{Id}', attempt {Attempt}", 
                    message.Id, message.ErrorCount + 1);
                message.PublishFailed(e.Message, _timeProvider, 
                    _options.MaxRetries, _options.MaxRetryDelay);
            }
        }

        // Save all changes (both successful and failed)
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        
        return processedCount;
    }
}
