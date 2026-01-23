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
        services.AddSingleton<ICloudEventBus, CloudEventBus>();
        return services;
    }
    
    /// <summary>
    /// Adds the console message sender for testing/development purposes.
    /// Messages are written to the console instead of being sent to a real message broker.
    /// </summary>
    public static IServiceCollection AddConsoleMessageSender(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSender, ConsoleMessageSender>();
        return services;
    }
}