using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq.Config;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender(
    RabbitMqConnectionManager connectionManager,
    RabbitMqOptions options,
    IRabbitMqEnvelopeMapper envelopeMapper,
    ChannelRegistry registry)
    : IMessageSender, IAsyncDisposable
{
    // Extension keys for routing
    public const string ExchangeExtensionKey = "rabbitmq.exchange";
    public const string RoutingKeyExtensionKey = "rabbitmq.routingKey";
    private const string RabbitMqMessageOptionsKey = "RabbitMqMessageOptions";

    public async Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        await using var channel = await connectionManager.CreateChannelAsync(options.UsePublisherConfirms, cancellationToken);
        
        var (exchange, routingKey) = ResolveTopology(props);
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
    
    private (string Exchange, string RoutingKey) ResolveTopology(MessageProperties props)
    {
        // 1. Explicit overrides take precedence
        string? exchange = null;
        if (props.TransportMetadata.TryGetValue(ExchangeExtensionKey, out var exchangeVal))
            exchange = exchangeVal;
            
        string? routingKey = null;
        if (props.TransportMetadata.TryGetValue(RoutingKeyExtensionKey, out var rkVal))
            routingKey = rkVal;
            
        if (exchange != null && routingKey != null)
            return (exchange, routingKey);

        // 2. Lookup in Registry
        if (!string.IsNullOrEmpty(props.Type))
        {
            var channelReg = registry.FindPublishChannelForTypeName(props.Type);
            if (channelReg != null)
            {
                exchange ??= channelReg.ChannelName;
                
                // Find message registration to get default routing key
                if (routingKey == null)
                {
                    var msgReg = channelReg.Messages.FirstOrDefault(m => m.MessageTypeName == props.Type);
                    if (msgReg != null)
                    {
                         // Try get option from metadata
                         if (msgReg.Metadata.TryGetValue(RabbitMqMessageOptionsKey, out var optsObj) 
                             && optsObj is Saithis.CloudEventBus.RabbitMq.Config.RabbitMqMessageOptions opts)
                         {
                             routingKey = opts.RoutingKey;
                         }
                    }
                }
            }
        }
        
        // 3. Fallbacks
        exchange ??= options.DefaultExchange;
        routingKey ??= props.Type ?? "";
        
        return (exchange, routingKey);
    }
    
    public async ValueTask DisposeAsync()
    {
        await connectionManager.DisposeAsync();
    }
}
