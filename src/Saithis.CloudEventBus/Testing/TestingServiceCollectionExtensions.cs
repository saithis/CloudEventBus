using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Testing;

/// <summary>
/// Extension methods for configuring CloudEventBus for testing.
/// </summary>
public static class TestingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the in-memory message sender for testing.
    /// The sender instance is registered as a scoped service for test isolation.
    /// Note: If you need to access the sender for assertions, retrieve it from the same scope.
    /// </summary>
    public static IServiceCollection AddInMemoryMessageSender(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryMessageSender>();
        services.AddSingleton<IMessageSender>(sp => sp.GetRequiredService<InMemoryMessageSender>());
        return services;
    }
    
    /// <summary>
    /// Adds the in-memory message sender with a specific instance.
    /// Useful when you want to share the sender across test fixtures.
    /// </summary>
    public static IServiceCollection AddInMemoryMessageSender(
        this IServiceCollection services, 
        InMemoryMessageSender sender)
    {
        services.AddSingleton(sender);
        services.AddSingleton<IMessageSender>(sender);
        return services;
    }
}
