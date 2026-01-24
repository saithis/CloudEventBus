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
    // TODO: use dotnet 10 extension properties
    public OutboxStagingCollection OutboxMessages { get; }
}