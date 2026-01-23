# Plan 07: Unify Publishing API (Option B: Explicit Separation)

## Priority: 7 (API Clarity)

## Depends On
- Plan 04 (Message Type Registration)
- Plan 05 (CloudEvents Format)

## Problem

There are currently two ways to publish messages:
1. `ICloudEventBus.PublishAsync()` - Direct publishing, bypasses outbox
2. `DbContext.OutboxMessages.Add()` - Via outbox, transactional

This creates confusion. Users might accidentally use the wrong one and lose transactional guarantees, or think they're getting guarantees when they're not.

## Decision

**Option B: Explicit Separation** - Keep both approaches but make them clearly distinct:
- `ICloudEventBus` for immediate/direct publishing (renamed methods for clarity)
- `DbContext.OutboxMessages` for transactional outbox publishing

Both have valid use cases:
- **Direct**: Fire-and-forget, background jobs, when you don't have a DbContext
- **Outbox**: When you need transactional consistency with database changes

## Solution

### 1. Rename ICloudEventBus Method for Clarity

Update `src/Saithis.CloudEventBus/ICloudEventBus.cs`:

```csharp
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

/// <summary>
/// Interface for publishing CloudEvents messages directly (without outbox).
/// For transactional publishing with EF Core, use DbContext.OutboxMessages.Add() instead.
/// </summary>
public interface ICloudEventBus
{
    /// <summary>
    /// Publishes a message immediately without transactional guarantees.
    /// The message is sent directly to the message broker.
    /// </summary>
    /// <remarks>
    /// This does NOT use the outbox pattern. If you need transactional consistency
    /// with database operations, use <c>DbContext.OutboxMessages.Add()</c> instead.
    /// </remarks>
    Task PublishDirectAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    /// Publishes a message immediately without transactional guarantees.
    /// </summary>
    [Obsolete("Use PublishDirectAsync to make the non-transactional nature explicit")]
    Task PublishAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull;
}
```

### 2. Update CloudEventBus Implementation

Update `src/Saithis.CloudEventBus/CloudEventBus.cs`:

```csharp
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(
    IMessageSerializer serializer, 
    IMessageSender sender,
    MessageTypeRegistry typeRegistry) : ICloudEventBus
{
    public Task PublishDirectAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return PublishInternalAsync(message, props, cancellationToken);
    }

    [Obsolete("Use PublishDirectAsync to make the non-transactional nature explicit")]
    public Task PublishAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return PublishInternalAsync(message, props, cancellationToken);
    }

    private async Task PublishInternalAsync<TMessage>(
        TMessage message, 
        MessageProperties? props,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        props ??= new MessageProperties();
        
        // Auto-populate type from registry if not explicitly set
        if (string.IsNullOrEmpty(props.Type))
        {
            var typeInfo = typeRegistry.GetByClrType<TMessage>();
            if (typeInfo != null)
            {
                props.Type = typeInfo.EventType;
                
                // Copy registered extensions
                foreach (var ext in typeInfo.Extensions)
                {
                    props.Extensions.TryAdd(ext.Key, ext.Value);
                }
            }
            else
            {
                // Try attribute fallback
                props.Type = typeRegistry.ResolveEventType(typeof(TMessage));
            }
        }
        
        var serializedMessage = serializer.Serialize(message, props);
        await sender.SendAsync(serializedMessage, props, cancellationToken);
    }
}
```

### 3. Improve OutboxStagingCollection API

Update `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxStagingCollection.cs`:

```csharp
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Collection for staging messages to be sent via the outbox pattern.
/// Messages added here will be persisted to the database and sent transactionally.
/// </summary>
public class OutboxStagingCollection
{
    internal readonly Queue<Item> Queue = new();

    /// <summary>
    /// Stages a message to be sent when SaveChanges is called.
    /// The message will be persisted to the outbox table and sent by the background processor.
    /// </summary>
    /// <typeparam name="TMessage">The message type (should be registered or have [CloudEvent] attribute)</typeparam>
    /// <param name="message">The message to send</param>
    /// <param name="properties">Optional message properties</param>
    public void Add<TMessage>(TMessage message, MessageProperties? properties = null) 
        where TMessage : notnull
    {
        Queue.Enqueue(new Item
        {
            Message = message,
            Properties = properties ?? new MessageProperties()
        });
    }

    /// <summary>
    /// Stages a message to be sent when SaveChanges is called.
    /// </summary>
    public void Add(object message, MessageProperties? properties = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        
        Queue.Enqueue(new Item
        {
            Message = message,
            Properties = properties ?? new MessageProperties()
        });
    }

    /// <summary>
    /// Gets the number of messages currently staged.
    /// </summary>
    public int Count => Queue.Count;

    internal class Item
    {
        internal required object Message { get; init; }
        internal required MessageProperties Properties { get; init; }
    }
}
```

### 4. Update IOutboxDbContext Documentation

Update `src/Saithis.CloudEventBus.EfCoreOutbox/IOutboxDbContext.cs`:

