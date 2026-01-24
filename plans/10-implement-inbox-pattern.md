# Plan 10: Implement Inbox Pattern

## Priority: Advanced Feature

## Depends On
- Plan 09 (Message Consuming) - Required for handler infrastructure

## Problem

Message brokers provide "at-least-once" delivery, meaning the same message can be delivered multiple times due to:
- Consumer crashes after processing but before acknowledgment
- Network issues causing redelivery
- Broker failover

Without idempotency handling, this leads to duplicate processing (e.g., charging a customer twice).

## Goals (from overview)
- Idempotent message processing
- Support for the inbox pattern
- Works with horizontally scaled applications

## Solution

Implement the Inbox pattern:
1. Store processed message IDs in a database table
2. Use database constraints and transactions to prevent race conditions
3. Mark message as processed BEFORE calling handler (optimistic lock via insert)
4. Provide cleanup mechanism for old entries

## Important Design Decision: Mark-Before-Handle

To prevent race conditions with concurrent consumers in horizontally scaled apps:
- **Insert inbox record first** (acts as optimistic lock)
- If insert succeeds → process the message
- If insert fails (duplicate key) → message already being/was processed, skip

This ensures only ONE consumer can process a message, even with concurrent delivery.

---

## Part 1: Inbox Entity

Create `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxMessageEntity.cs`:

```csharp
namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

/// <summary>
/// Tracks processed messages to prevent duplicate handling.
/// </summary>
internal class InboxMessageEntity
{
    /// <summary>
    /// The CloudEvent ID of the processed message.
    /// </summary>
    public required string MessageId { get; init; }
    
    /// <summary>
    /// The CloudEvent type.
    /// </summary>
    public required string MessageType { get; init; }
    
    /// <summary>
    /// When the message was processed.
    /// </summary>
    public required DateTimeOffset ProcessedAt { get; init; }
    
    /// <summary>
    /// Optional: handler that processed this message.
    /// </summary>
    public string? HandlerName { get; init; }

    public static InboxMessageEntity Create(
        string messageId, 
        string messageType, 
        TimeProvider timeProvider,
        string? handlerName = null)
    {
        return new InboxMessageEntity
        {
            MessageId = messageId,
            MessageType = messageType,
            ProcessedAt = timeProvider.GetUtcNow(),
            HandlerName = handlerName
        };
    }
}
```

---

## Part 2: Inbox DbContext Integration

Update `src/Saithis.CloudEventBus.EfCoreOutbox/IOutboxDbContext.cs`:

```csharp
namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Interface for DbContexts that support outbox and/or inbox patterns.
/// </summary>
public interface IOutboxDbContext
{
    OutboxStagingCollection OutboxMessages { get; }
}

/// <summary>
/// Extended interface that also supports the inbox pattern.
/// </summary>
public interface IInboxDbContext : IOutboxDbContext
{
    // No additional members - inbox is managed internally
}
```

Update `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs`:

```csharp
public static void AddOutboxEntities(this ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OutboxMessageEntity>(entity =>
    {
        entity.HasKey(e => e.Id);
        // ... existing index configuration ...
    });
}

public static void AddInboxEntities(this ModelBuilder modelBuilder)
{
    modelBuilder.Entity<InboxMessageEntity>(entity =>
    {
        entity.HasKey(e => e.MessageId);
        
        // Index for cleanup queries
        entity.HasIndex(e => e.ProcessedAt)
            .HasDatabaseName("IX_InboxMessages_ProcessedAt");
    });
}

/// <summary>
/// Adds both outbox and inbox entities.
/// </summary>
public static void AddOutboxAndInboxEntities(this ModelBuilder modelBuilder)
{
    modelBuilder.AddOutboxEntities();
    modelBuilder.AddInboxEntities();
}
```

---

## Part 3: Inbox Checker Service

