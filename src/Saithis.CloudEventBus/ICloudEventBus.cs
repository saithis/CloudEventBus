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