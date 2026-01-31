using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;
using Saithis.CloudEventBus.Testing;

namespace Saithis.CloudEventBus;

public static class MessageBusServiceCollectionExtensions
{
    public static IServiceCollection AddCloudEventBus(
        this IServiceCollection services,
        Action<CloudEventBusBuilder>? configure = null)
    {
        var builder = new CloudEventBusBuilder(services);
        configure?.Invoke(builder);
        
        services.AddSingleton(builder.CloudEventsOptions);
        
        // Register TimeProvider if not already registered (allows test overrides)
        services.TryAddSingleton(TimeProvider.System);
        
        // Register message properties enricher
        services.AddSingleton<IMessagePropertiesEnricher, MessagePropertiesEnricher>();
        
        // Register ChannelRegistry
        services.AddSingleton(builder.ChannelRegistry);
        
        // Register serializer
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