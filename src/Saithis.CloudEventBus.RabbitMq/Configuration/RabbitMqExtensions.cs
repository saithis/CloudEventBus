using Saithis.CloudEventBus.Configuration;
using Saithis.CloudEventBus.RabbitMq;

namespace Saithis.CloudEventBus.RabbitMq.Configuration;

public static class RabbitMqExtensions
{
    public const string RabbitMqMetadataKey = "RabbitMq";

    public static ProductionBuilder<T> WithRabbitMq<T>(
        this ProductionBuilder<T> builder,
        Action<RabbitMqProductionOptions> configure)
    {
        var options = new RabbitMqProductionOptions();
        configure(options);
        builder.Registration.Metadata[RabbitMqMetadataKey] = options;
        return builder;
    }

    public static ConsumptionBuilder<T> WithRabbitMq<T>(
        this ConsumptionBuilder<T> builder,
        Action<RabbitMqConsumptionOptions> configure)
    {
        var options = new RabbitMqConsumptionOptions();
        configure(options);
        builder.Registration.Metadata[RabbitMqMetadataKey] = options;
        return builder;
    }
    
    // Also we need to add UseRabbitMq on CloudEventBusBuilder?
    // Plan said: builder.UseRabbitMq(mq => mq.ConnectionString("amqp://..."));
    // This is probably an alias or replacement for AddRabbitMqMessageSender/Consumer?
    // The previous implementation had AddRabbitMqMessageSender/AddRabbitMqConsumer.
    // The plan suggests a unified `UseRabbitMq`.
    
    public static CloudEventBusBuilder UseRabbitMq(
        this CloudEventBusBuilder builder,
        Action<RabbitMqOptions> configure,
        Action<RabbitMqConsumerOptions>? configureConsumer = null)
    {
        builder.Services.AddRabbitMqMessageSender(configure);
        builder.Services.AddRabbitMqTopologyManager();
        
        // Register Consumer if not already? Or always?
        // TopologyManager only runs if registered.
        
        // We register consumer with default or provided options
        builder.Services.AddRabbitMqConsumer(configureConsumer ?? (opt => {}));
        
        return builder;
    }
}
