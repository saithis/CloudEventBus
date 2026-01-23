using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

internal class OutboxTriggerInterceptor<TDbContext>(
    OutboxProcessor<TDbContext> outboxProcessor, 
    IMessageSerializer messageSerializer,
    TimeProvider timeProvider) 
    : SaveChangesInterceptor where TDbContext : DbContext, IOutboxDbContext
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        DbContext? context = eventData.Context;
        if (context == null)
        {
            return ValueTask.FromResult(result);
        }

        if (context is not IOutboxDbContext outboxDbContext)
            throw new InvalidOperationException("Expected IOutboxDbContext");

        // Peek and process items - if serialization fails,
        // successfully processed items are already removed, failed items remain
        while (outboxDbContext.OutboxMessages.Queue.TryPeek(out var item))
        {
            var serializedMessage = messageSerializer.Serialize(item.Message, item.Properties);
            var outboxMessage = OutboxMessageEntity.Create(serializedMessage, item.Properties, timeProvider);
            context.Set<OutboxMessageEntity>().Add(outboxMessage);
            
            // Only dequeue after successful serialization
            outboxDbContext.OutboxMessages.Queue.TryDequeue(out _);
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.EntitiesSavedCount == 0)
            return result;

        var outboxMessages = eventData.Context?.ChangeTracker.Entries<OutboxMessageEntity>() ?? [];
        if (outboxMessages.Any(e => e.Entity.ProcessedAt == null))
        {
            await outboxProcessor.ScheduleNowAsync();
        }

        return result;
    }
}