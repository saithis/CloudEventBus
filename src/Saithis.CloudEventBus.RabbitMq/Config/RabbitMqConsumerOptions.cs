namespace Saithis.CloudEventBus.RabbitMq.Config;

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

    public RabbitMqConsumerOptions WithRetry(int maxRetries, TimeSpan delay)
    {
        MaxRetries = maxRetries;
        RetryDelay = delay;
        return this;
    }
}
