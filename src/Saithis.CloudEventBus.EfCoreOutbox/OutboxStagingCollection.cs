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
    public void Add<TMessage>(TMessage message, MessageEnvelope? properties = null) 
        where TMessage : notnull
    {
        Queue.Enqueue(new Item
        {
            Message = message,
            Envelope = properties ?? new MessageEnvelope()
        });
    }

    /// <summary>
    /// Stages a message to be sent when SaveChanges is called.
    /// </summary>
    public void Add(object message, MessageEnvelope? properties = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        
        Queue.Enqueue(new Item
        {
            Message = message,
            Envelope = properties ?? new MessageEnvelope()
        });
    }

    /// <summary>
    /// Gets the number of messages currently staged.
    /// </summary>
    public int Count => Queue.Count;

    internal class Item
    {
        internal required object Message { get; init; }
        internal required MessageEnvelope Envelope { get; init; }
    }
}