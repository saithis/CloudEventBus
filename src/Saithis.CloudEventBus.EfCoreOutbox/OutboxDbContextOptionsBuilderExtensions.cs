using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Saithis.CloudEventBus.EfCoreOutbox;

public static class OutboxDbContextOptionsBuilderExtensions
{
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
}