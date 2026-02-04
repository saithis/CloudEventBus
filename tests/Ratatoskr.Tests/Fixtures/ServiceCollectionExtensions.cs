using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ratatoskr.RabbitMq;
using Ratatoskr;
using Ratatoskr.Core;
using Ratatoskr.EfCore;
using Ratatoskr.EfCore.Internal;
using Ratatoskr.Testing;

namespace Ratatoskr.Tests.Fixtures;

/// <summary>
/// Helper methods for configuring services in tests
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Ratatoskr with InMemoryMessageSender for testing
    /// </summary>
    public static IServiceCollection AddTestRatatoskr(
        this IServiceCollection services,
        Action<RatatoskrBuilder>? configure = null)
    {
        services.AddRatatoskr(configure);
        services.AddInMemoryMessageSender();
        
        // Register RabbitMq transport enricher if not already registered
        services.TryAddSingleton<ITransportMessageMetadataEnricher, RabbitMqMessageMetadataEnricher>();
        
        return services;
    }

    /// <summary>
    /// Adds outbox pattern with synchronous processor for testing.
    /// Note: Does NOT register the background OutboxProcessor - use SynchronousOutboxProcessor instead.
    /// </summary>
    public static IServiceCollection AddTestOutbox<TDbContext>(
        this IServiceCollection services,
        Action<OutboxBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext, IOutboxDbContext
    {
        var builder = new OutboxBuilder<TDbContext>(services);
        configure?.Invoke(builder);
        
        // Register options
        services.AddSingleton(Options.Create(builder.Options));
        
        return services;
    }

    /// <summary>
    /// Adds RabbitMQ message sender configured with test container
    /// </summary>
    public static IServiceCollection AddTestRabbitMq(
        this IServiceCollection services,
        string connectionString,
        string? defaultExchange = "test")
    {
        return services;
    }

    /// <summary>
    /// Adds PostgreSQL DbContext configured with test container.
    /// Includes outbox interceptor for converting staged messages to entities.
    /// </summary>
    public static IServiceCollection AddTestDbContext(
        this IServiceCollection services,
        string connectionString,
        bool withOutboxInterceptor = true)
    {
        // First register the DbContext without interceptor
        services.AddDbContext<TestDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            
            // If outbox is needed, configure the interceptor
            if (withOutboxInterceptor)
            {
                var messageSerializer = sp.GetRequiredService<IMessageSerializer>();
                var enricher = sp.GetRequiredService<IMessagePropertiesEnricher>();
                var timeProvider = sp.GetRequiredService<TimeProvider>();
                var interceptor = new TestOutboxInterceptor<TestDbContext>(messageSerializer, enricher, timeProvider);
                options.AddInterceptors(interceptor);
            }
        });
        
        return services;
    }

    /// <summary>
    /// Test-specific outbox interceptor that doesn't trigger background processing.
    /// Converts staged messages to entities but doesn't call TriggerAsync.
    /// </summary>
    private class TestOutboxInterceptor<TDbContext>(
        IMessageSerializer messageSerializer,
        IMessagePropertiesEnricher enricher,
        TimeProvider timeProvider)
        : OutboxTriggerInterceptor<TDbContext>(null!, messageSerializer, enricher, timeProvider)
        where TDbContext : DbContext, IOutboxDbContext
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            // Override base to prevent outbox processor triggering
            return ValueTask.FromResult(result);
        }
    }
}
