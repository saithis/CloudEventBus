using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Ratatoskr.CloudEvents;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;
using TUnit.Core;

namespace Ratatoskr.Tests.Integration;

public class ConsumeTests(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture postgres) : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string ExchangeName => $"cons-test-{TestId}";
    private string QueueName => $"cons-queue-{TestId}";

    [Test]
    public async Task Consume_DirectPublish_HandlerInvoked()
    {
        // Arrange
        var handler = new TestEventHandler();
        var services = new ServiceCollection();
        base.ConfigureServices(services);
        
        services.AddRatatoskr(bus => 
        {
            bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
            bus.AddCommandConsumeChannel(QueueName, c => c
                .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>());
            bus.AddHandler<TestEvent, TestEventHandler>();
        });
        
        services.AddSingleton(handler);

        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);

        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            // Act
            await PublishToRabbitMqAsync(exchange: "", routingKey: QueueName, new TestEvent { Id = "123", Data = "consumed" });

            // Assert
            await WaitForConditionAsync(() => handler.HandledMessages.Count > 0, TimeSpan.FromSeconds(2));
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("123");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consume_MultipleHandlers_AllInvoked()
    {
        // Arrange
        var handler1 = new TestEventHandler();
        var handler2 = new SecondTestEventHandler();
        var services = new ServiceCollection();
        base.ConfigureServices(services);

        services.AddRatatoskr(bus => 
        {
            bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
            bus.AddCommandConsumeChannel(QueueName, c => c
                .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>());
             bus.AddHandler<TestEvent, TestEventHandler>()
                .AddHandler<TestEvent, SecondTestEventHandler>();
        });

        services.AddSingleton(handler1);
        services.AddSingleton(handler2);

        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);

        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            // Act
            await PublishToRabbitMqAsync(exchange: "", routingKey: QueueName, new TestEvent { Id = "multi", Data = "cast" });

            // Assert
            await WaitForConditionAsync(() => handler1.HandledMessages.Count > 0 && handler2.HandledMessages.Count > 0, TimeSpan.FromSeconds(2));
            handler1.HandledMessages.Should().HaveCount(1);
            handler2.HandledMessages.Should().HaveCount(1);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Consume_BinaryCloudEvent_DeserializedCorrectly()
    {
        // Arrange
        var handler = new TestEventHandler();
        var services = new ServiceCollection();
        base.ConfigureServices(services);

        services.AddRatatoskr(bus => 
        {
            bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
            bus.AddCommandConsumeChannel(QueueName, c => c
                .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>());
            bus.AddHandler<TestEvent, TestEventHandler>();
            bus.ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Binary);
        });

        services.AddSingleton(handler);

        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);

        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            // Act - Manual publish with CloudEvents headers
            // Using helper method but manually would be better to ensure headers are set correctly by test code
             var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            var json = System.Text.Json.JsonSerializer.Serialize(new TestEvent { Id = "bin-1", Data = "binary data" });
            var body = Encoding.UTF8.GetBytes(json);

            var props = new BasicProperties
            {
                ContentType = "application/json",
                Headers = new Dictionary<string, object?>
                {
                    ["cloudEvents_specversion"] = "1.0",
                    ["cloudEvents_type"] = "test.event", // Must match implicit type
                    ["cloudEvents_source"] = "/test",
                    ["cloudEvents_id"] = Guid.NewGuid().ToString()
                }
            };
            
            await channel.BasicPublishAsync(exchange: "", routingKey: QueueName, false, props, body);

            // Assert
            await WaitForConditionAsync(() => handler.HandledMessages.Count > 0, TimeSpan.FromSeconds(2));
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("bin-1");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
