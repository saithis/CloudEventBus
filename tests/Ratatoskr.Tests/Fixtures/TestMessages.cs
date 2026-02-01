using Ratatoskr;
using Ratatoskr.Core;

namespace Ratatoskr.Tests.Fixtures;

/// <summary>
/// Test event with CloudEvent attribute
/// </summary>
[RatatoskrMessage("test.event")]
public record TestEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// Test event without attribute (for registry tests)
/// </summary>
[RatatoskrMessage("order.created")]
public record OrderCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Test event with custom source
/// </summary>
[RatatoskrMessage("customer.updated", Source = "/test-service")]
public record CustomerUpdatedEvent
{
    public string CustomerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Test entity for database operations
/// </summary>
public class TestEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

// Test handler implementations for message consuming tests
public class TestEventHandler : IMessageHandler<TestEvent>
{
    public List<TestEvent> HandledMessages { get; } = new();
    
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        HandledMessages.Add(message);
        return Task.CompletedTask;
    }
}

public class SecondTestEventHandler : IMessageHandler<TestEvent>
{
    public List<TestEvent> HandledMessages { get; } = new();
    
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        HandledMessages.Add(message);
        return Task.CompletedTask;
    }
}

public class OrderCreatedHandler : IMessageHandler<OrderCreatedEvent>
{
    public List<OrderCreatedEvent> HandledMessages { get; } = new();
    
    public Task HandleAsync(OrderCreatedEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        HandledMessages.Add(message);
        return Task.CompletedTask;
    }
}

public class ThrowingTestEventHandler : IMessageHandler<TestEvent>
{
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Handler failed intentionally");
    }
}

public class SlowTestEventHandler : IMessageHandler<TestEvent>
{
    private int _processingCount;
    
    public int ProcessingCount => _processingCount;
    
    public async Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _processingCount);
        await Task.Delay(500, cancellationToken); // Simulate slow processing
    }
}
