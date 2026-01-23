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
        services.AddSingleton<IMessageSender, RabbitMqMessageSender>();
        
        return services;
    }
}
