# Plan 01: Fix Outbox Error Handling

## Priority: 1 (Critical Bug Fix)

## Problem

The current `OutboxProcessor` has broken error handling:

1. `OutboxMessageEntity.PublishFailed()` method exists but is **never called**
2. Failed messages are caught and logged, but not marked as failed
3. The query `Where(x => x.ProcessedAt == null)` picks up failed messages forever
4. No maximum retry count - infinite retries
5. No exponential backoff - immediate retries flood the system
6. No dead-letter/poison message handling

### Current Broken Code

In `OutboxProcessor.cs` lines 93-106:
```csharp
try
{
    foreach (var message in messages)
    {
        logger.LogInformation("Processing message '{Id}'", message.Id);
        await sender.SendAsync(message.Content, message.GetProperties(), stoppingToken);
        message.MarkAsProcessed(timeProvider);
    }
}
finally
{
    // We always want to save here, so that sent messages will not be sent again if possible
    await dbContext.SaveChangesAsync(CancellationToken.None);
}
```

When `sender.SendAsync()` throws, the exception bubbles up, the message is NOT marked as processed or failed, and the outer catch just logs it.

## Solution

### 1. Update `OutboxMessageEntity`

Add new fields and methods:

```csharp
internal class OutboxMessageEntity
{
    // Existing fields...
    
    /// <summary>
    /// When the message should next be attempted. Null means ready to process.
    /// Used for exponential backoff.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }
    
    /// <summary>
    /// True if the message has permanently failed and should not be retried.
    /// </summary>
    public bool IsPoisoned { get; private set; }

    public void PublishFailed(string error, TimeProvider timeProvider, int maxRetries)
    {
        ErrorCount++;
        Error = error.Length > 2000 ? error[..2000] : error;
        FailedAt = timeProvider.GetUtcNow();
        
        if (ErrorCount >= maxRetries)
        {
            IsPoisoned = true;
            NextAttemptAt = null;
        }
        else
        {
            // Exponential backoff: 2^attempt seconds (2s, 4s, 8s, 16s, 32s, 64s, 128s, 256s...)
            // Cap at 5 minutes
            var delaySeconds = Math.Min(Math.Pow(2, ErrorCount), 300);
            NextAttemptAt = timeProvider.GetUtcNow().AddSeconds(delaySeconds);
        }
    }
    
    public void MarkAsPoisoned(string reason, TimeProvider timeProvider)
    {
        IsPoisoned = true;
        Error = reason.Length > 2000 ? reason[..2000] : reason;
        FailedAt = timeProvider.GetUtcNow();
        NextAttemptAt = null;
    }
}
```

### 2. Update `OutboxProcessor` Query

Change the query to respect backoff and exclude poisoned messages:

```csharp
var now = timeProvider.GetUtcNow();
var messages = await dbContext.Set<OutboxMessageEntity>()
    .Where(x => x.ProcessedAt == null 
             && !x.IsPoisoned
             && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
    .OrderBy(x => x.CreatedAt)
    .Take(BatchSize)
    .ToArrayAsync(stoppingToken);
```

### 3. Update `OutboxProcessor` Processing Loop

Handle errors per-message instead of per-batch:

```csharp
foreach (var message in messages)
{
    try
    {
        logger.LogInformation("Processing message '{Id}'", message.Id);
        await sender.SendAsync(message.Content, message.GetProperties(), stoppingToken);
        message.MarkAsProcessed(timeProvider);
    }
    catch (Exception e)
    {
        logger.LogWarning(e, "Failed to send message '{Id}', attempt {Attempt}", 
            message.Id, message.ErrorCount + 1);
        message.PublishFailed(e.Message, timeProvider, MaxRetries);
    }
}

// Save all changes (both successful and failed)
await dbContext.SaveChangesAsync(CancellationToken.None);
```

### 4. Add Configuration Constant (to be replaced with options in Plan 06)

```csharp
private const int MaxRetries = 5;
```

### 5. Add Index for Query Performance

In `PublicApiExtensions.AddOutboxEntities()`:

```csharp
public static void AddOutboxEntities(this ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OutboxMessageEntity>(entity =>
    {
        entity.HasIndex(e => new { e.ProcessedAt, e.IsPoisoned, e.NextAttemptAt, e.CreatedAt })
            .HasFilter("[ProcessedAt] IS NULL AND [IsPoisoned] = 0");
    });
}
```

Note: The filter syntax may need adjustment for different database providers.

## Files to Modify

1. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxMessageEntity.cs`
   - Add `NextAttemptAt` property
   - Add `IsPoisoned` property
   - Update `PublishFailed()` method
   - Add `MarkAsPoisoned()` method

2. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxProcessor.cs`
   - Add `MaxRetries` constant
   - Update query to filter by `NextAttemptAt` and `IsPoisoned`
   - Update processing loop to handle errors per-message
   - Call `PublishFailed()` on exceptions

3. `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs`
   - Update `AddOutboxEntities()` to configure the entity with an index

## Testing Considerations

- Unit test `PublishFailed()` increments `ErrorCount` correctly
- Unit test exponential backoff calculation
- Unit test `IsPoisoned` is set after max retries
- Integration test that a failing message is retried with backoff
- Integration test that a poisoned message is not retried

## Migration Required

This change adds new columns to `OutboxMessageEntity`:
- `NextAttemptAt` (nullable DateTimeOffset)
- `IsPoisoned` (bool, default false)

Users will need to create a new EF Core migration after updating.
