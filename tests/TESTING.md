# CloudEventBus Testing Guide

## Quick Start

```bash
# Run all tests
cd tests/CloudEventBus.Tests
dotnet run

# Expected results: 64+ passing tests
# First run downloads Docker images (PostgreSQL, RabbitMQ)
```

## Test Structure

We use **TUnit** as the test framework and **AwesomeAssertions** for fluent assertions.

### Why These Tools?

- **TUnit**: Modern, fast, parallel-first .NET test framework
- **AwesomeAssertions**: Open-source fork of FluentAssertions with identical API

## Writing New Tests

### Basic Test Structure

```csharp
using AwesomeAssertions;
using TUnit.Core;

public class MyTests
{
    [Test]
    public async Task MethodName_Scenario_ExpectedBehavior()
    {
        // Arrange
        var sut = new SystemUnderTest();
        
        // Act
        var result = await sut.DoSomethingAsync();
        
        // Assert
        result.Should().Be(expectedValue);
    }
}
```

### Using Test Fixtures

For tests that need PostgreSQL:

```csharp
[ClassDataSource<PostgresContainerFixture>(Shared = SharedType.PerTestSession)]
public class MyDatabaseTests(PostgresContainerFixture postgres)
{
    [Test]
    public async Task MyTest()
    {
        // Use postgres.ConnectionString
        var services = new ServiceCollection();
        services.AddTestDbContext(postgres.ConnectionString);
        // ...
    }
}
```

For tests that need RabbitMQ:

```csharp
[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerTestSession)]
public class MyRabbitMqTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    public async Task MyTest()
    {
        // Use rabbitMq.ConnectionString
        var services = new ServiceCollection();
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        // ...
    }
}
```

For tests that need both:

```csharp
[ClassDataSource<CombinedContainerFixture>(Shared = SharedType.PerTestSession)]
public class MyE2ETests(CombinedContainerFixture containers)
{
    [Test]
    public async Task MyTest()
    {
        // Use containers.PostgresConnectionString
        // Use containers.RabbitMqConnectionString
    }
}
```

### Common Assertions

```csharp
// Values
result.Should().Be(expected);
result.Should().NotBeNull();
value.Should().BeGreaterThan(0);

// Collections
list.Should().HaveCount(3);
list.Should().Contain(item);
dictionary.Should().ContainKey("key");

// Strings
name.Should().Be("expected");
name.Should().Contain("substring");
name.Should().StartWith("prefix");

// Booleans
flag.Should().BeTrue();
flag.Should().BeFalse();

// Exceptions
Action act = () => ThrowingMethod();
act.Should().Throw<InvalidOperationException>()
    .WithMessage("*expected message*");

// Async exceptions
Func<Task> act = async () => await AsyncMethod();
await act.Should().ThrowAsync<InvalidOperationException>();
```

### Testing with InMemoryMessageSender

```csharp
[Test]
public async Task PublishAsync_SendsMessage()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddTestCloudEventBus(bus => bus
        .AddMessage<MyEvent>("my.event"));
    
    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<ICloudEventBus>();
    
    // Create scope to get sender
    using var scope = provider.CreateScope();
    var sender = scope.ServiceProvider.GetRequiredService<InMemoryMessageSender>();
    
    // Act
    await bus.PublishDirectAsync(new MyEvent { Data = "test" });
    
    // Assert
    sender.SentMessages.Should().HaveCount(1);
    var message = sender.SentMessages.First();
    message.Properties.Type.Should().Be("my.event");
}
```

### Testing Outbox Pattern

```csharp
[ClassDataSource<PostgresContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("PostgreSQL")] // Sequential execution
public class MyOutboxTests(PostgresContainerFixture postgres)
{
    [Test]
    public async Task Outbox_ProcessesMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTestCloudEventBus(bus => bus
            .AddMessage<MyEvent>("my.event"));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>();
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }
        
        // Act - Stage messages
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.OutboxMessages.Add(new MyEvent { Data = "test" });
            await db.SaveChangesAsync();
        }
        
        // Process outbox
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider
                .GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            var count = await processor.ProcessAllAsync();
            
            count.Should().Be(1);
        }
        
        // Assert - Check sender
        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider
                .GetRequiredService<InMemoryMessageSender>();
            sender.SentMessages.Should().HaveCount(1);
        }
    }
}
```

### Testing with FakeTimeProvider

```csharp
using Microsoft.Extensions.Time.Testing;

[Test]
public void Entity_WithFakeTime_UsesControlledTime()
{
    // Arrange
    var fakeTime = new FakeTimeProvider(
        new DateTimeOffset(2025, 1, 24, 12, 0, 0, TimeSpan.Zero));
    
    var entity = OutboxMessageEntity.Create(
        "test"u8.ToArray(), 
        new MessageProperties(), 
        fakeTime);
    
    // Assert
    entity.CreatedAt.Should().Be(fakeTime.GetUtcNow());
    
    // Advance time
    fakeTime.Advance(TimeSpan.FromMinutes(5));
    
    entity.MarkAsProcessing(fakeTime);
    entity.ProcessingStartedAt.Should().Be(fakeTime.GetUtcNow());
}
```

## Best Practices

1. **Test Independence**: Each test creates its own `ServiceProvider`
2. **Container Reuse**: Fixtures are shared per session for performance
3. **Sequential Integration Tests**: Use `[NotInParallel]` for tests using shared resources
4. **Descriptive Names**: Follow `Method_Scenario_ExpectedBehavior` pattern
5. **Scoped Access**: Always get `InMemoryMessageSender` from the scope you're testing in

## Troubleshooting

### Tests fail intermittently
- Integration tests may need `[NotInParallel]` attribute
- Increase delays in RabbitMQ tests if messages aren't delivered yet

### Can't access internal types
- EfCoreOutbox has `[InternalsVisibleTo("CloudEventBus.Tests")]`
- If adding new test projects, update the attribute

### TestContainers slow
- First run downloads images (one-time)
- Containers are reused across all tests (fast subsequent runs)
- Containers are cleaned up automatically after test session

## Performance

- Unit tests: < 1 second
- Integration tests with containers: 10-15 seconds total
- Parallel execution for unit tests
- Sequential execution for integration tests (to avoid conflicts)

## Further Reading

- [TUnit Documentation](https://tunit.dev/)
- [FluentAssertions Documentation](https://fluentassertions.com/) (use for AwesomeAssertions)
- [TestContainers Documentation](https://dotnet.testcontainers.org/)
