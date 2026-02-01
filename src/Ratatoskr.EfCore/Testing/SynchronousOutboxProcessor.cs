using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ratatoskr.Core;
using Ratatoskr.EfCore.Internal;

namespace Ratatoskr.EfCore.Testing;

/// <summary>
/// A synchronous outbox processor for testing that uses the EXACT SAME processing logic
/// as production via the shared OutboxMessageProcessor.
/// 
/// This ensures that when you change the core processing logic, tests automatically
/// use the updated logic - no risk of drift between test and production code.
/// 
/// What's different from production:
/// - No distributed locking (tests typically run in isolation)
/// - No background loop (you control when processing happens via explicit calls)
/// - No stuck message detection (not needed for synchronous tests)
/// </summary>
public class SynchronousOutboxProcessor<TDbContext>(
    IServiceProvider serviceProvider,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<SynchronousOutboxProcessor<TDbContext>>? logger = null)
    where TDbContext : DbContext, IOutboxDbContext
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Processes all pending outbox messages synchronously.
    /// Uses the EXACT SAME OutboxMessageProcessor as production.
    /// Returns the total number of messages successfully processed.
    /// </summary>
    public async Task<int> ProcessAllAsync(CancellationToken cancellationToken = default)
    {
        var totalProcessed = 0;
        
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IMessageSender>();
        
        // Use the EXACT SAME processor as production
        var processor = new OutboxMessageProcessor<TDbContext>(
            dbContext, sender, timeProvider, _options, _logger);
        
        // Keep processing batches until no more messages remain
        while (true)
        {
            // Process batch WITHOUT stuck message detection (not needed for synchronous tests)
            var batchProcessed = await processor.ProcessBatchAsync(
                includeStuckMessageDetection: false,
                cancellationToken);
            
            totalProcessed += batchProcessed;
            
            if (batchProcessed == 0)
                break; // No more messages to process
        }
        
        return totalProcessed;
    }
    
    /// <summary>
    /// Gets the count of pending (unprocessed) messages.
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        
        return await dbContext.Set<OutboxMessageEntity>()
            .CountAsync(x => x.ProcessedAt == null && !x.IsPoisoned, cancellationToken);
    }
}
