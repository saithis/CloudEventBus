namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Handles messages of a specific type.
/// </summary>
/// <typeparam name="TMessage">The message type to handle</typeparam>
public interface IMessageHandler<in TMessage> where TMessage : notnull
{
    /// <summary>
    /// Handles the message.
    /// </summary>
    /// <param name="message">The deserialized message</param>
    /// <param name="envelope">Context about the message delivery</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleAsync(TMessage message, MessageEnvelope envelope, CancellationToken cancellationToken);
}
