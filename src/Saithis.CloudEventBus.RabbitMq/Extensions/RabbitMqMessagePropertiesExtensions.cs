using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqMessagePropertiesExtensions
{
    private const string ExchangeExtensionKey = "rabbitmq.exchange";
    private const string RoutingKeyExtensionKey = "rabbitmq.routingKey";

    extension(MessageProperties props)
    {
        public void SetExchange(string exchange)
        {
            props.TransportMetadata[ExchangeExtensionKey] = exchange;
        }

        public string GetExchange() => props.TransportMetadata[ExchangeExtensionKey];
        
        public void SetRoutingKey(string routingKey)
        {
            props.TransportMetadata[RoutingKeyExtensionKey] = routingKey;
        }
        
        public string GetRoutingKey() => props.TransportMetadata[RoutingKeyExtensionKey];
    }
}