using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Builder for configuring the outbox pattern.
/// </summary>
public class OutboxBuilder<TDbContext> where TDbContext : DbContext, IOutboxDbContext
{
    internal IServiceCollection Services { get; }
    internal OutboxOptions Options { get; } = new();
    
    internal OutboxBuilder(IServiceCollection services)
    {
        Services = services;
    }
    
    /// <summary>
    /// Sets the polling interval for checking the database.
    /// </summary>
    public OutboxBuilder<TDbContext> WithPollingInterval(TimeSpan interval)
    {
        Options.PollingInterval = interval;
        return this;
    }
    
    /// <summary>
    /// Sets the batch size for processing messages.
    /// </summary>
    public OutboxBuilder<TDbContext> WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive");
        Options.BatchSize = batchSize;
        return this;
    }
    
    /// <summary>
    /// Sets the maximum number of retries before a message is marked as poisoned.
    /// </summary>
    public OutboxBuilder<TDbContext> WithMaxRetries(int maxRetries)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries cannot be negative");
        Options.MaxRetries = maxRetries;
        return this;
    }
    
    /// <summary>
    /// Configures all options via an action.
    /// </summary>
    public OutboxBuilder<TDbContext> Configure(Action<OutboxOptions> configure)
    {
        configure(Options);
        return this;
    }
}
