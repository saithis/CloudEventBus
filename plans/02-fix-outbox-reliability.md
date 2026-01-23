# Plan 02: Fix Outbox Reliability Issues

## Priority: 2 (Critical Bug Fixes)

## Problems

### Problem A: Serialization Failure Can Cause Duplicates

In `OutboxTriggerInterceptor.SavingChangesAsync()`:

```csharp
foreach (var item in outboxDbContext.OutboxMessages.Queue)
{
    var serializedMessage = messageSerializer.Serialize(item.Message, item.Properties);
    var outboxMessage = OutboxMessageEntity.Create(serializedMessage, item.Properties, timeProvider);
    context.Set<OutboxMessageEntity>().Add(outboxMessage);
}
outboxDbContext.OutboxMessages.Queue.Clear();
```

**What works correctly:** If `SaveChangesAsync()` fails after the interceptor runs, the `OutboxMessageEntity` instances remain in the EF Core change tracker (in `Added` state). On retry, they will be saved - no messages are lost.

**What's broken:** If serialization throws mid-loop (e.g., on item 3 of 5):
- Items 1-2 are already added to the change tracker
- `Queue.Clear()` never runs (exception thrown)
- On retry, all 5 items are processed again
- Items 1-2 are **duplicated** in the change tracker

### Problem B: Race Condition / Duplicate Sends

The distributed lock protects against multiple processors running simultaneously, but if:
1. Processor A acquires lock and loads 100 messages
2. Lock times out (60s) while Processor A is still sending
3. Processor B acquires lock and loads the same 100 messages
4. Both processors send the same messages

### Problem C: Missing Database Indexes

The query `Where(x => x.ProcessedAt == null).OrderBy(x => x.CreatedAt)` will do a full table scan as the outbox grows. No indexes are defined on `OutboxMessageEntity`.

---

## Solution A: Dequeue Items As They're Processed

Remove items from the queue as each one is successfully serialized. This ensures:
- Successfully serialized items won't be re-processed on retry
- Failed items remain in the queue for the next attempt

### Update `OutboxStagingCollection`

Change from `Queue<Item>` to support `TryDequeue`:

```csharp
// In OutboxStagingCollection.cs - Queue<T> already supports TryDequeue in .NET 6+
// No changes needed to the collection itself
```

### Modify `OutboxTriggerInterceptor`

```csharp
internal class OutboxTriggerInterceptor<TDbContext>(...) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        DbContext? context = eventData.Context;
        if (context == null)
            return ValueTask.FromResult(result);

        if (context is not IOutboxDbContext outboxDbContext)
            throw new InvalidOperationException("Expected IOutboxDbContext");

        // Dequeue items as we process them - if serialization fails,
        // successfully processed items are already removed, failed items remain
        while (outboxDbContext.OutboxMessages.Queue.TryDequeue(out var item))
        {
            var serializedMessage = messageSerializer.Serialize(item.Message, item.Properties);
            var outboxMessage = OutboxMessageEntity.Create(serializedMessage, item.Properties, timeProvider);
            context.Set<OutboxMessageEntity>().Add(outboxMessage);
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
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
```

### Why This Works

| Scenario | Behavior |
|----------|----------|
| All items serialize successfully | All dequeued, all added to context, saved on commit |
| Serialization fails on item 3/5 | Items 1-2 dequeued & in context, items 3-5 remain in queue |
| SaveChanges fails | Items already dequeued, but entities in change tracker are saved on retry |
| SaveChanges fails + new DbContext | Entities lost (expected EF Core behavior for any pending changes) |

**Note:** The case where SaveChanges fails and the user creates a new DbContext is expected behavior - all pending changes (including domain entities) would be lost. This is standard EF Core behavior, not specific to the outbox.

---

## Solution B: Add Processing State to Prevent Duplicates

Add a `ProcessingStartedAt` field to track when a message was picked up for processing. Consider messages "stuck" if they've been processing for too long.

### Update `OutboxMessageEntity`

```csharp
internal class OutboxMessageEntity
{
    // Existing fields...
    
    /// <summary>
    /// When this message was picked up for processing. Used to detect stuck messages.
    /// </summary>
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    
    public void MarkAsProcessing(TimeProvider timeProvider)
    {
        ProcessingStartedAt = timeProvider.GetUtcNow();
    }
    
    public void MarkAsProcessed(TimeProvider timeProvider)
    {
        ProcessedAt = timeProvider.GetUtcNow();
        ProcessingStartedAt = null; // Clear processing flag
    }
    
    public void PublishFailed(string error, TimeProvider timeProvider, int maxRetries)
    {
        // ... existing code ...
        ProcessingStartedAt = null; // Clear processing flag on failure too
    }
}
```

