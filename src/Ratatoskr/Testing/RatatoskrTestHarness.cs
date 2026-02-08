using Ratatoskr.Core;

namespace Ratatoskr.Testing;

/// <summary>
/// A harness for testing Ratatoskr applications without a real message broker.
/// allows for simulating incoming messages and verifying outgoing messages.
/// </summary>
public class RatatoskrTestHarness(
    InMemoryMessageSender sender,
    MessageDispatcher dispatcher,
    IMessageSerializer serializer,
    IMessagePropertiesEnricher enricher)
{
    /// <summary>
    /// Gets the in-memory message sender to verify sent messages.
    /// </summary>
    public InMemoryMessageSender Sender => sender;

    /// <summary>
    /// Simulates receiving a message of the specified type.
    /// This will be dispatched to the registered handlers for this message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message.</typeparam>
    /// <param name="message">The message content.</param>
    /// <param name="properties">Optional message properties. If not provided, minimal properties will be generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SimulateReceiveAsync<TMessage>(
        TMessage message,
        MessageProperties? properties = null,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        // Enrich properties or create new ones
        properties = enricher.Enrich<TMessage>(properties);
        
        // Serialize the message
        var body = serializer.Serialize(message);
        properties.ContentType = serializer.ContentType;

        // Dispatch via the dispatcher
        var result = await dispatcher.DispatchAsync(body, properties, cancellationToken);

        if (result == DispatchResult.NoHandlers)
        {
            // For testing, we might want to warn or throw if no handlers are found, 
            // but for now let's just log or maybe explicitly throw if desired.
            // But the dispatcher logs it.
            // Ideally, tests probably assert that something happened.
        }
    }
}