```csharp
namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Interface that DbContext classes must implement to support the outbox pattern.
/// </summary>
/// <example>
/// <code>
/// public class MyDbContext : DbContext, IOutboxDbContext
/// {
///     public OutboxStagingCollection OutboxMessages { get; } = new();
///     
///     // Usage in application code:
///     // db.MyEntities.Add(entity);
///     // db.OutboxMessages.Add(new MyEvent { ... });
///     // await db.SaveChangesAsync(); // Both saved transactionally
/// }
/// </code>
/// </example>
public interface IOutboxDbContext
{
    /// <summary>
    /// Collection for staging messages to be sent via the outbox.
    /// Messages added here will be persisted and sent when SaveChanges is called.
    /// </summary>
    /// <remarks>
    /// This provides transactional message publishing - if the database transaction
    /// fails, the messages won't be sent. For non-transactional publishing,
    /// use <see cref="ICloudEventBus.PublishDirectAsync{TMessage}"/> instead.
    /// </remarks>
    OutboxStagingCollection OutboxMessages { get; }
}
```

### 5. Add XML Documentation to Guide Users

Create `src/Saithis.CloudEventBus/PublishingGuide.cs` (documentation-only file):

```csharp
namespace Saithis.CloudEventBus;

/// <summary>
/// Guide for choosing between publishing methods in CloudEventBus.
/// </summary>
/// <remarks>
/// <para>
/// CloudEventBus provides two ways to publish messages:
/// </para>
/// 
/// <h2>1. Direct Publishing (ICloudEventBus.PublishDirectAsync)</h2>
/// <para>
/// Use when you need immediate, fire-and-forget publishing without database transactions.
/// </para>
/// <code>
/// await bus.PublishDirectAsync(new OrderShipped { OrderId = 123 });
/// </code>
/// <para>
/// Best for: Background jobs, scheduled tasks, webhooks, when you don't have a DbContext.
/// </para>
/// 
/// <h2>2. Outbox Publishing (DbContext.OutboxMessages.Add)</h2>
/// <para>
/// Use when you need transactional consistency between database changes and message publishing.
/// </para>
/// <code>
/// db.Orders.Add(order);
/// db.OutboxMessages.Add(new OrderCreated { OrderId = order.Id });
/// await db.SaveChangesAsync(); // Both committed atomically
/// </code>
/// <para>
/// Best for: Domain events, integration events that must be consistent with data changes.
/// </para>
/// 
/// <h2>Comparison</h2>
/// <list type="table">
/// <item>
/// <term>Feature</term>
/// <description>Direct | Outbox</description>
/// </item>
/// <item>
/// <term>Transactional</term>
/// <description>No | Yes</description>
/// </item>
/// <item>
/// <term>Latency</term>
/// <description>Immediate | Small delay (background processing)</description>
/// </item>
/// <item>
/// <term>Requires DbContext</term>
/// <description>No | Yes</description>
/// </item>
/// <item>
/// <term>Retry on failure</term>
/// <description>No | Yes (automatic)</description>
/// </item>
/// </list>
/// </remarks>
internal static class PublishingGuide { }
```

### 6. Update Example Code

Update `examples/PlaygroundApi/Program.cs`:

```csharp
// Direct publishing example - no transaction
app.MapPost("/send", async ([FromBody] NoteDto dto, [FromServices] ICloudEventBus bus) =>
{
    // PublishDirectAsync - sends immediately, no outbox
    await bus.PublishDirectAsync(dto, new MessageProperties());
    return TypedResults.Ok(dto);
});

// Outbox publishing example - transactional
app.MapPost("/notes", async ([FromBody] NoteDto dto, [FromServices] NotesDbContext db) =>
{
    var note = new Note
    {
        Text = dto.Text,
        CreatedAt = DateTime.Now,
    };
    
    // Both operations are transactional
    db.Notes.Add(note);
    db.OutboxMessages.Add(new NoteAddedEvent
    {
        Id = note.Id,
        Text = $"New Note: {dto.Text}",
    });
    
    await db.SaveChangesAsync(); // Note saved AND event queued atomically
    return TypedResults.Ok(note);
});
```

---

## Files to Modify

1. `src/Saithis.CloudEventBus/ICloudEventBus.cs` - Add `PublishDirectAsync`, deprecate `PublishAsync`
2. `src/Saithis.CloudEventBus/CloudEventBus.cs` - Implement new method
3. `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxStagingCollection.cs` - Add generic overload and docs
4. `src/Saithis.CloudEventBus.EfCoreOutbox/IOutboxDbContext.cs` - Improve documentation

## Files to Create

1. `src/Saithis.CloudEventBus/PublishingGuide.cs` - Documentation guide

## Files to Update in Examples

1. `examples/PlaygroundApi/Program.cs` - Use new API with comments

---

## Testing Considerations

- Test that `PublishDirectAsync` works the same as `PublishAsync`
- Test that deprecation warning is shown for `PublishAsync`
- Test that outbox publishing still works correctly
- Verify XML documentation appears in IDE tooltips

## Migration Notes

Users will see obsolete warnings when using `PublishAsync`. The fix is simple:
```csharp
// Before
await bus.PublishAsync(message);

// After
await bus.PublishDirectAsync(message);
```

No functional change, just clarity about what the method does.
