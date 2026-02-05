using System.Diagnostics;
using RabbitMQ.Client;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;

namespace Ratatoskr.RabbitMq;

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
        
        using var activity = RatatoskrDiagnostics.ActivitySource.StartActivity(
            "Ratatoskr.Send", 
            ActivityKind.Client);

        if (activity != null)
        {
            // Inject current trace context into headers for propagation
            props.TraceParent = activity.Id;
            props.TraceState = activity.TraceStateString;
            
            // https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#messaging-attributes
            // https://opentelemetry.io/docs/specs/semconv/messaging/rabbitmq/
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination.name", props.GetExchange());
            activity.SetTag("messaging.rabbitmq.destination.routing_key", props.GetRoutingKey() ?? props.Type);
            activity.SetTag("messaging.message.id", props.Id);
            activity.SetTag("messaging.message.body.size", content.Length);
        }

        // Use envelope mapper to map properties and potentially wrap content
        var bodyToSend = envelopeMapper.MapOutgoing(content, props, basicProps);
        
        // In RabbitMQ.Client 7.x with publisher confirms enabled,
        // BasicPublishAsync returns a ValueTask that completes when the message is confirmed
        await channel.BasicPublishAsync(
            exchange: props.GetExchange(),
            routingKey: props.GetRoutingKey() ?? props.Type,
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
