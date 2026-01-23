# Plan 06: Add Configuration Options Pattern

## Priority: 6 (Usability)

## Problem

The library has many hard-coded values with no way for users to configure them:

```csharp
// OutboxProcessor.cs
private readonly TimeSpan _dbCheckDelay = TimeSpan.FromSeconds(60);
private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(5);
private readonly TimeSpan _lockAcquireTimeout = TimeSpan.FromSeconds(60);
private const int BatchSize = 100;
private const int MaxRetries = 5; // Added in Plan 01
```

Users need to be able to customize:
- Polling intervals
- Batch sizes
- Retry behavior
- Lock timeouts

## Solution

Implement the standard .NET Options pattern with:
1. Options classes for each component
2. Integration with `IOptions<T>` / `IOptionsMonitor<T>`
3. Support for configuration from appsettings.json
4. Builder pattern methods for fluent configuration

### 1. Create Outbox Options

Create `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxOptions.cs`:

```csharp
namespace Saithis.CloudEventBus.EfCoreOutbox;

/// <summary>
/// Configuration options for the outbox pattern.
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// Section name in configuration files.
    /// </summary>
    public const string SectionName = "CloudEventBus:Outbox";
    
    /// <summary>
    /// How often to poll the database for unsent messages.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(60);
    
    /// <summary>
    /// How long to wait before restarting after a crash.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Maximum time to wait when acquiring the distributed lock.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan LockAcquireTimeout { get; set; } = TimeSpan.FromSeconds(60);
    
    /// <summary>
    /// Number of messages to process in each batch.
    /// Default: 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// Maximum number of retry attempts before marking a message as poisoned.
    /// Default: 5.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
    
    /// <summary>
    /// How long a message can be in "processing" state before it's considered stuck.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan StuckMessageThreshold { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Maximum backoff delay between retries.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Name of the distributed lock. Change this if you have multiple outboxes.
    /// Default: "OutboxProcessor".
    /// </summary>
    public string LockName { get; set; } = "OutboxProcessor";
}
```

### 2. Update OutboxProcessor to Use Options

Update `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxProcessor.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

internal class OutboxProcessor<TDbContext>(
    IServiceScopeFactory serviceScopeFactory, 
    IDistributedLockProvider distributedLockProvider, 
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor<TDbContext>> logger) 
    : BackgroundService where TDbContext : DbContext, IOutboxDbContext
{
    private readonly OutboxOptions _options = options.Value;
    private CancellationTokenSource _cts = new();
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting OutboxProcessor with options: {@Options}", _options);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxAsync(stoppingToken);
                await WaitTillDelayOverOrTriggeredAsync(stoppingToken);
            }
            catch (Exception e)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;
                logger.LogCritical(e, "OutboxProcessor crashed, trying to restart in {Delay}", 
                    _options.RestartDelay);
                await Task.Delay(_options.RestartDelay, timeProvider, stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    break;
            }
        }

        logger.LogInformation("Stopped OutboxProcessor");
    }

    private async Task ProcessOutboxAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("Trying to acquire distributed lock '{LockName}'", _options.LockName);
        await using IDistributedSynchronizationHandle? dLock =
            await distributedLockProvider.TryAcquireLockAsync(
                _options.LockName, 
                _options.LockAcquireTimeout,
                stoppingToken);

        if (dLock == null)
        {
            logger.LogInformation("Failed to acquire lock, outbox processing will be skipped");
            return;
        }

        logger.LogDebug("Distributed lock acquired");
        
        try
        {
            using IServiceScope serviceScope = serviceScopeFactory.CreateScope();

            var dbContext = serviceScope.ServiceProvider.GetRequiredService<TDbContext>();
            var sender = serviceScope.ServiceProvider.GetRequiredService<IMessageSender>();
            
            while (true)
            {
                logger.LogDebug("Checking outbox for unsent messages");
                var now = timeProvider.GetUtcNow();
                var stuckThreshold = now - _options.StuckMessageThreshold;
                
                var messages = await dbContext.Set<OutboxMessageEntity>()
                    .Where(x => x.ProcessedAt == null 
                             && !x.IsPoisoned
                             && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                             && (x.ProcessingStartedAt == null || x.ProcessingStartedAt < stuckThreshold))
                    .OrderBy(x => x.CreatedAt)
                    .Take(_options.BatchSize)
                    .ToArrayAsync(stoppingToken);

                logger.LogInformation("Found {Count} messages to send", messages.Length);
                if (messages.Length == 0)
                    return;

                // Mark as processing
                foreach (var message in messages)
                {
                    message.MarkAsProcessing(timeProvider);
                }
                await dbContext.SaveChangesAsync(stoppingToken);

                // Process messages
                foreach (var message in messages)
                {
                    try
                    {
                        logger.LogInformation("Processing message '{Id}'", message.Id);
                        await sender.SendAsync(message.Content, message.GetProperties(), stoppingToken);
                        message.MarkAsProcessed(timeProvider);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to send message '{Id}', attempt {Attempt}", 
                            message.Id, message.ErrorCount + 1);
                        message.PublishFailed(e.Message, timeProvider, 
                            _options.MaxRetries, _options.MaxRetryDelay);
                    }
                }
                
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while processing outbox messages");
        }
    }

    private async Task WaitTillDelayOverOrTriggeredAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("Waiting for {Delay}", _options.PollingInterval);

        if (_cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            ResetCts(stoppingToken);
        }

        await TaskHelper.DelayWithoutExceptionAsync(_options.PollingInterval, timeProvider, _cts.Token);

        if (_cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Delay was cancelled");
        }
    }
    
    // ... rest of the class unchanged ...
}
```

