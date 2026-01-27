namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConsumerOptions
{
    /// <summary>
    /// Queues to consume from, with their configurations.
    /// </summary>
    public List<QueueConsumerConfig> Queues { get; set; } = new();
    
    /// <summary>
    /// Number of messages to prefetch per consumer.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;
    
    /// <summary>
    /// Whether to auto-acknowledge messages (not recommended for production).
    /// </summary>
    public bool AutoAck { get; set; } = false;
}

public class QueueConsumerConfig
{
    /// <summary>
    /// Name of the queue to consume from.
    /// </summary>
    public required string QueueName { get; init; }
}
