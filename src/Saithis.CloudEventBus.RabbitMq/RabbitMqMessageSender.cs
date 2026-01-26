using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
    : IMessageSender, IAsyncDisposable
{
    // Extension keys for routing
    public const string ExchangeExtensionKey = "rabbitmq.exchange";
    public const string RoutingKeyExtensionKey = "rabbitmq.routingKey";

    public async Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        await using var channel = await connectionManager.CreateChannelAsync(options.UsePublisherConfirms, cancellationToken);
        
        var exchange = GetExchange(props);
        var routingKey = GetRoutingKey(props);
        var basicProps = CreateBasicProperties(props);
        
        // In RabbitMQ.Client 7.x with publisher confirms enabled,
        // BasicPublishAsync returns a ValueTask that completes when the message is confirmed
        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: basicProps,
            body: content,
            cancellationToken: cancellationToken);
    }
    
    private string GetExchange(MessageProperties props)
    {
        if (props.TransportMetadata.TryGetValue(ExchangeExtensionKey, out var exchange))
            return exchange;
        return options.DefaultExchange;
    }
    
    private string GetRoutingKey(MessageProperties props)
    {
        if (props.TransportMetadata.TryGetValue(RoutingKeyExtensionKey, out var routingKey))
            return routingKey;
        // Fall back to message type if available
        return props.Type ?? "";
    }
    
    private static BasicProperties CreateBasicProperties(MessageProperties props)
    {
        var basicProps = new BasicProperties
        {
            ContentType = props.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };
        
        if (props.Headers.Count > 0)
        {
            basicProps.Headers = new Dictionary<string, object?>();
            foreach (var header in props.Headers)
            {
                basicProps.Headers[header.Key] = header.Value;
            }
        }
        
        if (!string.IsNullOrEmpty(props.Type))
        {
            basicProps.Type = props.Type;
        }
        
        return basicProps;
    }
    
    public async ValueTask DisposeAsync()
    {
        await connectionManager.DisposeAsync();
    }
}
