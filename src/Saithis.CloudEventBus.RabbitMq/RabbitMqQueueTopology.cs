using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Saithis.CloudEventBus.RabbitMq;

/// <summary>
/// Manages RabbitMQ queue topology including retry and dead letter queues.
/// </summary>
internal class RabbitMqQueueTopology
{
    private const string RetryCountHeader = "x-retry-count";
    
    public static async Task DeclareQueueTopologyAsync(
        IChannel channel,
        QueueConsumerConfig config,
        CancellationToken cancellationToken)
    {
        if (!config.UseManagedRetryTopology)
        {
            return; // Skip auto-declaration
        }
        
        var queueName = config.QueueName;
        var retryQueueName = $"{queueName}{config.RetryQueueSuffix}";
        var dlqName = $"{queueName}{config.DeadLetterQueueSuffix}";
        
        // 1. Declare DLQ (no special config, messages stay here)
        await channel.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        
        // 2. Declare Retry Queue with TTL and DLX back to main queue
        var retryQueueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "", // Default exchange
            ["x-dead-letter-routing-key"] = queueName, // Route back to main queue
            ["x-message-ttl"] = (int)config.RetryDelay.TotalMilliseconds
        };
        
        await channel.QueueDeclareAsync(
            queue: retryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: retryQueueArgs,
            cancellationToken: cancellationToken);
        
        // 3. Declare Main Queue with DLX to retry queue (for normal failures)
        var mainQueueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = retryQueueName
        };
        
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainQueueArgs,
            cancellationToken: cancellationToken);
    }
    
    public static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers == null) return 0;
        
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
        
        return 0;
    }
}
