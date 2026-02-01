namespace Ratatoskr.RabbitMq.Config;

public class RabbitMqChannelOptions
{
    public string ExchangeType { get; set; } = "topic";
    public bool Durable { get; set; } = true;
    public bool AutoDelete { get; set; } = false;

    public RabbitMqChannelOptions ExchangeTypeDirect()
    {
        ExchangeType = "direct";
        return this;
    }

    public RabbitMqChannelOptions ExchangeTypeTopic()
    {
        ExchangeType = "topic";
        return this;
    }
    
    public RabbitMqChannelOptions ExchangeTypeFanout()
    {
        ExchangeType = "fanout";
        return this;
    }
}
