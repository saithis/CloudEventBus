using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Saithis.CloudEventBus.Configuration;
using Saithis.CloudEventBus.RabbitMq.Configuration;

namespace Saithis.CloudEventBus.RabbitMq.Infrastructure;

public class RabbitMqTopologyManager : IHostedService
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ProductionRegistry _productionRegistry;
    private readonly ConsumptionRegistry _consumptionRegistry;
    private readonly ILogger<RabbitMqTopologyManager> _logger;
    private readonly RabbitMqOptions _options;

    public RabbitMqTopologyManager(
        RabbitMqConnectionManager connectionManager,
        ProductionRegistry productionRegistry,
        ConsumptionRegistry consumptionRegistry,
        RabbitMqOptions options,
        ILogger<RabbitMqTopologyManager> logger)
    {
        _connectionManager = connectionManager;
        _productionRegistry = productionRegistry;
        _consumptionRegistry = consumptionRegistry;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Provisioning RabbitMQ Topology...");

        using var channel = _connectionManager.CreateChannel();

        // 1. Provision Production/Sending Topology
        foreach (var reg in _productionRegistry.GetAll())
        {
            if (reg.Metadata.TryGetValue(RabbitMqExtensions.RabbitMqMetadataKey, out var metadataObj) && 
                metadataObj is RabbitMqProductionOptions options)
            {
                var exchangeName = reg.ChannelName; // The Logical Channel is the Exchange Name by default
                
                if (reg.Intent == MessageIntent.EventProduction)
                {
                    // We OWn it -> Declare
                    _logger.LogInformation("Declaring Exchange '{Exchange}' for {MessageType} (EventProduction)", exchangeName, reg.MessageType.Name);
                    channel.ExchangeDeclare(exchangeName, options.ExchangeType, durable: true, autoDelete: false);
                }
                else if (reg.Intent == MessageIntent.CommandSending)
                {
                    // Ww SEND to it -> Validate (Passive)
                    _logger.LogInformation("Validating Exchange '{Exchange}' for {MessageType} (CommandSending)", exchangeName, reg.MessageType.Name);
                    // channel.ExchangeDeclarePassive throws if not found
                    try 
                    {
                        channel.ExchangeDeclarePassive(exchangeName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to validate exchange '{Exchange}'", exchangeName);
                        throw; 
                    }
                }
            }
        }

        // 2. Provision Consumption Topology
        foreach (var reg in _consumptionRegistry.GetAll())
        {
             if (reg.Metadata.TryGetValue(RabbitMqExtensions.RabbitMqMetadataKey, out var metadataObj) && 
                metadataObj is RabbitMqConsumptionOptions options)
            {
                var exchangeName = reg.ChannelName; // Expected Source Exchange
                // But wait, the registry might not have ChannelName if resolving from type?
                // The ConsumptionRegistration has ChannelName (optional/empty?)
                // In Plan: builder.ConsumesCommand().FromChannel("orders.commands") -> ChannelName set.
                // If ChannelName is empty, maybe we derived it or we skip exchange validation?
                // But we definitely need a Queue.
                
                var queueName = reg.SubscriptionName; // The Queue Name
                
                _logger.LogInformation("Declaring Queue '{Queue}' for {MessageType}", queueName, reg.MessageType.Name);
                
                // Use RabbitMqQueueTopology to ensure consistent arguments (DLQ, TTL, etc.)
                var queueConfig = new QueueConsumerConfig { QueueName = queueName };
                // Note: RabbitMqQueueTopology is static internal in base namespace. We need to respect namespaces.
                // Assuming RabbitMqQueueTopology is accessible.
                await RabbitMqQueueTopology.DeclareQueueTopologyAsync(channel, queueConfig, cancellationToken);

                if (reg.Intent == MessageIntent.CommandConsumption)
                {
                    // We OWN the exchange?
                    // Plan says: CommandConsumption -> We own "orders.commands". We declare it.
                    // So we must declare exchange if we are the command processor.
                    if (!string.IsNullOrEmpty(exchangeName))
                    {
                         _logger.LogInformation("Declaring Exchange '{Exchange}' for {MessageType} (CommandConsumption)", exchangeName, reg.MessageType.Name);
                        channel.ExchangeDeclare(exchangeName, options.ExchangeType, durable: true, autoDelete: false);
                    
                        // Bind Queue to Exchange
                        var routingKey = options.RoutingKey ?? queueName; // Default routing key?
                        _logger.LogInformation("Binding '{Queue}' to '{Exchange}' with key '{RoutingKey}'", queueName, exchangeName, routingKey);
                        channel.QueueBind(queueName, exchangeName, routingKey);
                    }
                }
                else if (reg.Intent == MessageIntent.EventConsumption)
                {
                    // We LISTEN to "users.events". Validate it exists.
                     if (!string.IsNullOrEmpty(exchangeName))
                    {
                         _logger.LogInformation("Validating Exchange '{Exchange}' for {MessageType} (EventConsumption)", exchangeName, reg.MessageType.Name);
                         channel.ExchangeDeclarePassive(exchangeName);

                        // Bind Queue to Exchange
                        var routingKey = options.RoutingKey ?? "#";
                        _logger.LogInformation("Binding '{Queue}' to '{Exchange}' with key '{RoutingKey}'", queueName, exchangeName, routingKey);
                        channel.QueueBind(queueName, exchangeName, routingKey);
                    }
                }
            }
        }
        
        return; // Synchronous end of async method if no await
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
