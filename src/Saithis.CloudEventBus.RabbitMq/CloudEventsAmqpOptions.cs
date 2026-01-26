namespace Saithis.CloudEventBus.RabbitMq;

/// <summary>
/// Options for CloudEvents AMQP protocol binding.
/// </summary>
public class CloudEventsAmqpOptions
{
    /// <summary>
    /// Content mode for CloudEvents serialization. Default is Binary (more efficient).
    /// </summary>
    public CloudEventsAmqpContentMode ContentMode { get; set; } = CloudEventsAmqpContentMode.Binary;
    
    /// <summary>
    /// Default source URI if not specified per message or message type. Default is "/".
    /// </summary>
    public string DefaultSource { get; set; } = "/";
    
    /// <summary>
    /// Time provider for generating timestamps. Default is TimeProvider.System.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>
/// CloudEvents content modes as defined in the AMQP protocol binding.
/// </summary>
public enum CloudEventsAmqpContentMode
{
    /// <summary>
    /// Data is serialized directly, CloudEvents attributes are in AMQP application-properties.
    /// More efficient for AMQP brokers. Uses cloudEvents_ prefix per AMQP binding spec.
    /// </summary>
    Binary,
    
    /// <summary>
    /// Entire CloudEvent is serialized as JSON including envelope and data.
    /// Content-Type: application/cloudevents+json
    /// </summary>
    Structured
}
