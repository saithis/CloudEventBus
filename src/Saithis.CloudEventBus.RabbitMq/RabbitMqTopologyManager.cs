using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq.Config;
using System.Security.Cryptography;
using System.Text;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqTopologyManager
{
    private readonly ChannelRegistry _registry;
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ILogger<RabbitMqTopologyManager> _logger;
    private const string RabbitMqChannelOptionsKey = "RabbitMqChannelOptions";
    private const string RabbitMqConsumerOptionsKey = "RabbitMqConsumerOptions";
    private const string RabbitMqMessageOptionsKey = "RabbitMqMessageOptions";

    public RabbitMqTopologyManager(
        ChannelRegistry registry, 
        RabbitMqConnectionManager connectionManager,
        ILogger<RabbitMqTopologyManager> logger)
    {
        _registry = registry;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task ProvisionTopologyAsync(CancellationToken cancellationToken)
    {
        using var channel = await _connectionManager.CreateChannelAsync(true, cancellationToken);

        foreach (var reg in _registry.GetAllChannels())
        {
            await ProvisionChannelAsync(channel, reg, cancellationToken);
        }
    }

    private async Task ProvisionChannelAsync(IChannel channel, ChannelRegistration reg, CancellationToken token)
    {
        var channelOpts = GetMetadata<RabbitMqChannelOptions>(reg.Metadata, RabbitMqChannelOptionsKey) 
                          ?? new RabbitMqChannelOptions(); // Default to Topic if missing?

        // 1. Exchange Logic
        if (reg.Intent == ChannelType.EventPublish || reg.Intent == ChannelType.CommandConsume)
        {
            // We OWN the exchange -> Declare it
            _logger.LogInformation("Declaring exchange '{Exchange}' Type: {Type}", reg.ChannelName, channelOpts.ExchangeType);
            await channel.ExchangeDeclareAsync(
                exchange: reg.ChannelName, 
                type: channelOpts.ExchangeType, 
                durable: channelOpts.Durable, 
                autoDelete: channelOpts.AutoDelete, 
                arguments: null, 
                cancellationToken: token);
        }
        else
        {
            // We EXPECT the exchange -> Validate it (Passive Declare)
            _logger.LogInformation("Validating exchange '{Exchange}' exists", reg.ChannelName);
            try 
            {
                await channel.ExchangeDeclarePassiveAsync(reg.ChannelName, token);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Exchange '{Exchange}' validation failed. It must exist for intent {Intent}.", reg.ChannelName, reg.Intent);
                throw; 
            }
        }

        if (reg.Intent == ChannelType.CommandConsume || reg.Intent == ChannelType.EventConsume)
        {
            var consumerOpts = GetMetadata<RabbitMqConsumerOptions>(reg.Metadata, RabbitMqConsumerOptionsKey);
            
            string queueName = consumerOpts?.QueueName ?? throw new InvalidOperationException($"Queue name must be specified for consumer channel '{reg.ChannelName}'");
            
            IDictionary<string, object?>? queueArgs = null;

            if (consumerOpts.UseManagedRetryTopology)
            {
                var dlqName = $"{queueName}{consumerOpts.DeadLetterQueueSuffix}";
                var retryQueueName = $"{queueName}{consumerOpts.RetryQueueSuffix}";

                _logger.LogInformation("Provisioning retry topology for queue '{Queue}' (DLQ: {Dlq}, Retry: {Retry})", queueName, dlqName, retryQueueName);

                // 1. Declare DLQ
                await channel.QueueDeclareAsync(
                    queue: dlqName,
                    durable: true, // DLQ should usually be durable to prevent data loss
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: token);

                // 2. Declare Retry Queue (TTL -> Main Queue)
                var retryArgs = new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = queueName,
                    ["x-message-ttl"] = (long)consumerOpts.RetryDelay.TotalMilliseconds
                };
                
                await channel.QueueDeclareAsync(
                    queue: retryQueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: retryArgs,
                    cancellationToken: token);

                // 3. Configure Main Queue to Dead-Letter to Retry Queue
                queueArgs = new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = retryQueueName
                };
            }

            _logger.LogInformation("Declaring queue '{Queue}' for channel '{Channel}'", queueName, reg.ChannelName);
            
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: consumerOpts.Durable,
                exclusive: consumerOpts.Exclusive,
                autoDelete: consumerOpts.AutoDelete,
                arguments: queueArgs,
                cancellationToken: token);

            // 4. Bindings
            foreach (var msg in reg.Messages)
            {
                var msgOpts = GetMetadata<RabbitMqMessageOptions>(msg.Metadata, RabbitMqMessageOptionsKey);
                
                string routingKey = msgOpts?.RoutingKey ?? msg.MessageTypeName;
                
                _logger.LogInformation("Binding queue '{Queue}' to exchange '{Exchange}' with key '{Key}'", queueName, reg.ChannelName, routingKey);
                
                await channel.QueueBindAsync(
                    queue: queueName, 
                    exchange: reg.ChannelName, 
                    routingKey: routingKey, 
                    arguments: null, 
                    cancellationToken: token);
            }
        }
    }

    private T? GetMetadata<T>(Dictionary<string, object> metadata, string key) where T : class
    {
        if (metadata.TryGetValue(key, out var val) && val is T typedVal)
            return typedVal;
        return null;
    }
}
