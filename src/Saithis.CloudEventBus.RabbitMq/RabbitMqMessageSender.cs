using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender(
    RabbitMqConnectionManager connectionManager,
    RabbitMqOptions options,
    IRabbitMqEnvelopeMapper envelopeMapper)
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
        var basicProps = new BasicProperties();
        
        // Use envelope mapper to map properties and potentially wrap content
        var bodyToSend = envelopeMapper.MapOutgoing(content, props, basicProps);
        
        // In RabbitMQ.Client 7.x with publisher confirms enabled,
        // BasicPublishAsync returns a ValueTask that completes when the message is confirmed
        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: basicProps,
            body: bodyToSend,
            cancellationToken: cancellationToken);
    }
    
    private string GetExchange(MessageProperties props)
    {
        // 1. Try to get explicit channel name from metadata (populated by CloudEventBus from Registry)
        if (props.TransportMetadata.TryGetValue("ChannelName", out var channelObj) && channelObj is string channelName)
            return channelName;
            
        // 2. Fallback to legacy string metadata
        if (props.TransportMetadata.TryGetValue(ExchangeExtensionKey, out var exchangeObj) && exchangeObj is string exchange)
            return exchange;
            
        return options.DefaultExchange;
    }
    
    private string GetRoutingKey(MessageProperties props)
    {
        // 1. Try to get RabbitMQ options from metadata
        if (props.TransportMetadata.TryGetValue(Configuration.RabbitMqExtensions.RabbitMqMetadataKey, out var metaObj) 
            && metaObj is Configuration.RabbitMqProductionOptions prodOptions
            && !string.IsNullOrEmpty(prodOptions.RoutingKey))
        {
            return prodOptions.RoutingKey;
        }

        // 2. Fallback to legacy string metadata
        if (props.TransportMetadata.TryGetValue(RoutingKeyExtensionKey, out var routingKeyObj) && routingKeyObj is string routingKey)
            return routingKey;
            
        // 3. Fallback to message type if available
        return props.Type ?? "";
    }
    
    public async ValueTask DisposeAsync()
    {
        await connectionManager.DisposeAsync();
    }
}
