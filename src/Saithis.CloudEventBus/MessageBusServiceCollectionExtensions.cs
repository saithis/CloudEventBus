using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Saithis.CloudEventBus;

public static class MessageBusServiceCollectionExtensions
{
    public static IServiceCollection AddMessageBus(this IServiceCollection services)
    {
        // TODO: split that up and use builder pattern
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddSingleton<IMessageSender, ConsoleMessageSender>();
        return services;
    }
}