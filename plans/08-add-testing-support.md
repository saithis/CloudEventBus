# Plan 08: Add Testing Support

## Priority: Foundation for Plans 09/10

## Depends On
- Core library with CloudEvents serialization (✅ implemented)
- Message type registry (✅ implemented)

## Problem

Testing applications that use CloudEventBus is difficult because:
- `ConsoleMessageSender` only logs to console, can't assert on sent messages
- Outbox processor runs asynchronously, hard to test transactional behavior
- No built-in test helpers for common assertions
- Integration tests require real RabbitMQ

## Goals (from overview)
- Make it easy for users to write tests for sending/receiving events/messages
- InMemoryMessageSender that collects messages for assertions
- FakeOutboxProcessor that processes synchronously for tests
- Test helpers like `bus.ShouldHavePublished<T>(predicate)`

---

## Part 1: InMemoryMessageSender

Create `src/Saithis.CloudEventBus/Testing/InMemoryMessageSender.cs`:

```csharp
using System.Collections.Concurrent;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Testing;

/// <summary>
/// A message sender that stores messages in memory for testing.
/// Thread-safe for parallel test execution.
/// </summary>
public class InMemoryMessageSender : IMessageSender
{
    private readonly ConcurrentBag<SentMessage> _messages = new();
    
    /// <summary>
    /// All messages that have been sent.
    /// </summary>
    public IReadOnlyCollection<SentMessage> SentMessages => _messages.ToArray();
    
    public Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        _messages.Add(new SentMessage(content, props, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Clears all sent messages. Call this in test setup/teardown.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
    }
}

/// <summary>
/// Represents a message that was sent via InMemoryMessageSender.
/// </summary>
public record SentMessage(byte[] Content, MessageProperties Properties, DateTimeOffset SentAt)
{
    /// <summary>
    /// Deserializes the message content to the specified type.
    /// </summary>
    public T? Deserialize<T>(IMessageDeserializer? deserializer = null)
    {
        if (deserializer != null)
        {
            return deserializer.Deserialize<T>(Content, CreateContext());
        }
        
        // Simple JSON deserialization fallback
        return System.Text.Json.JsonSerializer.Deserialize<T>(Content);
    }
    
    /// <summary>
    /// Gets the content as a string (assumes UTF-8 encoding).
    /// </summary>
    public string ContentAsString => System.Text.Encoding.UTF8.GetString(Content);
    
    private MessageContext CreateContext() => new()
    {
        Id = Properties.Id ?? "",
        Type = Properties.Type ?? "",
        Source = Properties.Source ?? "/",
        Time = Properties.Time,
        RawBody = Content
    };
}
```

---

## Part 2: Test Assertions Extension Methods

Create `src/Saithis.CloudEventBus/Testing/TestAssertions.cs`:

