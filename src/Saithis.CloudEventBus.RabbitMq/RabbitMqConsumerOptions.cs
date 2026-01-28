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
    
    // Retry configuration (always enabled)
    
    /// <summary>
    /// Maximum number of retry attempts. Default: 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// Delay between retry attempts. Default: 30 seconds.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Whether to use library-managed retry topology. When enabled, the library will:
    /// - Automatically declare and configure retry/DLQ queues with proper DLX routing
    /// - Manually publish messages to the DLQ with failure metadata
    /// When disabled, expects externally managed queue topology and relies on external DLX configuration.
    /// Default: true.
    /// </summary>
    public bool UseManagedRetryTopology { get; init; } = true;
    
    /// <summary>
    /// Suffix for retry queue name. Default: ".retry".
    /// </summary>
    public string RetryQueueSuffix { get; init; } = ".retry";
    
    /// <summary>
    /// Suffix for dead letter queue name. Default: ".dlq".
    /// </summary>
    public string DeadLetterQueueSuffix { get; init; } = ".dlq";
}
