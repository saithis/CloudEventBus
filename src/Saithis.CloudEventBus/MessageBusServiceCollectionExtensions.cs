using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.CloudEvents;
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
        
        // Freeze registry for optimal performance
        builder.TypeRegistry.Freeze();
        
        services.AddSingleton(builder.TypeRegistry);
        services.AddSingleton(builder.CloudEventsOptions);
        
        // Register TimeProvider if not already registered (allows test overrides)
        services.TryAddSingleton(TimeProvider.System);
        
        // Register inner serializer
        services.AddSingleton<JsonMessageSerializer>();
        
        // Register CloudEvents wrapper as the main serializer
        services.AddSingleton<IMessageSerializer>(sp => new CloudEventsSerializer(
            sp.GetRequiredService<JsonMessageSerializer>(),
            sp.GetRequiredService<CloudEventsOptions>(),
            sp.GetRequiredService<MessageTypeRegistry>(),
            sp.GetRequiredService<TimeProvider>()));
        
        services.AddSingleton<ICloudEventBus, CloudEventBus>();
        
        return services;
    }
    
    [Obsolete("Use AddCloudEventBus instead")]
    public static IServiceCollection AddMessageBus(this IServiceCollection services)
    {
        return services.AddCloudEventBus();
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