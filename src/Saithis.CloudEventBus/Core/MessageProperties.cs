namespace Saithis.CloudEventBus.Core;

public class MessageProperties
{
    /// <summary>
    /// Unique identifier for this event. Auto-generated if not set.
    /// Maps to CloudEvents "id" field.
    /// </summary>
    public string? Id { get; set; }
    
    /// <summary>
    /// The type of the message, e.g. "com.example.order-shipped".
    /// Maps to CloudEvents "type" field.
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// URI identifying the event source, e.g. "/orders-service".
    /// Maps to CloudEvents "source" field.
    /// </summary>
    public string? Source { get; set; }
    
    /// <summary>
    /// Subject of the event, e.g. order ID.
    /// Maps to CloudEvents "subject" field.
    /// </summary>
    public string? Subject { get; set; }
    
    /// <summary>
    /// Content type of the data payload.
    /// </summary>
    public string? ContentType { get; set; }
    
    /// <summary>
    /// Event timestamp. Auto-set to current time if not specified.
    /// </summary>
    public DateTimeOffset? Time { get; set; }
    
    /// <summary>
    /// Custom headers to include with the message.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();
    
    /// <summary>
    /// Transport-specific metadata (e.g., RabbitMQ exchange/routing key).
    /// Not included in CloudEvents envelope.
    /// </summary>
    public Dictionary<string, string> Extensions { get; set; } = new();
    
    /// <summary>
    /// CloudEvents extension attributes (included in envelope).
    /// </summary>
    public Dictionary<string, object> CloudEventExtensions { get; set; } = new();
}