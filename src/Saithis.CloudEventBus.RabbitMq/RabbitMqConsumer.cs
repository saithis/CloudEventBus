using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConsumer(
    RabbitMqConnectionManager connectionManager,
    RabbitMqConsumerOptions options,
    MessageDispatcher dispatcher,
    IRabbitMqEnvelopeMapper envelopeMapper,
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
        
        foreach (var queueConfig in options.Queues)
        {
            var channel = await connectionManager.CreateChannelAsync(false, stoppingToken);
            await channel.BasicQosAsync(0, options.PrefetchCount, false);
            
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await HandleMessageAsync(channel, ea, stoppingToken);
            };
            
            await channel.BasicConsumeAsync(
                queue: queueConfig.QueueName,
                autoAck: options.AutoAck,
                consumer: consumer,
                cancellationToken: stoppingToken);
            
            _channels.Add(channel);
            logger.LogInformation("Started consuming from queue '{Queue}'", queueConfig.QueueName);
        }
        
        // Keep running until cancelled
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private async Task HandleMessageAsync(
        IChannel channel, 
        BasicDeliverEventArgs ea,
        CancellationToken cancellationToken)
    {
        var messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
        
        try
        {
            // Use envelope mapper to extract body and properties
            var (body, props) = envelopeMapper.MapIncoming(ea);
            var result = await dispatcher.DispatchAsync(body, props, cancellationToken);
            
            if (!options.AutoAck)
            {
                switch (result)
                {
                    case DispatchResult.Success:
                        await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                        break;
                    case DispatchResult.NoHandlers:
                        // No handler found - reject without requeue (goes to DLQ if configured)
                        logger.LogWarning("No handler for message '{MessageId}', rejecting", messageId);
                        await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                        break;
                    case DispatchResult.DeserializationFailed:
                        // Can't deserialize - reject without requeue (poison message)
                        logger.LogError("Failed to deserialize message '{MessageId}', rejecting", messageId);
                        await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message '{MessageId}'", messageId);
            
            if (!options.AutoAck)
            {
                // Handler threw - requeue for retry
                // TODO: Consider retry count header to avoid infinite loops
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
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
