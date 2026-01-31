namespace Saithis.CloudEventBus.RabbitMq.Configuration;

public class RabbitMqProductionOptions
{
    /// <summary>
    /// The exchange type (e.g., topic, direct, fanout).
    /// </summary>
    public string ExchangeType { get; set; } = "topic";
    
    /// <summary>
    /// The routing key to use when publishing.
    /// </summary>
    public string? RoutingKey { get; set; }
    
    // Add Fluent Configuration methods if desired or keep as POCO
    // For fluent syntax like `.ExchangeType(...)` we can add methods or use a builder wrapper.
    // The plan showed: cfg.ExchangeType(ExchangeType.Topic).RoutingKey(...)
    // So we should support fluent setters.

    public RabbitMqProductionOptions ExchangeType(string exchangeType)
    {
        this.ExchangeType = exchangeType;
        return this;
    }

    public RabbitMqProductionOptions RoutingKey(string routingKey)
    {
        this.RoutingKey = routingKey;
        return this;
    }
}
