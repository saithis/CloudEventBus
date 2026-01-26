using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessageSender(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null,
        Action<CloudEventsAmqpOptions>? configureCloudEvents = null)
    {
        var options = new RabbitMqOptions();
        configure?.Invoke(options);
        
        var cloudEventsOptions = new CloudEventsAmqpOptions();
        configureCloudEvents?.Invoke(cloudEventsOptions);
        
        services.AddSingleton(options);
        services.AddSingleton(cloudEventsOptions);
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
        Action<RabbitMqConsumerOptions> configure,
        Action<CloudEventsAmqpOptions>? configureCloudEvents = null)
    {
        var options = new RabbitMqConsumerOptions();
        configure(options);
        
        var cloudEventsOptions = new CloudEventsAmqpOptions();
        configureCloudEvents?.Invoke(cloudEventsOptions);
        
        services.AddSingleton(options);
        services.AddSingleton(cloudEventsOptions);
        services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        services.AddSingleton<MessageDispatcher>();
        services.AddHostedService<RabbitMqConsumer>();
        
        return services;
    }
}
