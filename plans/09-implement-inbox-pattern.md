# Plan 09: Implement Inbox Pattern

## Priority: 9 (Advanced Feature)

## Depends On
- Plan 08 (Message Consuming)

## Problem

Message brokers provide "at-least-once" delivery, meaning the same message can be delivered multiple times due to:
- Consumer crashes after processing but before acknowledgment
- Network issues causing redelivery
- Broker failover

Without idempotency handling, this leads to duplicate processing (e.g., charging a customer twice).

## Solution

Implement the Inbox pattern:
1. Store processed message IDs in a database table
2. Check if message was already processed before handling
3. Use database transactions to ensure atomicity
4. Provide cleanup mechanism for old entries

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

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

/// <summary>
/// Checks and records processed messages for idempotency.
/// </summary>
internal class InboxChecker<TDbContext> where TDbContext : DbContext, IInboxDbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    
    public InboxChecker(TDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }
    
    /// <summary>
    /// Checks if a message was already processed.
    /// </summary>
    public async Task<bool> WasProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        return await _dbContext.Set<InboxMessageEntity>()
            .AnyAsync(x => x.MessageId == messageId, cancellationToken);
    }
    
    /// <summary>
    /// Marks a message as processed.
    /// </summary>
    public async Task MarkAsProcessedAsync(
        string messageId, 
        string messageType,
        string? handlerName,
        CancellationToken cancellationToken)
    {
        var entry = InboxMessageEntity.Create(messageId, messageType, _timeProvider, handlerName);
        _dbContext.Set<InboxMessageEntity>().Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    
    /// <summary>
    /// Tries to mark as processed, returning false if already processed.
    /// Uses database constraints to handle race conditions.
    /// </summary>
    public async Task<bool> TryMarkAsProcessedAsync(
        string messageId,
        string messageType,
        string? handlerName,
        CancellationToken cancellationToken)
    {
        try
        {
            await MarkAsProcessedAsync(messageId, messageType, handlerName, cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation - already processed
            return false;
        }
    }
}
```

---

## Part 4: Idempotent Message Handler Wrapper

Create `src/Saithis.CloudEventBus.EfCoreOutbox/IdempotentMessageHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox.Internal;

namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Wraps a message handler to provide idempotency via the inbox pattern.
/// </summary>
public class IdempotentMessageHandler<TMessage, TDbContext> : IMessageHandler<TMessage>
    where TMessage : notnull
    where TDbContext : DbContext, IInboxDbContext
{
    private readonly IMessageHandler<TMessage> _innerHandler;
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    
    public IdempotentMessageHandler(
        IMessageHandler<TMessage> innerHandler,
        TDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<IdempotentMessageHandler<TMessage, TDbContext>> logger)
    {
        _innerHandler = innerHandler;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }
    
    public async Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken)
    {
        var checker = new InboxChecker<TDbContext>(_dbContext, _timeProvider);
        
        // Check if already processed
        if (await checker.WasProcessedAsync(context.Id, cancellationToken))
        {
            _logger.LogInformation(
                "Message '{MessageId}' was already processed, skipping", 
                context.Id);
            return;
        }
        
        // Process the message
        await _innerHandler.HandleAsync(message, context, cancellationToken);
        
        // Mark as processed (uses DB constraint for race condition safety)
        var handlerName = _innerHandler.GetType().Name;
        await checker.TryMarkAsProcessedAsync(context.Id, context.Type, handlerName, cancellationToken);
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

## Testing Considerations

- Test duplicate messages are skipped
- Test race condition handling with concurrent consumers
- Test cleanup removes old entries
- Test retention period is respected
- Integration test with real database

## Migration Required

New table `InboxMessages` with:
- `MessageId` (string, primary key)
- `MessageType` (string)
- `ProcessedAt` (DateTimeOffset)
- `HandlerName` (string, nullable)
