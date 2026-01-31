using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessageSender(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null)
    {
        var options = new RabbitMqOptions();
        configure?.Invoke(options);
        
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        services.AddSingleton<IMessageSender, RabbitMqMessageSender>();
        
        return services;
    }
    
    /// <summary>
    /// Adds RabbitMQ message consuming support.
    /// </summary>
    public static IServiceCollection AddRabbitMqConsumer(
        this IServiceCollection services,
        Action<RabbitMqConsumerOptions> configure)
    {
        var options = new RabbitMqConsumerOptions();
        configure(options);
        
        services.AddSingleton(options);
        services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        services.AddSingleton<MessageDispatcher>();
        services.AddSingleton<RabbitMqRetryHandler>();
        services.AddHostedService<RabbitMqConsumer>();
        
        return services;
    }

    /// <summary>
    /// Adds RabbitMQ topology management.
    /// </summary>
    public static IServiceCollection AddRabbitMqTopologyManager(this IServiceCollection services)
    {
        services.AddHostedService<Infrastructure.RabbitMqTopologyManager>();
        return services;
    }
}
