using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;
using Saithis.CloudEventBus.Testing;

namespace Saithis.CloudEventBus;

public static class MessageBusServiceCollectionExtensions
{
    public static IServiceCollection AddMessageBus(this IServiceCollection services)
    {
        // TODO: split that up and use builder pattern
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddSingleton<IMessageSender, ConsoleMessageSender>();
        services.AddSingleton<ICloudEventBus, CloudEventBus>();
        return services;
    }
}