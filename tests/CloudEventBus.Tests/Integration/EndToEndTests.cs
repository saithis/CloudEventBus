using System.Text;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCore.Testing;
using Saithis.CloudEventBus.RabbitMq;
using Saithis.CloudEventBus.RabbitMq.Config;
using TUnit.Core;

namespace CloudEventBus.Tests.Integration;

/// <summary>
/// End-to-end integration tests combining outbox pattern with RabbitMQ delivery
/// </summary>
[ClassDataSource<CombinedContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("RabbitMQ")] // Tests use shared exchange and may interfere
public class EndToEndTests(CombinedContainerFixture containers)
{
    private const string TestEventType = "test.event.basic";
    private const string TestExchange = "test";

    [Test]
    public async Task Outbox_ToRabbitMq_FullFlow()
    {
        // Arrange - Setup full stack with PostgreSQL outbox and RabbitMQ sender
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddEventPublishChannel(TestExchange, c => c.Produces<TestEvent>()));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
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
        await channel.QueueBindAsync(queue: queueName, exchange: TestExchange, routingKey: TestEventType);
        
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
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = TestEventType }
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
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddEventPublishChannel(TestExchange, c => c.Produces<CustomerUpdatedEvent>()));
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Setup RabbitMQ queue
        var queueName = $"e2e-direct-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        
        // CustomerUpdatedEvent has CloudEvent attribute "customer.updated"
        await channel.QueueBindAsync(queue: queueName, exchange: TestExchange, routingKey: "customer.updated");
        
        // Act - Publish directly (no outbox)
        await bus.PublishDirectAsync(new CustomerUpdatedEvent 
        { 
            CustomerId = "CUST-123",
            Name = "John Doe"
        }, new MessageProperties
        {
            TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "customer.updated" }
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
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddEventPublishChannel(TestExchange, c => c
                .Produces<TestEvent>()
                .Produces<OrderCreatedEvent>()));
                
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
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
        await channel.QueueBindAsync(queue: testEventQueue, exchange: TestExchange, routingKey: TestEventType);
        
        await channel.QueueDeclareAsync(queue: orderCreatedQueue, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: orderCreatedQueue, exchange: TestExchange, routingKey: "order.created");
        
        // Act - Stage multiple messages of different types
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 1" }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = TestEventType }
            });
            
            dbContext.OutboxMessages.Add(new OrderCreatedEvent 
            { 
                OrderId = "ORDER-1",
                Amount = 99.99m,
                CreatedAt = DateTimeOffset.UtcNow
            }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "order.created" }
            });
            
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 2" }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = TestEventType }
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
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddEventPublishChannel(TestExchange, c => c.Produces<TestEvent>())
            .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Structured));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
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
        await channel.QueueBindAsync(queue: queueName, exchange: TestExchange, routingKey: TestEventType);
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "structured message" }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = TestEventType }
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
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddEventPublishChannel(TestExchange, c => c.Produces<TestEvent>())
            .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Binary));
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString);
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
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
        await channel.QueueBindAsync(queue: queueName, exchange: TestExchange, routingKey: TestEventType);
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "binary message" }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = TestEventType }
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
        
        // Verify CloudEvents headers are present (AMQP binding uses cloudEvents_ prefix)
        result!.BasicProperties.Headers.Should().NotBeNull();
        var headers = result.BasicProperties.Headers!;
        headers.Should().ContainKey("cloudEvents_specversion");
        headers.Should().ContainKey("cloudEvents_type");
        headers.Should().ContainKey("cloudEvents_source");
    }

    [Test]
    public async Task PublishAndConsume_DirectPublish_HandlerReceivesMessage()
    {
        // Arrange
        var queueName = $"e2e-consume-{Guid.NewGuid()}";
        var handler = new TestEventHandler();
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
        services.AddSingleton(handler);
        services.AddTestRabbitMq(containers.RabbitMqConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);

        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Setup queue - handled by consumer, but also need to ensure publishing works.
        // DirectPublishAsync sends with routing key. If using default exchange (""), routing key must match queue name.
        // We need to ensure message has correct RoutingKey in metadata if publishing direct to queue?
        // Wait, PublishDirectAsync uses "transportMetadata: { [RabbitMqMessageSender.RoutingKeyExtensionKey] = ... }"
        // If we set it to queueName, it goes to queueName.
        
        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message
            await bus.PublishDirectAsync(new TestEvent { Id = "e2e-123", Data = "end-to-end test" }, 
                new MessageProperties
                {
                    // Publish direct to the queue name (acting as command)
                    TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = queueName }
                });
            
            // Wait for message to be consumed
            await Task.Delay(1500);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("e2e-123");
            handler.HandledMessages[0].Data.Should().Be("end-to-end test");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PublishAndConsume_OutboxToConsumer_CompleteFlow()
    {
        // Arrange
        var queueName = $"e2e-outbox-consume-{Guid.NewGuid()}";
        var handler = new TestEventHandler();
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
             .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
        services.AddSingleton(handler);
        services.AddTestDbContext(containers.PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();
        services.AddTestRabbitMq(containers.RabbitMqConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        // Setup database
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        // Queue setup by consumer start
        
        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Stage message in outbox
            using (var scope = provider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                dbContext.OutboxMessages.Add(
                    new TestEvent { Id = "outbox-e2e-123", Data = "outbox to consumer test" },
                    new MessageProperties
                    {
                        // Route directly to queue name
                        TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = queueName }
                    });
                await dbContext.SaveChangesAsync();
            }
            
            // Process outbox to send to RabbitMQ
            using (var scope = provider.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
                var processedCount = await processor.ProcessAllAsync();
                processedCount.Should().Be(1);
            }
            
            // Wait for message to be consumed
            await Task.Delay(1500);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("outbox-e2e-123");
            handler.HandledMessages[0].Data.Should().Be("outbox to consumer test");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PublishAndConsume_MultipleHandlers_AllHandlersCalled()
    {
        // Arrange
        var queueName = $"e2e-multi-{Guid.NewGuid()}";
        var handler1 = new TestEventHandler();
        var handler2 = new SecondTestEventHandler();
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>()
            .AddHandler<TestEvent, SecondTestEventHandler>());
        services.AddSingleton(handler1);
        services.AddSingleton(handler2);
        services.AddTestRabbitMq(containers.RabbitMqConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act
            await bus.PublishDirectAsync(new TestEvent { Id = "multi-123", Data = "multi-handler test" },
                new MessageProperties
                {
                    TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = queueName }
                });
            
            await Task.Delay(1500);
            
            // Assert - Both handlers received the message
            handler1.HandledMessages.Should().HaveCount(1);
            handler1.HandledMessages[0].Id.Should().Be("multi-123");
            
            handler2.HandledMessages.Should().HaveCount(1);
            handler2.HandledMessages[0].Id.Should().Be("multi-123");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PublishAndConsume_WithCloudEventsBinary_HandlerReceivesCorrectData()
    {
        // Arrange
        var queueName = $"e2e-binary-{Guid.NewGuid()}";
        var handler = new TestEventHandler();
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>()
            .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Binary));
        services.AddSingleton(handler);
        services.AddTestRabbitMq(containers.RabbitMqConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var bus = provider.GetRequiredService<ICloudEventBus>();

        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act
            await bus.PublishDirectAsync(new TestEvent { Id = "binary-123", Data = "binary mode test" },
                new MessageProperties
                {
                    TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = queueName }
                });
            
            await Task.Delay(1500);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("binary-123");
            handler.HandledMessages[0].Data.Should().Be("binary mode test");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PublishAndConsume_MultipleMessages_AllProcessed()
    {
        // Arrange
        var queueName = $"e2e-multiple-{Guid.NewGuid()}";
        var handler = new TestEventHandler();
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .Prefetch(10)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
        services.AddSingleton(handler);
        services.AddTestRabbitMq(containers.RabbitMqConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish multiple messages
            for (int i = 1; i <= 10; i++)
            {
                await bus.PublishDirectAsync(
                    new TestEvent { Id = i.ToString(), Data = $"message {i}" },
                    new MessageProperties
                    {
                        TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = queueName }
                    });
            }
            
            await Task.Delay(2500);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(10);
            handler.HandledMessages.Select(m => m.Id).Should().BeEquivalentTo(
                ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
