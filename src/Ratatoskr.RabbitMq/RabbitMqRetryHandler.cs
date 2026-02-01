using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq.Config;

namespace Ratatoskr.RabbitMq;

/// <summary>
/// Handles retry logic for failed messages.
/// </summary>
internal class RabbitMqRetryHandler(ILogger<RabbitMqRetryHandler> logger)
{
    private const string RetryCountHeader = "x-retry-count";

    /// <summary>
    /// Determines how to handle a failed message based on retry configuration.
    /// </summary>
    public async Task HandleFailureAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        RabbitMqConsumerOptions config, 
        string queueName,
        DispatchResult result,
        CancellationToken cancellationToken)
    {
        var messageId = ea.BasicProperties.MessageId ?? "unknown";
        
        // Permanent errors go straight to DLQ
        if (result == DispatchResult.PermanentError || result == DispatchResult.NoHandlers)
        {
            logger.LogWarning("Permanent error for message '{MessageId}', sending to DLQ", messageId);
            await RejectToDlqAsync(channel, ea, config, queueName, cancellationToken);
            return;
        }
        
        // Check retry count
        var retryCount = GetRetryCount(ea.BasicProperties.Headers);
        
        if (retryCount >= config.MaxRetries)
        {
            logger.LogError(
                "Message '{MessageId}' exceeded max retries ({MaxRetries}), sending to DLQ",
                messageId, config.MaxRetries);
            await RejectToDlqAsync(channel, ea, config, queueName, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Message '{MessageId}' will be retried (attempt {RetryCount}/{MaxRetries})",
                messageId, retryCount + 1, config.MaxRetries);
            
            // Reject without requeue - DLX will route to retry queue
            await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
        }
    }
    
    private async Task RejectToDlqAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        RabbitMqConsumerOptions config,
        string queueName,
        CancellationToken cancellationToken)
    {
        // Need to manually publish to DLQ since we can't conditionally route via DLX
        if (config.UseManagedRetryTopology)
        {
            var dlqName = $"{queueName}{config.DeadLetterQueueSuffix}";
            
            // Copy properties and add metadata about failure
            var props = new BasicProperties
            {
                MessageId = ea.BasicProperties.MessageId,
                ContentType = ea.BasicProperties.ContentType,
                DeliveryMode = ea.BasicProperties.DeliveryMode,
                Type = ea.BasicProperties.Type,
                Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
            };
            
            props.Headers["x-original-queue"] = queueName;
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

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers == null) return 0;
        
        // Check explicit header first
        if (headers.TryGetValue(RetryCountHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes when bytes.Length == 4 => BitConverter.ToInt32(bytes, 0),
                _ => 0
            };
        }
        
        // Fallback to x-death header for DLX loops
        if (headers.TryGetValue("x-death", out var xDeathObj) && xDeathObj is System.Collections.IEnumerable xDeathList)
        {
            long totalCount = 0;
            foreach (var entryObj in xDeathList)
            {
                if (entryObj is IDictionary<string, object> entry)
                {
                    if (entry.TryGetValue("count", out var countObj) && 
                        entry.TryGetValue("reason", out var reasonObj))
                    {
                        var reason = GetString(reasonObj);
                        if (reason == "rejected")
                        {
                            totalCount += Convert.ToInt64(countObj);
                        }
                    }
                }
            }
            return (int)totalCount;
        }
        
        return 0;
    }

    private static string GetString(object? value)
    {
        return value switch
        {
            null => "",
            string str => str,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value.ToString() ?? ""
        };
    }
}
