using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq;
using TUnit.Core;

namespace CloudEventBus.Tests.RabbitMq;

public class RabbitMqConsumerHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenConsumerHealthy_ReturnsHealthy()
    {
        // Arrange
        var consumer = CreateMockHealthyConsumer();
        var healthCheck = new RabbitMqConsumerHealthCheck(consumer);
        var context = new HealthCheckContext();
        
        // Act
        var result = await healthCheck.CheckHealthAsync(context);
        
        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("RabbitMQ consumer is running");
    }

    [Test]
    public async Task CheckHealthAsync_WhenConsumerUnhealthy_ReturnsUnhealthy()
    {
        // Arrange
        var consumer = CreateMockUnhealthyConsumer();
        var healthCheck = new RabbitMqConsumerHealthCheck(consumer);
        var context = new HealthCheckContext();
        
        // Act
        var result = await healthCheck.CheckHealthAsync(context);
        
        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("RabbitMQ consumer is not healthy");
    }

    [Test]
    public async Task CheckHealthAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var consumer = CreateMockHealthyConsumer();
        var healthCheck = new RabbitMqConsumerHealthCheck(consumer);
        var context = new HealthCheckContext();
        using var cts = new CancellationTokenSource();
        
        // Act
        var result = await healthCheck.CheckHealthAsync(context, cts.Token);
        
        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    private static RabbitMqConsumer CreateMockHealthyConsumer()
    {
        // Create a mock consumer that reports as healthy
        // Since we can't easily mock the consumer, we create a test double
        return new TestRabbitMqConsumer(isHealthy: true);
    }

    private static RabbitMqConsumer CreateMockUnhealthyConsumer()
    {
        return new TestRabbitMqConsumer(isHealthy: false);
    }
}

// Test double for RabbitMqConsumer that allows controlling IsHealthy
internal class TestRabbitMqConsumer : RabbitMqConsumer
{
    private readonly bool _isHealthy;

    public TestRabbitMqConsumer(bool isHealthy)
        : base(
            new RabbitMqConnectionManager(new RabbitMqOptions { HostName = "localhost" }),
            new RabbitMqConsumerOptions(),
            CreateMockDispatcher(),
            CreateMockMapper(),
            new RabbitMqRetryHandler(NullLogger<RabbitMqRetryHandler>.Instance),
            NullLogger<RabbitMqConsumer>.Instance)
    {
        _isHealthy = isHealthy;
    }

    public override bool IsHealthy => _isHealthy;

    private static MessageDispatcher CreateMockDispatcher()
    {
        // Create minimal dispatcher for testing
        var handlerRegistry = new MessageHandlerRegistry();
        var deserializer = new Saithis.CloudEventBus.Serializers.Json.JsonMessageSerializer();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        
        return new MessageDispatcher(
            handlerRegistry,
            deserializer,
            scopeFactory,
            NullLogger<MessageDispatcher>.Instance);
    }
    
    private static IRabbitMqEnvelopeMapper CreateMockMapper()
    {
        var options = new CloudEventsOptions();
        var typeRegistry = new MessageTypeRegistry();
        return new CloudEventsAmqpMapper(options, TimeProvider.System, typeRegistry);
    }
}
