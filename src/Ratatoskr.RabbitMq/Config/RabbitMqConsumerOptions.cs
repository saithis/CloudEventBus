namespace Ratatoskr.RabbitMq.Config;

public class RabbitMqConsumerOptions
{
    public string? QueueName { get; set; }
    public ushort PrefetchCount { get; set; } = 10;
    public bool AutoAck { get; set; } = false;
    
    // Default queue settings
    public bool Durable { get; set; } = true;
    public bool Exclusive { get; set; } = false;
    public bool AutoDelete { get; set; } = false;

    public RabbitMqConsumerOptions WithQueueName(string name)
    {
        QueueName = name;
        return this;
    }
    
    public RabbitMqConsumerOptions WithPrefetch(ushort count)
    {
        PrefetchCount = count;
        return this;
    }
    
    // Retry Configuration
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseManagedRetryTopology { get; set; } = true;
    public string RetryQueueSuffix { get; set; } = ".retry";
    public string DeadLetterQueueSuffix { get; set; } = ".dlq";

    // Queue Configuration
    public QueueType QueueType { get; set; } = QueueType.Quorum;
    public IDictionary<string, object?> QueueArguments { get; set; } = new Dictionary<string, object?>();

    public RabbitMqConsumerOptions WithQueueType(QueueType type)
    {
        QueueType = type;
        return this;
    }

    public RabbitMqConsumerOptions WithQueueArguments(IDictionary<string, object?> arguments)
    {
        QueueArguments = arguments;
        return this;
    }

    public RabbitMqConsumerOptions WithRetry(int maxRetries, TimeSpan delay)
    {
        MaxRetries = maxRetries;
        RetryDelay = delay;
        return this;
    }
}
