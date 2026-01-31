using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Configuration;
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
        
        // Freeze registries for optimal performance
        builder.TypeRegistry.Freeze();
        builder.HandlerRegistry.Freeze();
        
        services.AddSingleton(builder.TypeRegistry);
        services.AddSingleton(builder.HandlerRegistry);
        services.AddSingleton(builder.ProductionRegistry);
        services.AddSingleton(builder.ConsumptionRegistry);
        services.AddSingleton(builder.CloudEventsOptions);
        
        // Register TimeProvider if not already registered (allows test overrides)
        services.TryAddSingleton(TimeProvider.System);
        
        // Register message properties enricher
        services.AddSingleton<IMessagePropertiesEnricher, MessagePropertiesEnricher>();
        
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