using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ratatoskr.CloudEvents;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;
using TUnit.Core;

namespace Ratatoskr.Tests.Integration;

public class PublishTests(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture postgres) : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string ExchangeName => $"pub-test-{TestId}";
    private string DefaultRoutingKey => "test.event";



    [Test]
    public async Task Publish_DirectToExchange_MessageDelivered()
    {
        // Arrange
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c.Produces<TestEvent>());
            });
        });

        var provider = Services;
        var bus = provider.GetRequiredService<IRatatoskr>();

        var queueName = $"pub-queue-{TestId}";
        await EnsureQueueBoundAsync(queueName, ExchangeName, DefaultRoutingKey);

        // Act
        var props = new MessageProperties();
        props.SetRoutingKey(DefaultRoutingKey);
        
        await bus.PublishDirectAsync(new TestEvent { Data = "direct publish" }, props);
        
        // Assert
        await Task.Delay(500);
        var message = await GetMessageAsync(queueName);
        message.Should().NotBeNull();
        
        var body = Encoding.UTF8.GetString(message!.Body.ToArray());
        body.Should().Contain("direct publish");
    }

    [Test]
    public async Task Publish_WithBinaryContentMode_HeadersPresent()
    {
        // Arrange - Override configuration for this test
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c.Produces<TestEvent>())
                    .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Binary);
            });
        });
        
        var provider = Services;
        var bus = provider.GetRequiredService<IRatatoskr>();

        var queueName = $"pub-binary-{TestId}";
        await EnsureQueueBoundAsync(queueName, ExchangeName, DefaultRoutingKey);

        // Act
        var props = new MessageProperties();
        props.SetRoutingKey(DefaultRoutingKey);

        await bus.PublishDirectAsync(new TestEvent { Data = "binary mode" }, props);

        // Assert
        await Task.Delay(500);
        var message = await GetMessageAsync(queueName);
        message.Should().NotBeNull();
        
        message!.BasicProperties.Headers.Should().NotBeNull();
        message.BasicProperties.Headers.Should().ContainKey("cloudEvents_specversion");
        message.BasicProperties.Headers.Should().ContainKey("cloudEvents_type");
    }

    [Test]
    public async Task Publish_WithStructuredContentMode_BodyStructureCorrect()
    {
        // Arrange
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c.Produces<TestEvent>())
                    .ConfigureCloudEvents(ce => ce.ContentMode = CloudEventsContentMode.Structured);
            });
        });

        var provider = Services;
        var bus = provider.GetRequiredService<IRatatoskr>();

        var queueName = $"pub-struct-{TestId}";
        await EnsureQueueBoundAsync(queueName, ExchangeName, DefaultRoutingKey);

        // Act
        var props = new MessageProperties();
        props.SetRoutingKey(DefaultRoutingKey);

        await bus.PublishDirectAsync(new TestEvent { Data = "structured mode" }, props);

        // Assert
        await Task.Delay(500);
        var message = await GetMessageAsync(queueName);
        message.Should().NotBeNull();

        var body = Encoding.UTF8.GetString(message!.Body.ToArray());
        body.Should().Contain("\"specversion\"");
        body.Should().Contain("\"data\"");
        body.Should().Contain("structured mode");
    }
}
