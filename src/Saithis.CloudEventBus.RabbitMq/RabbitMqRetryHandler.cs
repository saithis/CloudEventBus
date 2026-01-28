using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

/// <summary>
/// Handles retry logic for failed messages.
/// </summary>
internal class RabbitMqRetryHandler
{
    private readonly ILogger<RabbitMqRetryHandler> _logger;
    
    public RabbitMqRetryHandler(ILogger<RabbitMqRetryHandler> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Determines how to handle a failed message based on retry configuration.
    /// </summary>
    public async Task HandleFailureAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        QueueConsumerConfig config,
        DispatchResult result,
        CancellationToken cancellationToken)
    {
        var messageId = ea.BasicProperties.MessageId ?? "unknown";
        
        // Permanent errors go straight to DLQ
        if (result == DispatchResult.PermanentError || result == DispatchResult.NoHandlers)
        {
            _logger.LogWarning("Permanent error for message '{MessageId}', sending to DLQ", messageId);
            await RejectToDlqAsync(channel, ea, config, cancellationToken);
            return;
        }
        
        // Check retry count
        var retryCount = RabbitMqQueueTopology.GetRetryCount(ea.BasicProperties.Headers);
        
        if (retryCount >= config.MaxRetries)
        {
            _logger.LogError(
                "Message '{MessageId}' exceeded max retries ({MaxRetries}), sending to DLQ",
                messageId, config.MaxRetries);
            await RejectToDlqAsync(channel, ea, config, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Message '{MessageId}' will be retried (attempt {RetryCount}/{MaxRetries})",
                messageId, retryCount + 1, config.MaxRetries);
            
            // Reject without requeue - DLX will route to retry queue
            await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
        }
    }
    
    private async Task RejectToDlqAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        QueueConsumerConfig config,
        CancellationToken cancellationToken)
    {
        // Need to manually publish to DLQ since we can't conditionally route via DLX
        if (config.UseManagedRetryTopology)
        {
            var dlqName = $"{config.QueueName}{config.DeadLetterQueueSuffix}";
            
            // Copy properties and add metadata about failure
            var props = new BasicProperties
            {
                MessageId = ea.BasicProperties.MessageId,
                ContentType = ea.BasicProperties.ContentType,
                DeliveryMode = ea.BasicProperties.DeliveryMode,
                Type = ea.BasicProperties.Type,
                Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
            };
            
            props.Headers["x-original-queue"] = config.QueueName;
            props.Headers["x-failure-time"] = DateTimeOffset.UtcNow.ToString("O");
            
            // Publish to DLQ
            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: dlqName,
                mandatory: false,
                basicProperties: props,
                body: ea.Body,
                cancellationToken: cancellationToken);
            
            // ACK original message
            await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
        }
        else
        {
            // Just reject without requeue - let DLX handle it
            await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
        }
    }
}
