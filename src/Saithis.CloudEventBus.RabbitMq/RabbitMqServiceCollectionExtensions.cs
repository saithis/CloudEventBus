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
        services.AddSingleton<RabbitMqTopologyManager>();
        
        return services;
    }
    
    /// <summary>
    /// Adds RabbitMQ message consuming support.
    /// </summary>
    public static IServiceCollection AddRabbitMqConsumer(
        this IServiceCollection services)
    {
        // No explicit config action anymore, config is in ChannelRegistry.
        // But we might want global settings later? For now, remove action.
        
        services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        services.AddSingleton<MessageDispatcher>();
        services.AddSingleton<RabbitMqRetryHandler>();
        services.AddSingleton<RabbitMqTopologyManager>();
        services.AddHostedService<RabbitMqConsumer>();
        
        return services;
    }
}
