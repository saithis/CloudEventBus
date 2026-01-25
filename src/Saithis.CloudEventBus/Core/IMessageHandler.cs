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
    /// <param name="context">Context about the message delivery</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context provided to message handlers.
/// </summary>
public class MessageContext
{
    /// <summary>
    /// The CloudEvent ID.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// The CloudEvent type.
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// The CloudEvent source.
    /// </summary>
    public required string Source { get; init; }
    
    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset? Time { get; init; }
    
    /// <summary>
    /// CloudEvent subject (optional).
    /// </summary>
    public string? Subject { get; init; }
    
    /// <summary>
    /// All headers from the message.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    
    /// <summary>
    /// The raw message body bytes.
    /// </summary>
    public required byte[] RawBody { get; init; }
}