Create `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxChecker.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

/// <summary>
/// Checks and records processed messages for idempotency.
/// Uses "mark-before-handle" pattern for race condition safety.
/// </summary>
internal class InboxChecker<TDbContext> where TDbContext : DbContext, IInboxDbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    
    public InboxChecker(TDbContext dbContext, TimeProvider timeProvider, ILogger logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// Tries to claim a message for processing by inserting into inbox.
    /// Returns true if this consumer successfully claimed the message.
    /// Returns false if another consumer already claimed it (duplicate).
    /// 
    /// This is an atomic operation - only one consumer can succeed.
    /// </summary>
    public async Task<bool> TryClaimMessageAsync(
        string messageId,
        string messageType,
        string? handlerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = InboxMessageEntity.Create(messageId, messageType, _timeProvider, handlerName);
            _dbContext.Set<InboxMessageEntity>().Add(entry);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogDebug("Successfully claimed message '{MessageId}' for processing", messageId);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Another consumer already claimed this message
            _logger.LogInformation("Message '{MessageId}' already claimed by another consumer, skipping", messageId);
            return false;
        }
    }
    
    /// <summary>
    /// Removes a claim if processing failed and we want to allow retry.
    /// Call this when you want to allow another delivery attempt.
    /// </summary>
    public async Task ReleaseClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        await _dbContext.Set<InboxMessageEntity>()
            .Where(x => x.MessageId == messageId)
            .ExecuteDeleteAsync(cancellationToken);
        
        _logger.LogDebug("Released claim for message '{MessageId}'", messageId);
    }
    
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        // Check for common duplicate key exception patterns
        // PostgreSQL: 23505, SQL Server: 2627/2601, SQLite: 19
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## Part 4: Idempotent Message Handler Wrapper

Create `src/Saithis.CloudEventBus.EfCoreOutbox/IdempotentMessageHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox.Internal;

namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Wraps a message handler to provide idempotency via the inbox pattern.
/// Uses "mark-before-handle" to prevent race conditions in horizontally scaled apps.
/// </summary>
public class IdempotentMessageHandler<TMessage, TDbContext> : IMessageHandler<TMessage>
    where TMessage : notnull
    where TDbContext : DbContext, IInboxDbContext
{
    private readonly IMessageHandler<TMessage> _innerHandler;
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly InboxOptions _options;
    
    public IdempotentMessageHandler(
        IMessageHandler<TMessage> innerHandler,
        TDbContext dbContext,
        TimeProvider timeProvider,
        InboxOptions options,
        ILogger<IdempotentMessageHandler<TMessage, TDbContext>> logger)
    {
        _innerHandler = innerHandler;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }
    
    public async Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken)
    {
        var checker = new InboxChecker<TDbContext>(_dbContext, _timeProvider, _logger);
        var handlerName = _innerHandler.GetType().Name;
        
        // Try to claim the message (insert into inbox)
        // If this fails, another consumer already claimed it
        var claimed = await checker.TryClaimMessageAsync(
            context.Id, context.Type, handlerName, cancellationToken);
        
        if (!claimed)
        {
            _logger.LogInformation(
                "Message '{MessageId}' already claimed by another consumer, skipping", 
                context.Id);
            return;
        }
        
        try
        {
            // We have the claim - process the message
            await _innerHandler.HandleAsync(message, context, cancellationToken);
            _logger.LogDebug("Successfully processed message '{MessageId}'", context.Id);
        }
        catch (Exception ex)
        {
            // Handler failed - decide whether to release the claim for retry
            if (_options.ReleaseClaimOnError)
            {
                _logger.LogWarning(ex, 
                    "Handler failed for message '{MessageId}', releasing claim for retry", 
                    context.Id);
                await checker.ReleaseClaimAsync(context.Id, cancellationToken);
            }
            else
            {
                _logger.LogError(ex, 
                    "Handler failed for message '{MessageId}', keeping claim (no retry)", 
                    context.Id);
            }
            
            throw; // Re-throw so broker can nack/retry
        }
    }
}
```

---

## Part 5: Inbox Cleanup Service

Create `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxCleanupProcessor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

internal class InboxCleanupProcessor<TDbContext> : BackgroundService
    where TDbContext : DbContext, IInboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<InboxOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InboxCleanupProcessor<TDbContext>> _logger;
    
    public InboxCleanupProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<InboxOptions> options,
        TimeProvider timeProvider,
        ILogger<InboxCleanupProcessor<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_options.Value.CleanupInterval, _timeProvider, stoppingToken);
            
            try
            {
                await CleanupOldEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbox cleanup");
            }
        }
    }
    
    private async Task CleanupOldEntriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        
        var cutoff = _timeProvider.GetUtcNow() - _options.Value.RetentionPeriod;
        
        var deleted = await dbContext.Set<InboxMessageEntity>()
            .Where(x => x.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        
        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old inbox entries", deleted);
        }
    }
}
```

---

## Part 6: Inbox Options

Create `src/Saithis.CloudEventBus.EfCoreOutbox/InboxOptions.cs`:

```csharp
namespace Saithis.CloudEventBus.EfCoreOutbox;

