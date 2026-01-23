using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox.Internal;

namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Contains the extension methods to enable/configure the outbox
/// </summary>
public static class PublicApiExtensions
{
    /// <summary>
    /// Registers the background services necessary for the outbox processing for the given DbContext.
    /// If you have multiple DbContexts, then this can be called once per DbContext.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext for which the background processing should be enabled.</typeparam>
    public static IServiceCollection AddOutboxPattern<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddSingleton<OutboxProcessor<TDbContext>>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessor<TDbContext>>());
        return services;
    }
    
    /// <summary>
    /// Registers the DbContext interceptor that is responsible for converting the messages to ef core entities for saving and triggering the outbox processor afterward for faster dispatch to the broker.
    /// </summary>
    /// <param name="serviceProvider">ServiceProvider that you get from the services.AddDbContext&lt;TDbContext&gt;((sp, c) => ..) call.</param>
    public static DbContextOptionsBuilder RegisterOutbox<TDbContext>(this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
        where TDbContext : DbContext, IOutboxDbContext
    {
        var outboxProcessor = serviceProvider.GetRequiredService<OutboxProcessor<TDbContext>>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        var messageSerializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var interceptor = new OutboxTriggerInterceptor<TDbContext>(outboxProcessor, messageSerializer, timeProvider);
        return builder.AddInterceptors(interceptor);
    }
    
    /// <summary>
    /// Adds the necessary outbox entities to the DB model.
    /// </summary>
    public static void AddOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.HasIndex(e => new { e.ProcessedAt, e.IsPoisoned, e.NextAttemptAt, e.CreatedAt })
                .HasFilter("[ProcessedAt] IS NULL AND [IsPoisoned] = 0");
        });
    }
}