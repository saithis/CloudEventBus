using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Saithis.CloudEventBus.EfCoreOutbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxPattern<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddSingleton<OutboxProcessor<TDbContext>>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessor<TDbContext>>());
        return services;
    }
}