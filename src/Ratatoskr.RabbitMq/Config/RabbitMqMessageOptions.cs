namespace Ratatoskr.RabbitMq.Config;

public class RabbitMqMessageOptions
{
    public string? RoutingKey { get; set; }

    public RabbitMqMessageOptions WithRoutingKey(string routingKey)
    {
        RoutingKey = routingKey;
        return this;
    }
}
