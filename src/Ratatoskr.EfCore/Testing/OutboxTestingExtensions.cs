using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ratatoskr.EfCore.Testing;

/// <summary>
/// Extension methods for testing with the outbox pattern.
/// </summary>
public static class OutboxTestingExtensions
{
    /// <summary>
    /// Adds a synchronous outbox processor for testing.
    /// Does NOT register the background processor.
    /// </summary>
    public static IServiceCollection AddSynchronousOutboxProcessor<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddScoped<SynchronousOutboxProcessor<TDbContext>>();
        return services;
    }
}
