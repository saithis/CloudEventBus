using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;

namespace Ratatoskr.RabbitMq;

internal class RabbitMqConsumer(
    RabbitMqConnectionManager connectionManager,
    ChannelRegistry registry,
    RabbitMqTopologyManager topologyManager,
    MessageDispatcher dispatcher,
    IRabbitMqEnvelopeMapper envelopeMapper,
    RabbitMqRetryHandler retryHandler,
    ILogger<RabbitMqConsumer> logger)
    : BackgroundService
{
    private readonly List<IChannel> _channels = new();

    /// <summary>
    /// Gets whether the consumer is healthy (all channels are open).
    /// </summary>
    public virtual bool IsHealthy => _channels.Count > 0 && _channels.All(c => c.IsOpen);
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting RabbitMQ consumer");
        
        // 1. Provision Topology First
        logger.LogInformation("Provisioning topology...");
        await topologyManager.ProvisionTopologyAsync(stoppingToken);
        
        // 2. Start Consumers for each Consumer Channel
        var consumerChannels = registry.GetConsumeChannels();

        foreach (var reg in consumerChannels)
        {
            var options = reg.GetRabbitMqConsumerOptions() ?? new RabbitMqConsumerOptions();

            // Queue name MUST be resolved (provisioning should have ensured it, or we assume it exists)
            // If it's missing here, we probably failed earlier or it's dynamic.
            if (string.IsNullOrEmpty(options.QueueName))
            {
                logger.LogWarning("Skipping consumer channel '{Channel}' because no queue name is configured.", reg.ChannelName);
                continue;
            }

            var channel = await connectionManager.CreateChannelAsync(false, stoppingToken);
            await channel.BasicQosAsync(0, options.PrefetchCount, false, stoppingToken);
            
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await HandleMessageAsync(channel, ea, options, options.QueueName!, reg.ChannelName, stoppingToken);
            };
            
            logger.LogInformation("Starting consuming from queue '{Queue}' for channel '{Channel}'", options.QueueName, reg.ChannelName);
            
            await channel.BasicConsumeAsync(
                queue: options.QueueName!,
                autoAck: options.AutoAck,
                consumer: consumer,
                cancellationToken: stoppingToken);
            
            _channels.Add(channel);
        }
        
        // Keep running until cancelled
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private async Task HandleMessageAsync(
        IChannel channel, 
        BasicDeliverEventArgs ea,
        RabbitMqConsumerOptions options,
        string queueName,
        string channelName,
        CancellationToken cancellationToken)
    {
        var messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
        
        try
        {
            // Use envelope mapper to extract body and properties
            var (body, props) = envelopeMapper.MapIncoming(ea);
            
            // Extract parent context for tracing
            ActivityContext.TryParse(props.TraceParent, props.TraceState, out var parentContext);

            using var activity = RatatoskrDiagnostics.ActivitySource.StartActivity(
                "Ratatoskr.Receive", 
                ActivityKind.Consumer, 
                parentContext);

            if (activity != null)
            {
                // https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#messaging-attributes
                // https://opentelemetry.io/docs/specs/semconv/messaging/rabbitmq/
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.destination.subscription.name", queueName);
                activity.SetTag("messaging.destination.name", ea.Exchange);
                activity.SetTag("messaging.rabbitmq.destination.routing_key", ea.RoutingKey);
                activity.SetTag("messaging.message.id", props.Id);
                activity.SetTag("messaging.message.body.size", body.Length);
            }

            // Dispatcher handles finding the handler based on type info in props/body
            // We pass the ChannelName (context) to help resolve the correct message type if ambiguous
            var result = await dispatcher.DispatchAsync(body, props, cancellationToken, channelName);
            
            if (options.AutoAck) return; // Message already acked by broker

            switch (result)
            {
                case DispatchResult.Success:
                    await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                    break;
                
                case DispatchResult.NoHandlers:
                case DispatchResult.PermanentError:
                case DispatchResult.RecoverableError:
                    await retryHandler.HandleFailureAsync(
                        channel, ea, options, queueName, result, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message '{MessageId}'", messageId);
            
            if (!options.AutoAck)
            {
                // Treat exception as recoverable error
                await retryHandler.HandleFailureAsync(
                    channel, ea, options, queueName, 
                    DispatchResult.RecoverableError, cancellationToken);
            }
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping RabbitMQ consumer");
        
        foreach (var channel in _channels)
        {
            await channel.CloseAsync(cancellationToken);
            channel.Dispose();
        }
        
        await base.StopAsync(cancellationToken);
    }
}
