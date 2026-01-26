using System.Text;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq;
using TUnit.Core;

namespace CloudEventBus.Tests.RabbitMq;

[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("RabbitMQ")] // Tests use shared exchange and may interfere
public class RabbitMqIntegrationTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    public async Task SendAsync_PublishesMessageToExchange()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Create a queue to receive the message
        var queueName = $"test-queue-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test message" }, new MessageProperties
        {
            TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
        });
        
        // Wait a bit for message delivery
        await Task.Delay(500);
        
        // Assert - Try to consume the message
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.Body.ToArray().Length.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task SendAsync_WithPublisherConfirms_WaitsForConfirmation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddRabbitMqMessageSender(options =>
        {
            options.ConnectionString = rabbitMq.ConnectionString;
            options.DefaultExchange = "test.exchange";
            options.UsePublisherConfirms = true; // Enable confirms
        });
        
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IMessageSender>();
        
        var message = new TestEvent { Data = "confirmed message" };
        var props = new MessageProperties { Type = "test.event" };
        
        // Serialize the message
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var serialized = serializer.Serialize(message);
        props.ContentType = serializer.ContentType;
        
        // Act - Should not throw, confirms success
        await sender.SendAsync(serialized, props, CancellationToken.None);
        
        // If we get here without exception, confirms worked (no assertion needed)
    }

    [Test]
    public async Task SendAsync_WithRoutingKeyExtension_UsesCustomRoutingKey()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        // Create queue bound to custom routing key
        var customRoutingKey = "custom.routing.key";
        var queueName = $"test-queue-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: customRoutingKey);
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "routed message" }, new MessageProperties
        {
            TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = customRoutingKey }
        });
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
    }

    [Test]
    public async Task SendAsync_SetsBasicProperties()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        var queueName = $"test-queue-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test" }, new MessageProperties
        {
            TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
        });
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.BasicProperties.DeliveryMode.Should().Be(DeliveryModes.Persistent);
        result.BasicProperties.ContentType.Should().NotBeNull();
        result.BasicProperties.MessageId.Should().NotBeNull();
        result.BasicProperties.Type.Should().Be("test.event");
    }

    [Test]
    public async Task SendAsync_WithBinaryCloudEvents_SetsHeaders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Binary));
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        var queueName = $"test-queue-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "binary test" }, new MessageProperties
        {
            TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
        });
        
        await Task.Delay(500);
        
        // Assert
        var result = await channel.BasicGetAsync(queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.BasicProperties.Headers.Should().NotBeNull();
        
        // Check for CloudEvents headers (AMQP binding uses cloudEvents_ prefix)
        var headers = result.BasicProperties.Headers!;
        headers.Should().ContainKey("cloudEvents_specversion");
        headers.Should().ContainKey("cloudEvents_id");
        headers.Should().ContainKey("cloudEvents_source");
        headers.Should().ContainKey("cloudEvents_type");
        
        var ceType = Encoding.UTF8.GetString((byte[])headers["cloudEvents_type"]!);
        ceType.Should().Be("test.event");
    }

    [Test]
    public async Task SendAsync_MultipleMessages_AllDelivered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCloudEventBus(bus => bus.AddMessage<TestEvent>("test.event"));
        services.AddTestRabbitMq(rabbitMq.ConnectionString);
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        
        var queueName = $"test-queue-{Guid.NewGuid()}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue: queueName, exchange: "test.exchange", routingKey: "test.event");
        
        // Act - Send multiple messages
        for (int i = 1; i <= 5; i++)
        {
            await bus.PublishDirectAsync(new TestEvent { Data = $"message {i}" }, new MessageProperties
            {
                TransportMetadata = { [RabbitMqMessageSender.RoutingKeyExtensionKey] = "test.event" }
            });
        }
        
        await Task.Delay(1000);
        
        // Assert
        var messageCount = await channel.MessageCountAsync(queueName);
        messageCount.Should().Be(5u);
    }
}