```csharp
using System.Text.Json;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Testing;

/// <summary>
/// Extension methods for testing message sending behavior.
/// </summary>
public static class TestAssertions
{
    /// <summary>
    /// Asserts that a message of the specified type was sent.
    /// </summary>
    public static SentMessage ShouldHaveSent<TMessage>(
        this InMemoryMessageSender sender,
        Func<TMessage, bool>? predicate = null)
    {
        var matching = sender.SentMessages
            .Where(m => MatchesType<TMessage>(m))
            .ToList();
        
        if (matching.Count == 0)
        {
            throw new AssertionException(
                $"Expected to find a sent message of type {typeof(TMessage).Name}, but none were found. " +
                $"Messages sent: [{string.Join(", ", sender.SentMessages.Select(m => m.Properties.Type))}]");
        }
        
        if (predicate != null)
        {
            var withPredicate = matching
                .Where(m => predicate(m.Deserialize<TMessage>()!))
                .ToList();
            
            if (withPredicate.Count == 0)
            {
                throw new AssertionException(
                    $"Found {matching.Count} message(s) of type {typeof(TMessage).Name}, " +
                    "but none matched the predicate.");
            }
            
            return withPredicate.First();
        }
        
        return matching.First();
    }
    
    /// <summary>
    /// Asserts that no message of the specified type was sent.
    /// </summary>
    public static void ShouldNotHaveSent<TMessage>(this InMemoryMessageSender sender)
    {
        var matching = sender.SentMessages.Where(m => MatchesType<TMessage>(m)).ToList();
        
        if (matching.Count > 0)
        {
            throw new AssertionException(
                $"Expected no messages of type {typeof(TMessage).Name} to be sent, " +
                $"but found {matching.Count}.");
        }
    }
    
    /// <summary>
    /// Asserts that exactly the specified number of messages were sent.
    /// </summary>
    public static void ShouldHaveSentCount(this InMemoryMessageSender sender, int expectedCount)
    {
        var actualCount = sender.SentMessages.Count;
        if (actualCount != expectedCount)
        {
            throw new AssertionException(
                $"Expected {expectedCount} message(s) to be sent, but found {actualCount}.");
        }
    }
    
    /// <summary>
    /// Asserts that no messages were sent.
    /// </summary>
    public static void ShouldNotHaveSentAny(this InMemoryMessageSender sender)
    {
        sender.ShouldHaveSentCount(0);
    }
    
    /// <summary>
    /// Gets all sent messages of the specified type.
    /// </summary>
    public static IReadOnlyList<SentMessage> GetSentMessages<TMessage>(this InMemoryMessageSender sender)
    {
        return sender.SentMessages.Where(m => MatchesType<TMessage>(m)).ToList();
    }
    
    private static bool MatchesType<TMessage>(SentMessage message)
    {
        // Try to match by CloudEvent type in properties
        var expectedType = typeof(TMessage).Name;
        
        // Check if it's a CloudEvents envelope (structured mode)
        try
        {
            var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(message.Content);
            if (envelope?.Type != null)
            {
                return envelope.Type.Contains(expectedType, StringComparison.OrdinalIgnoreCase)
                    || message.Properties.Type?.Contains(expectedType, StringComparison.OrdinalIgnoreCase) == true;
            }
        }
        catch
        {
            // Not a CloudEvents envelope, continue with other checks
        }
        
        // Check message properties
        if (message.Properties.Type?.Contains(expectedType, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        
        // Try to deserialize and see if it matches
        try
        {
            var deserialized = message.Deserialize<TMessage>();
            return deserialized != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Exception thrown when a test assertion fails.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
```

---

## Part 3: Synchronous Outbox Processor for Tests

Create `src/Saithis.CloudEventBus.EfCoreOutbox/Testing/SynchronousOutboxProcessor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox.Internal;

namespace Saithis.CloudEventBus.EfCoreOutbox.Testing;

/// <summary>
/// A synchronous outbox processor for testing.
/// Processes all pending messages immediately without background processing.
/// </summary>
public class SynchronousOutboxProcessor<TDbContext> where TDbContext : DbContext, IOutboxDbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    
    public SynchronousOutboxProcessor(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger.Instance;
    }
    
    /// <summary>
    /// Processes all pending outbox messages synchronously.
    /// Returns the number of messages processed.
    /// </summary>
    public async Task<int> ProcessAllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IMessageSender>();
        
        var messages = await dbContext.Set<OutboxMessageEntity>()
            .Where(x => x.ProcessedAt == null && !x.IsPoisoned)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        
        var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var processedCount = 0;
        
        foreach (var message in messages)
        {
            try
            {
                _logger.LogDebug("Processing outbox message {Id}", message.Id);
                await sender.SendAsync(message.Content, message.GetProperties(), cancellationToken);
                message.MarkAsProcessed(timeProvider);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {Id}", message.Id);
                throw; // In tests, we want failures to be visible
            }
        }
        
        await dbContext.SaveChangesAsync(cancellationToken);
        return processedCount;
    }
    
    /// <summary>
    /// Gets the count of pending (unprocessed) messages.
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        
        return await dbContext.Set<OutboxMessageEntity>()
            .CountAsync(x => x.ProcessedAt == null && !x.IsPoisoned, cancellationToken);
    }
}
```

---

## Part 4: Test Service Collection Extensions

Create `src/Saithis.CloudEventBus/Testing/TestingServiceCollectionExtensions.cs`:

```csharp
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
    /// The sender instance is registered as a singleton for easy access in tests.
    /// </summary>
    public static IServiceCollection AddInMemoryMessageSender(this IServiceCollection services)
    {
        var sender = new InMemoryMessageSender();
        services.AddSingleton(sender);
        services.AddSingleton<IMessageSender>(sender);
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
```

Add to `src/Saithis.CloudEventBus.EfCoreOutbox/Testing/OutboxTestingExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Saithis.CloudEventBus.EfCoreOutbox.Testing;

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
```

---

## Part 5: FakeTimeProvider for Testing

Create `src/Saithis.CloudEventBus/Testing/FakeTimeProvider.cs`:

