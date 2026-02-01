using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq.Config;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender(
    RabbitMqConnectionManager connectionManager,
    RabbitMqOptions options,
    IRabbitMqEnvelopeMapper envelopeMapper)
    : IMessageSender, IAsyncDisposable
{
    public async Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        await using var channel = await connectionManager.CreateChannelAsync(options.UsePublisherConfirms, cancellationToken);
        
        var basicProps = new BasicProperties();
        
        // Use envelope mapper to map properties and potentially wrap content
        var bodyToSend = envelopeMapper.MapOutgoing(content, props, basicProps);
        
        // In RabbitMQ.Client 7.x with publisher confirms enabled,
        // BasicPublishAsync returns a ValueTask that completes when the message is confirmed
        await channel.BasicPublishAsync(
            exchange: props.GetExchange(),
            routingKey: props.GetRoutingKey(),
            mandatory: false,
            basicProperties: basicProps,
            body: bodyToSend,
            cancellationToken: cancellationToken);
    }
    
    public async ValueTask DisposeAsync()
    {
        await connectionManager.DisposeAsync();
    }
}
