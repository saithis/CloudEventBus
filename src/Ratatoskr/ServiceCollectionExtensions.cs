using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ratatoskr.Core;
using Ratatoskr.Serializers.Json;
using Ratatoskr.Testing;

namespace Ratatoskr;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRatatoskr(
        this IServiceCollection services,
        Action<RatatoskrBuilder>? configure = null)
    {
        var builder = new RatatoskrBuilder(services);
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
        
        services.AddSingleton<IRatatoskr, Ratatoskr>();
        
        return services;
    }
}