### Update `OutboxProcessor` Query

```csharp
private static readonly TimeSpan StuckMessageThreshold = TimeSpan.FromMinutes(5);

// In ProcessOutboxAsync:
var now = timeProvider.GetUtcNow();
var stuckThreshold = now - StuckMessageThreshold;

var messages = await dbContext.Set<OutboxMessageEntity>()
    .Where(x => x.ProcessedAt == null 
             && !x.IsPoisoned
             && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
             && (x.ProcessingStartedAt == null || x.ProcessingStartedAt < stuckThreshold))
    .OrderBy(x => x.CreatedAt)
    .Take(BatchSize)
    .ToArrayAsync(stoppingToken);

// Mark all as processing before sending
foreach (var message in messages)
{
    message.MarkAsProcessing(timeProvider);
}
await dbContext.SaveChangesAsync(stoppingToken);

// Now process them
foreach (var message in messages)
{
    // ... send logic ...
}
```

This ensures:
- Messages being processed by another instance are skipped
- Messages stuck in "processing" state for >5 minutes are recovered

---

## Solution C: Add Database Indexes

### Update `PublicApiExtensions.AddOutboxEntities()`

```csharp
public static void AddOutboxEntities(this ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OutboxMessageEntity>(entity =>
    {
        // Primary key (if not already configured)
        entity.HasKey(e => e.Id);
        
        // Index for the main query: unprocessed, not poisoned, ready to process
        // Covers: ProcessedAt, IsPoisoned, NextAttemptAt, ProcessingStartedAt, CreatedAt
        entity.HasIndex(e => new { 
            e.ProcessedAt, 
            e.IsPoisoned, 
            e.NextAttemptAt, 
            e.ProcessingStartedAt, 
            e.CreatedAt 
        }).HasDatabaseName("IX_OutboxMessages_Processing");
        
        // Configure column constraints
        entity.Property(e => e.Error).HasMaxLength(2000);
        entity.Property(e => e.Content).IsRequired();
        entity.Property(e => e.SerializedProperties).IsRequired();
    });
}
```

Note: Some providers support filtered indexes which would be more efficient:
```csharp
// For SQL Server:
entity.HasIndex(e => e.CreatedAt)
    .HasFilter("[ProcessedAt] IS NULL AND [IsPoisoned] = 0")
    .HasDatabaseName("IX_OutboxMessages_Pending");
```

---

## Files to Modify

1. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxMessageEntity.cs`
   - Add `ProcessingStartedAt` property
   - Add `MarkAsProcessing()` method
   - Update `MarkAsProcessed()` to clear `ProcessingStartedAt`
   - Update `PublishFailed()` to clear `ProcessingStartedAt`

2. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxTriggerInterceptor.cs`
   - Change `foreach` + `Clear()` to `while (TryDequeue())` pattern

3. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxProcessor.cs`
   - Add `StuckMessageThreshold` constant
   - Update query to filter by `ProcessingStartedAt`
   - Mark messages as processing before sending
   - Add an intermediate `SaveChangesAsync` after marking as processing

4. `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs`
   - Update `AddOutboxEntities()` to configure indexes and column constraints

---

## Testing Considerations

- Test that if serialization fails mid-loop, only failed items remain in queue
- Test that successfully serialized items are dequeued even if later items fail
- Test that SaveChanges retry works (entities in change tracker are saved)
- Test that messages with `ProcessingStartedAt` in the past are picked up (stuck recovery)
- Test that messages with recent `ProcessingStartedAt` are skipped
- Verify index is created in migrations

## Migration Required

This change adds a new column to `OutboxMessageEntity`:
- `ProcessingStartedAt` (nullable DateTimeOffset)

Combined with Plan 01, the migration will add:
- `NextAttemptAt` (nullable DateTimeOffset)
- `IsPoisoned` (bool, default false)
- `ProcessingStartedAt` (nullable DateTimeOffset)
- New index on processing-related columns

Users will need to create a new EF Core migration after updating.
