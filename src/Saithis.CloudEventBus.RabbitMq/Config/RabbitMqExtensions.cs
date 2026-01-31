using Saithis.CloudEventBus.Config;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq.Config;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqExtensions
{
    private const string RabbitMqChannelOptionsKey = "RabbitMqChannelOptions";
    private const string RabbitMqConsumerOptionsKey = "RabbitMqConsumerOptions";
    private const string RabbitMqMessageOptionsKey = "RabbitMqMessageOptions";

    public static ChannelBuilder WithRabbitMq(this ChannelBuilder builder, Action<RabbitMqChannelOptions> configure)
    {
        // This is typically for Exchange settings (Producers and Consumers both care about Exchange type)
        // But mainly for declaration (EventPublish / CommandConsume).
        // For CommandPublish / EventConsume, we might just be validating.
        
        var options = new RabbitMqChannelOptions();
        configure(options);
        
        builder.WithMetadata(RabbitMqChannelOptionsKey, options);
        return builder;
    }

    /// <summary>
    /// Configures RabbitMQ Consumer options (Queue settings).
    /// Only valid for Consumer channels.
    /// </summary>
    public static ChannelBuilder WithRabbitMqConsumer(this ChannelBuilder builder, Action<RabbitMqConsumerOptions> configure)
    {
        // Valid for CommandConsume and EventConsume
        var options = new RabbitMqConsumerOptions();
        configure(options);
        
        builder.WithMetadata(RabbitMqConsumerOptionsKey, options);
        return builder;
    }
    
    // We can overload WithRabbitMq to handle both if we want a unified API as per plan example?
    // Plan example: .WithRabbitMq(cfg => cfg.QueueName("...").ExchangeType(...))
    // This implies a combined options object or checks.
    // The plan had separate methods or combined?
    // Plan example: 
    // builder.AddEventConsumeChannel("users.events")
    //        .WithRabbitMq(cfg => cfg
    //             .QueueName("orders.user-handler") 
    //             .ExchangeType(ExchangeType.Topic) 
    //         )
    
    // So distinct properties on one builder, or overloaded method?
    // I can make a combined helper or just extend RabbitMqChannelOptions to include Queue stuff?
    // But Exchange params apply to the Channel (Exchange), Queue params apply to the Consumer (Queue).
    // Let's support a combined configuration closure for convenience, or strictly separate?
    // The plan showed one `.WithRabbitMq(...)` block.
    // Let's try to support that style.
    
    public static ChannelBuilder WithRabbitMq(this ChannelBuilder builder, Action<RabbitMqCombinedOptions> configure)
    {
        var options = new RabbitMqCombinedOptions();
        configure(options);
        
        if (options.ChannelOptions != null)
             builder.WithMetadata(RabbitMqChannelOptionsKey, options.ChannelOptions);
             
        if (options.ConsumerOptions != null)
             builder.WithMetadata(RabbitMqConsumerOptionsKey, options.ConsumerOptions);
             
        return builder;
    }

    public static MessageBuilder WithRoutingKey(this MessageBuilder builder, string routingKey)
    {
        var options = new RabbitMqMessageOptions { RoutingKey = routingKey };
        builder.WithMetadata(RabbitMqMessageOptionsKey, options);
        return builder;
    }
}