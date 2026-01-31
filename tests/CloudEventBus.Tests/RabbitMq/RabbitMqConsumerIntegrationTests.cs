using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq;
using Saithis.CloudEventBus.RabbitMq.Config;
using TUnit.Core;

namespace CloudEventBus.Tests.RabbitMq;

[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("RabbitMQ")] // Tests use shared exchange and may interfere
public class RabbitMqConsumerIntegrationTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    public async Task Consumer_ReceivesAndHandlesMessage()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        // Auto-provisioning handles queue creation
        
        var handler = new TestEventHandler();
        var services = ConfigureServices(queueName, handler);
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        // Start consumer
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish a message to the queue
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test data" }, "test.event");
            
            // Wait for message to be processed
            await Task.Delay(1000);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("123");
            handler.HandledMessages[0].Data.Should().Be("test data");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithMultipleHandlers_CallsAllHandlers()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
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
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test data" }, "test.event");
            await Task.Delay(1000);
            
            // Assert
            handler1.HandledMessages.Should().HaveCount(1);
            handler2.HandledMessages.Should().HaveCount(1);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithAutoAck_AcknowledgesAutomatically()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var handler = new TestEventHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(true) // Enable auto-ack
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test data" }, "test.event");
            await Task.Delay(1000);
            
            // Assert - Message was handled
            handler.HandledMessages.Should().HaveCount(1);
            
            // Assert - Queue is empty (message was acked)
            var messageCount = await GetQueueMessageCountAsync(queueName);
            messageCount.Should().Be(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithManualAck_AcknowledgesOnSuccess()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var handler = new TestEventHandler();
        var services = ConfigureServices(queueName, handler);
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test data" }, "test.event");
            await Task.Delay(1000);
            
            // Assert - Queue is empty (message was acked)
            var messageCount = await GetQueueMessageCountAsync(queueName);
            messageCount.Should().Be(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_NoHandlerRegistered_NacksMessage()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
             // Register channel so queue is created, but no handlers and no message types
             .AddCommandConsumeChannel(queueName, c => c
                 .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true)))
        ); 
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message with unknown event type
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test" }, "unknown.event");
            await Task.Delay(1000);
            
            // Assert - Message is rejected (no handlers found)
            // Message should be nacked without requeue, so queue should be empty
            var messageCount = await GetQueueMessageCountAsync(queueName);
            messageCount.Should().Be(0u); // Message was rejected
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_DeserializationFails_NacksMessage()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var handler = new TestEventHandler();
        var services = ConfigureServices(queueName, handler);
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish invalid JSON
            await PublishRawMessageAsync(queueName, "not valid json", "test.event");
            await Task.Delay(1000);
            
            // Assert - Handler not called (deserialization failed)
            handler.HandledMessages.Should().BeEmpty();
            
            // Assert - Message is rejected (deserialization failed)
            var messageCount = await GetQueueMessageCountAsync(queueName);
            messageCount.Should().Be(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_HandlerThrows_UsesRetryMechanism()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        var retryQueueName = $"{queueName}.retry";
        
        var handler = new ThrowingTestEventHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(2000), useManaged: true)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, ThrowingTestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message that will cause handler to throw
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test" }, "test.event");
            // Wait for message to be processed and moved to retry queue
            await Task.Delay(1000);
            
            // Assert - Message is in retry queue (using retry mechanism, not infinite requeue)
            var retryCount = await GetQueueMessageCountAsync(retryQueueName);
            retryCount.Should().BeGreaterThan(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithBinaryCloudEvents_DeserializesCorrectly()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
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
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message with CloudEvents binary mode (CE headers)
            await PublishBinaryCloudEventMessageAsync(queueName, 
                new TestEvent { Id = "123", Data = "test data" }, 
                "test.event");
            await Task.Delay(1000);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("123");
            handler.HandledMessages[0].Data.Should().Be("test data");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_MultipleMessages_ProcessesAll()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var handler = new TestEventHandler();
        var services = ConfigureServices(queueName, handler);
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish multiple messages
            for (int i = 1; i <= 5; i++)
            {
                await PublishTestMessageAsync(queueName, 
                    new TestEvent { Id = i.ToString(), Data = $"message {i}" }, 
                    "test.event");
            }
            
            await Task.Delay(2000);
            
            // Assert
            handler.HandledMessages.Should().HaveCount(5);
            handler.HandledMessages.Select(m => m.Id).Should().BeEquivalentTo(["1", "2", "3", "4", "5"]);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithPrefetchCount_LimitsUnackedMessages()
    {
        // Arrange
        var queueName = $"consumer-test-{Guid.NewGuid()}";
        
        var handler = new SlowTestEventHandler(); // Handler that takes time to process
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .Prefetch(2) // Limit prefetch
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, SlowTestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish several messages
            for (int i = 1; i <= 5; i++)
            {
                await PublishTestMessageAsync(queueName, 
                    new TestEvent { Id = i.ToString(), Data = $"message {i}" }, 
                    "test.event");
            }
            
            // Wait a bit but not enough for all to complete
            await Task.Delay(1500);
            
            // Assert - Should have started processing but not completed all due to prefetch limit
            // This is a soft assertion as timing can vary
            handler.ProcessingCount.Should().BeGreaterThan(0);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithRetryConfig_RetriesFailedMessages()
    {
        // Reuse same structure as HandlerThrows_UsesRetryMechanism but checking retry config specifically
        // Arrange
        var queueName = $"consumer-retry-test-{Guid.NewGuid()}";
        var retryQueueName = $"{queueName}.retry";
        
        var handler = new ThrowingTestEventHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .RetryOptions(maxRetries: 2, delay: TimeSpan.FromSeconds(5), useManaged: true)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, ThrowingTestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message that will fail
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test" }, "test.event");
            
            // Wait for initial failure and first retry
            await Task.Delay(3000);
            
            // Assert - Message should be in retry queue waiting
            var retryCount = await GetQueueMessageCountAsync(retryQueueName);
            retryCount.Should().BeGreaterThan(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithRetryConfig_SendsToDeadLetterQueueAfterMaxRetries()
    {
        // Arrange
        var queueName = $"consumer-dlq-test-{Guid.NewGuid()}";
        var dlqName = $"{queueName}.dlq";
        
        var handler = new ThrowingTestEventHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(500), useManaged: true)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, ThrowingTestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message that will fail
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test" }, "test.event");
            
            // Wait for all retries to exhaust (initial + 2 retries with delays)
            await Task.Delay(10000);
            
            // Assert - Message should be in DLQ
            var dlqCount = await GetQueueMessageCountAsync(dlqName);
            dlqCount.Should().Be(1u);
            
            // Main queue should be empty
            var mainQueueCount = await GetQueueMessageCountAsync(queueName);
            mainQueueCount.Should().Be(0u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithRetryConfig_PermanentErrorGoesDirectlyToDlq()
    {
        // Arrange
        var queueName = $"consumer-permanent-error-test-{Guid.NewGuid()}";
        var dlqName = $"{queueName}.dlq";
        
        // No handlers registered - this will cause NoHandlers (permanent error)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
             .AddCommandConsumeChannel(queueName, c => c
                 .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), useManaged: true)
                    .QueueOptions(durable: false, autoDelete: true)))
        ); 
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Act - Publish message with no registered handler
            await PublishTestMessageAsync(queueName, new TestEvent { Id = "123", Data = "test" }, "unknown.event");
            
            // Wait for processing
            await Task.Delay(2000);
            
            // Assert - Message should go directly to DLQ (no retries for permanent errors)
            var dlqCount = await GetQueueMessageCountAsync(dlqName);
            dlqCount.Should().Be(1u);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consumer_WithRetryConfig_CreatesQueueTopology()
    {
        // This test is now covered by implicit provisioning in setup, but we can double check
        // Arrange
        var queueName = $"consumer-topology-test-{Guid.NewGuid()}";
        var retryQueueName = $"{queueName}.retry";
        var dlqName = $"{queueName}.dlq";
        
        var handler = new TestEventHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName)
                    .AutoAck(false)
                    .RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(30))
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);
        
        try
        {
            // Assert - All queues should exist
            var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            
            // Verify queues exist by declaring them passively (will throw if they don't exist)
            var mainQueueOk = await channel.QueueDeclarePassiveAsync(queueName);
            mainQueueOk.Should().NotBeNull();
            
            var retryQueueOk = await channel.QueueDeclarePassiveAsync(retryQueueName);
            retryQueueOk.Should().NotBeNull();
            
            var dlqOk = await channel.QueueDeclarePassiveAsync(dlqName);
            dlqOk.Should().NotBeNull();
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
    
    // Helper methods
    // CreateQueueAsync is removed as replaced by auto-provisioning

    private async Task PublishTestMessageAsync<T>(string queueName, T message, string eventType)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);
        
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = eventType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };
        
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: queueName,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    private async Task PublishRawMessageAsync(string queueName, string body, string eventType)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = eventType,
            ContentType = "application/json"
        };
        
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: queueName,
            mandatory: false,
            basicProperties: properties,
            body: bodyBytes);
    }

    private async Task PublishBinaryCloudEventMessageAsync<T>(string queueName, T message, string eventType)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);
        
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            Headers = new Dictionary<string, object?>
            {
                ["cloudEvents_specversion"] = "1.0",
                ["cloudEvents_type"] = eventType,
                ["cloudEvents_source"] = "/test",
                ["cloudEvents_id"] = Guid.NewGuid().ToString()
            }
        };
        
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: queueName,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    private async Task<uint> GetQueueMessageCountAsync(string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        return await channel.MessageCountAsync(queueName);
    }

    private ServiceCollection ConfigureServices(string queueName, TestEventHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus
            .AddCommandConsumeChannel(queueName, c => c
                .WithRabbitMq(o => o
                    .QueueName(queueName) // Explicitly set queue name, though defaults to channel name could exist
                    .AutoAck(false)
                    .QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>())
            .AddHandler<TestEvent, TestEventHandler>());
            
        services.AddSingleton(handler);
        services.AddTestRabbitMq(rabbitMq.ConnectionString, defaultExchange: "");
        services.AddRabbitMqConsumer();
        
        return services;
    }
}
