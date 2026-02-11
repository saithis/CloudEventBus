namespace Ratatoskr.RabbitMq.Config;

public class RabbitMqCombinedOptions
{
    internal RabbitMqChannelOptions? ChannelOptions;
    internal RabbitMqConsumerOptions? ConsumerOptions;

    private RabbitMqChannelOptions EnsureChannelOptions() => ChannelOptions ??= new RabbitMqChannelOptions();
    private RabbitMqConsumerOptions EnsureConsumerOptions() => ConsumerOptions ??= new RabbitMqConsumerOptions();

    public RabbitMqCombinedOptions ExchangeType(string type)
    {
        EnsureChannelOptions().ExchangeType = type;
        return this;
    }
    
    public RabbitMqCombinedOptions ExchangeTypeTopic() => ExchangeType("topic");
    public RabbitMqCombinedOptions ExchangeTypeDirect() => ExchangeType("direct");

    public RabbitMqCombinedOptions QueueName(string name)
    {
        EnsureConsumerOptions().QueueName = name;
        return this;
    }
    
    public RabbitMqCombinedOptions Prefetch(ushort count)
    {
        EnsureConsumerOptions().PrefetchCount = count;
        return this;
    }

    public RabbitMqCombinedOptions AutoAck(bool autoAck)
    {
        EnsureConsumerOptions().AutoAck = autoAck;
        return this;
    }

    public RabbitMqCombinedOptions QueueOptions(bool durable = true, bool exclusive = false, bool autoDelete = false)
    {
        var opts = EnsureConsumerOptions();
        opts.Durable = durable;
        opts.Exclusive = exclusive;
        opts.AutoDelete = autoDelete;
        return this;
    }

    public RabbitMqCombinedOptions WithQueueType(QueueType type)
    {
        EnsureConsumerOptions().QueueType = type;
        return this;
    }

    public RabbitMqCombinedOptions WithQueueArguments(IDictionary<string, object?> arguments)
    {
        EnsureConsumerOptions().QueueArguments = arguments;
        return this;
    }

    public RabbitMqCombinedOptions RetryOptions(int maxRetries, TimeSpan? delay = null, bool useManaged = true)
    {
        var opts = EnsureConsumerOptions();
        opts.MaxRetries = maxRetries;
        if (delay.HasValue) opts.RetryDelay = delay.Value;
        opts.UseManagedRetryTopology = useManaged;
        return this;
    }
}