public class InboxOptions
{
    public const string SectionName = "CloudEventBus:Inbox";
    
    /// <summary>
    /// How long to keep inbox entries before cleanup. Default: 7 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    
    /// <summary>
    /// How often to run cleanup. Default: 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
    
    /// <summary>
    /// Whether inbox pattern is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Whether to release the inbox claim when a handler throws an exception.
    /// 
    /// If true: The claim is released, allowing the message to be redelivered and retried.
    /// If false: The claim is kept, preventing duplicate processing even on failure.
    /// 
    /// Default: false (fail-safe, prevents duplicate processing)
    /// 
    /// Set to true if your handlers are designed to be safely retried.
    /// </summary>
    public bool ReleaseClaimOnError { get; set; } = false;
}
```

---

## Part 7: Registration Extensions

Add to `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs`:

```csharp
/// <summary>
/// Registers the inbox pattern for idempotent message handling.
/// </summary>
public static IServiceCollection AddInboxPattern<TDbContext>(
    this IServiceCollection services,
    Action<InboxOptions>? configure = null)
    where TDbContext : DbContext, IInboxDbContext
{
    var options = new InboxOptions();
    configure?.Invoke(options);
    
    services.AddSingleton(Options.Create(options));
    services.AddHostedService<InboxCleanupProcessor<TDbContext>>();
    
    return services;
}
```

---

## Example Usage

### DbContext Setup

```csharp
public class NotesDbContext : DbContext, IInboxDbContext
{
    public DbSet<Note> Notes { get; set; }
    public OutboxStagingCollection OutboxMessages { get; } = new();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutboxAndInboxEntities();
    }
}
```

### Service Registration

```csharp
services.AddInboxPattern<NotesDbContext>(options =>
{
    options.RetentionPeriod = TimeSpan.FromDays(14);
});
```

### Handler with Idempotency

```csharp
// The handler registration can automatically wrap with idempotency
services.AddCloudEventBus(bus => bus
    .AddMessageWithHandler<NoteAddedEvent, NoteAddedHandler>("notes.added")
    .UseIdempotentHandlers<NotesDbContext>()); // Wraps all handlers
```

---

## Files to Create

1. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxMessageEntity.cs`
2. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxChecker.cs`
3. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/InboxCleanupProcessor.cs`
4. `src/Saithis.CloudEventBus.EfCoreOutbox/IdempotentMessageHandler.cs`
5. `src/Saithis.CloudEventBus.EfCoreOutbox/InboxOptions.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus.EfCoreOutbox/IOutboxDbContext.cs` - Add IInboxDbContext
2. `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs` - Add inbox registration

---

## Package Naming Consideration

**Question from overview**: Should `Saithis.CloudEventBus.EfCoreOutbox` be renamed to `Saithis.CloudEventBus.EfCore`?

**Recommendation**: Yes, rename to `Saithis.CloudEventBus.EfCore` since it will contain:
- Outbox pattern (sending)
- Inbox pattern (receiving)
- Shared EF Core infrastructure

This is a breaking change but makes more sense architecturally.

---

## Testing Considerations

- Test duplicate messages are skipped (second insert fails)
- Test race condition handling with concurrent consumers (parallel test)
- Test `ReleaseClaimOnError = true` releases claim on failure
- Test `ReleaseClaimOnError = false` keeps claim on failure
- Test cleanup removes old entries
- Test retention period is respected
- Integration test with real database (PostgreSQL, SQL Server)
- Test with InMemory database (note: unique constraints may behave differently)

## Migration Required

New table `InboxMessages` with:
- `MessageId` (string, primary key) - ensures uniqueness
- `MessageType` (string)
- `ProcessedAt` (DateTimeOffset)
- `HandlerName` (string, nullable)

Index on `ProcessedAt` for efficient cleanup queries.
