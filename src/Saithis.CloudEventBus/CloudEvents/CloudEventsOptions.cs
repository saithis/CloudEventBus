namespace Saithis.CloudEventBus.CloudEvents;

public class CloudEventsOptions
{
    /// <summary>
    /// Whether to wrap messages in CloudEvents envelope. Default true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Content mode for CloudEvents serialization.
    /// </summary>
    public CloudEventsContentMode ContentMode { get; set; } = CloudEventsContentMode.Structured;
    
    /// <summary>
    /// Default source URI if not specified per message or message type.
    /// </summary>
    public string DefaultSource { get; set; } = "/";
}

public enum CloudEventsContentMode
{
    /// <summary>
    /// Entire CloudEvent is serialized as JSON including envelope and data.
    /// Content-Type: application/cloudevents+json
    /// </summary>
    Structured,
    
    /// <summary>
    /// Data is serialized directly, CloudEvents attributes are in headers.
    /// More efficient for brokers that support headers well (RabbitMQ, Kafka).
    /// </summary>
    Binary
}
