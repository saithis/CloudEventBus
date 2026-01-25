using System.Text;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox.Testing;
using Saithis.CloudEventBus.RabbitMq;
using TUnit.Core;

namespace CloudEventBus.Tests.Integration;

/// <summary>
/// End-to-end integration tests combining outbox pattern with RabbitMQ delivery
/// </summary>
[ClassDataSource<CombinedContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("RabbitMQ")] // Tests use shared exchange and may interfere
public class EndToEndTests(CombinedContainerFixture containers)
{
    [Test]
    public async Task Outbox_ToRabbitMq_FullFlow()
    {
        // Arrange - Setup full stack with PostgreSQL outbox and RabbitMQ sender
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        
        // Ensure database is created
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        // Setup RabbitMQ queue to receive the message
        var queueName = $"e2e-test-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act - Stage message in outbox via DbContext
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            // Add a test entity and an event in the same transaction
            var entity = new TestEntity 
            { 
                Name = "E2E Test Entity",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.TestEntities.Add(entity);
            
            // Stage the event
            dbContext.OutboxMessages.Add(new TestEvent { Data = "end-to-end test message" }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
            
            await dbContext.SaveChangesAsync();
        }
        
        // Process outbox - sends to RabbitMQ
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            var processedCount = await processor.ProcessAllAsync();
            processedCount.Should().Be(1);
        }
        
        // Wait for message delivery
        await Task.Delay(1000);
        
        // Assert - Message should be in RabbitMQ
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.Body.ToArray().Length.Should().BeGreaterThan(0);
        
        // Verify entity was also saved (transactional consistency)
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entities = await dbContext.TestEntities.ToListAsync();
            entities.Should().HaveCount(1);
            entities[0].Name.Should().Be("E2E Test Entity");
        }
    }

    [Test]
    public async Task DirectPublish_ToRabbitMq_FullFlow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<CustomerUpdatedEvent>("customer.updated"));
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Setup RabbitMQ queue
        var queueName = $"e2e-direct-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "customer.updated");
        
        // Act - Publish directly (no outbox)
        await bus.PublishDirectAsync(new CustomerUpdatedEvent 
        { 
            CustomerId = "CUST-123",
            Name = "John Doe"
        }, new MessageProperties
        {
            Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "customer.updated" }
        });
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.BasicProperties.Type.Should().Be("customer.updated");
    }

    [Test]
    public async Task Outbox_MultipleMessages_AllDeliveredToRabbitMq()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .AddMessage<OrderCreatedEvent>("order.created"));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        // Setup queues for different message types
        var testEventQueue = $"test-event-{Guid.NewGuid()}";
        var orderCreatedQueue = $"order-created-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: testEventQueue, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: testEventQueue, exchange: "test.exchange", routingKey: "test.event");
        
        await channel.QueueDeclareAsync(queue: orderCreatedQueue, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: orderCreatedQueue, exchange: "test.exchange", routingKey: "order.created");
        
        // Act - Stage multiple messages of different types
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 1" }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
            
            dbContext.OutboxMessages.Add(new OrderCreatedEvent 
            { 
                OrderId = "ORDER-1",
                Amount = 99.99m,
                CreatedAt = DateTimeOffset.UtcNow
            }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "order.created" }
            });
            
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 2" }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
            
            await dbContext.SaveChangesAsync();
        }
        
        // Process outbox
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            var processedCount = await processor.ProcessAllAsync();
            processedCount.Should().Be(3);
        }
        
        await Task.Delay(1000);
        
        // Assert - Check both queues
        var testEventCount = await channel.MessageCountAsync(testEventQueue);
        testEventCount.Should().Be(2u);
        
        var orderCreatedCount = await channel.MessageCountAsync(orderCreatedQueue);
        orderCreatedCount.Should().Be(1u);
    }

    [Test]
    public async Task Outbox_WithStructuredCloudEvents_DeliveredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Structured));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        var queueName = $"structured-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "structured message" }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
            await dbContext.SaveChangesAsync();
        }
        
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        
        // Verify it's structured CloudEvents (JSON envelope)
        var body = Encoding.UTF8.GetString(result!.Body.ToArray());
        body.Should().Contain("\"specversion\"");
        body.Should().Contain("\"type\"");
        body.Should().Contain("\"source\"");
        body.Should().Contain("\"data\"");
    }

    [Test]
    public async Task Outbox_WithBinaryCloudEvents_DeliveredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Binary));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        var queueName = $"binary-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "binary message" }, new MessageProperties
            {
                Extensions = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
            await dbContext.SaveChangesAsync();
        }
        
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        
        // Verify CloudEvents headers are present
        result!.BasicProperties.Headers.Should().NotBeNull();
        var headers = result.BasicProperties.Headers!;
        headers.Should().ContainKey("ce-specversion");
        headers.Should().ContainKey("ce-type");
        headers.Should().ContainKey("ce-source");
    }
}