```csharp
namespace Saithis.CloudEventBus.Testing;

/// <summary>
/// A controllable TimeProvider for testing time-dependent behavior.
/// </summary>
public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    
    public FakeTimeProvider() : this(DateTimeOffset.UtcNow) { }
    
    public FakeTimeProvider(DateTimeOffset initialTime)
    {
        _utcNow = initialTime;
    }
    
    public override DateTimeOffset GetUtcNow() => _utcNow;
    
    /// <summary>
    /// Advances time by the specified duration.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }
    
    /// <summary>
    /// Sets the current time to a specific value.
    /// </summary>
    public void SetUtcNow(DateTimeOffset time)
    {
        _utcNow = time;
    }
}
```

---

## Example Usage

### Unit Test for Direct Publishing

```csharp
public class PublishingTests
{
    [Fact]
    public async Task Should_publish_event_with_correct_type()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<OrderCreatedEvent>("orders.created"));
        services.AddInMemoryMessageSender();
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        // Act
        await bus.PublishDirectAsync(new OrderCreatedEvent { OrderId = "123" });
        
        // Assert
        var sent = sender.ShouldHaveSent<OrderCreatedEvent>(
            e => e.OrderId == "123");
        Assert.Equal("orders.created", sent.Properties.Type);
    }
}
```

### Integration Test with Outbox

```csharp
public class OutboxTests : IAsyncLifetime
{
    private ServiceProvider _provider;
    private InMemoryMessageSender _sender;
    
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("test"));
        services.AddCloudEventBus(bus => bus
            .AddMessage<OrderCreatedEvent>("orders.created"));
        services.AddInMemoryMessageSender();
        services.AddSynchronousOutboxProcessor<TestDbContext>();
        
        _provider = services.BuildServiceProvider();
        _sender = _provider.GetRequiredService<InMemoryMessageSender>();
    }
    
    [Fact]
    public async Task Should_send_message_after_outbox_processing()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
        
        // Act - Add to outbox
        db.OutboxMessages.Add(new OrderCreatedEvent { OrderId = "456" });
        await db.SaveChangesAsync();
        
        // Messages not sent yet
        _sender.ShouldNotHaveSentAny();
        
        // Process outbox
        var count = await processor.ProcessAllAsync();
        
        // Assert
        Assert.Equal(1, count);
        _sender.ShouldHaveSent<OrderCreatedEvent>(e => e.OrderId == "456");
    }
    
    public async Task DisposeAsync() => await _provider.DisposeAsync();
}
```

### Testing Time-Dependent Behavior

```csharp
[Fact]
public async Task Should_use_fake_time_for_timestamps()
{
    var fakeTime = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
    
    var services = new ServiceCollection();
    services.AddSingleton<TimeProvider>(fakeTime);
    services.AddCloudEventBus();
    services.AddInMemoryMessageSender();
    
    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<ICloudEventBus>();
    var sender = provider.GetRequiredService<InMemoryMessageSender>();
    
    await bus.PublishDirectAsync(new TestEvent());
    
    var sent = sender.SentMessages.First();
    Assert.Equal(fakeTime.GetUtcNow(), sent.Properties.Time);
}
```

---

## Files to Create

1. `src/Saithis.CloudEventBus/Testing/InMemoryMessageSender.cs`
2. `src/Saithis.CloudEventBus/Testing/TestAssertions.cs`
3. `src/Saithis.CloudEventBus/Testing/TestingServiceCollectionExtensions.cs`
4. `src/Saithis.CloudEventBus/Testing/FakeTimeProvider.cs`
5. `src/Saithis.CloudEventBus.EfCoreOutbox/Testing/SynchronousOutboxProcessor.cs`
6. `src/Saithis.CloudEventBus.EfCoreOutbox/Testing/OutboxTestingExtensions.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs` - Ensure TimeProvider registration doesn't override existing

---

## Testing the Testing Support

Write tests for the testing utilities themselves:

- `InMemoryMessageSender` captures messages correctly
- `ShouldHaveSent<T>` finds messages by type
- `ShouldHaveSent<T>` with predicate filters correctly
- `ShouldNotHaveSent<T>` throws when message exists
- `SynchronousOutboxProcessor` processes all pending messages
- `FakeTimeProvider` advances time correctly

---

## Future Enhancements

- `InMemoryMessageReceiver` for testing consumers (Plan 08)
- `TestMessageBuilder` for fluent message construction
- Automatic cleanup between tests via test framework integration
- Snapshot testing for message content
- Integration with popular assertion libraries (FluentAssertions, Shouldly)