### 3. Update OutboxMessageEntity.PublishFailed

Update to accept max retry delay:

```csharp
public void PublishFailed(string error, TimeProvider timeProvider, int maxRetries, TimeSpan maxRetryDelay)
{
    ErrorCount++;
    Error = error.Length > 2000 ? error[..2000] : error;
    FailedAt = timeProvider.GetUtcNow();
    ProcessingStartedAt = null;
    
    if (ErrorCount >= maxRetries)
    {
        IsPoisoned = true;
        NextAttemptAt = null;
    }
    else
    {
        // Exponential backoff: 2^attempt seconds, capped at maxRetryDelay
        var delaySeconds = Math.Min(Math.Pow(2, ErrorCount), maxRetryDelay.TotalSeconds);
        NextAttemptAt = timeProvider.GetUtcNow().AddSeconds(delaySeconds);
    }
}
```

### 4. Create Outbox Builder

Create `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxBuilder.cs`:

```csharp
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
```

### 5. Update PublicApiExtensions

Update `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Saithis.CloudEventBus.EfCoreOutbox;

public static class PublicApiExtensions
{
    /// <summary>
    /// Registers the outbox pattern with default options.
    /// </summary>
    public static IServiceCollection AddOutboxPattern<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        return services.AddOutboxPattern<TDbContext>(configure: null);
    }
    
    /// <summary>
    /// Registers the outbox pattern with custom options via builder.
    /// </summary>
    public static IServiceCollection AddOutboxPattern<TDbContext>(
        this IServiceCollection services,
        Action<OutboxBuilder<TDbContext>>? configure)
        where TDbContext : DbContext, IOutboxDbContext
    {
        var builder = new OutboxBuilder<TDbContext>(services);
        configure?.Invoke(builder);
        
        // Register options
        services.AddSingleton(Options.Create(builder.Options));
        
        services.AddSingleton<OutboxProcessor<TDbContext>>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessor<TDbContext>>());
        
        return services;
    }
    
    /// <summary>
    /// Registers the outbox pattern with options from configuration.
    /// </summary>
    public static IServiceCollection AddOutboxPattern<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        
        services.AddSingleton<OutboxProcessor<TDbContext>>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessor<TDbContext>>());
        
        return services;
    }
    
    // ... rest unchanged ...
}
```

### 6. Add Required Package Reference

Update `src/Saithis.CloudEventBus.EfCoreOutbox/Saithis.CloudEventBus.EfCoreOutbox.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="DistributedLock.Core" Version="1.0.8" />
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.4" />
  <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
</ItemGroup>
```

---

## Example Usage

### Fluent Builder

```csharp
services.AddOutboxPattern<NotesDbContext>(outbox => outbox
    .WithPollingInterval(TimeSpan.FromSeconds(30))
    .WithBatchSize(50)
    .WithMaxRetries(3)
    .Configure(options =>
    {
        options.LockName = "NotesOutbox";
        options.MaxRetryDelay = TimeSpan.FromMinutes(2);
    }));
```

### From Configuration

```csharp
services.AddOutboxPattern<NotesDbContext>(builder.Configuration);
```

appsettings.json:
```json
{
  "CloudEventBus": {
    "Outbox": {
      "PollingInterval": "00:00:30",
      "BatchSize": 50,
      "MaxRetries": 3,
      "MaxRetryDelay": "00:02:00",
      "LockName": "NotesOutbox"
    }
  }
}
```

---

## Files to Create

1. `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxOptions.cs`
2. `src/Saithis.CloudEventBus.EfCoreOutbox/OutboxBuilder.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxProcessor.cs` - Use IOptions
2. `src/Saithis.CloudEventBus.EfCoreOutbox/Internal/OutboxMessageEntity.cs` - Update PublishFailed signature
3. `src/Saithis.CloudEventBus.EfCoreOutbox/PublicApiExtensions.cs` - Add overloads
4. `src/Saithis.CloudEventBus.EfCoreOutbox/Saithis.CloudEventBus.EfCoreOutbox.csproj` - Add package refs

---

## Testing Considerations

- Test default values are applied correctly
- Test fluent builder overrides defaults
- Test configuration binding works
- Test validation of invalid values (negative batch size, etc.)
- Test options are logged on startup
