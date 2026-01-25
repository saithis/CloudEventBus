using Saithis.CloudEventBus;

namespace CloudEventBus.Tests.Fixtures;

/// <summary>
/// Test event with CloudEvent attribute
/// </summary>
[CloudEvent("test.event.basic")]
public record TestEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// Test event without attribute (for registry tests)
/// </summary>
public record OrderCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Test event with custom source
/// </summary>
[CloudEvent("customer.updated", Source = "/test-service")]
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
