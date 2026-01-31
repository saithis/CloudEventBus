namespace Saithis.CloudEventBus.RabbitMq.Configuration;

public class RabbitMqConsumptionOptions
{
    /// <summary>
    /// The exchange type (e.g., topic, direct, fanout).
    /// </summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>
    /// The routing key or binding key to bind the queue with.
    /// </summary>
    public string? RoutingKey { get; set; }
    
    /// <summary>
    /// The prefetch count for the consumer.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    public RabbitMqConsumptionOptions ExchangeType(string exchangeType)
    {
        this.ExchangeType = exchangeType;
        return this;
    }

    public RabbitMqConsumptionOptions RoutingKey(string routingKey)
    {
        this.RoutingKey = routingKey;
        return this;
    }

    public RabbitMqConsumptionOptions PrefetchCount(ushushort prefetchCount)
    {
        this.PrefetchCount = prefetchCount;
        return this;
    }
}
