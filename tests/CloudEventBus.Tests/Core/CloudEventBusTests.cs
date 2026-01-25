using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Testing;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class CloudEventBusTests
{
    [Test]
    public async Task PublishDirectAsync_WithRegisteredType_SetsCorrectEventType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestCloudEventBus(bus => bus
            .AddMessage<OrderCreatedEvent>("order.created"));
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        // Act
        await bus.PublishDirectAsync(new OrderCreatedEvent 
        { 
            OrderId = "123",
            Amount = 99.99m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Properties.Type.Should().Be("order.created");
    }

    [Test]
    public async Task PublishDirectAsync_WithAttributeType_SetsCorrectEventType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestCloudEventBus();
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test data" });
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Properties.Type.Should().Be("test.event.basic");
    }

    [Test]
    public async Task PublishDirectAsync_WithExplicitProperties_UsesProvidedValues()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestCloudEventBus();
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        var customProperties = new Saithis.CloudEventBus.Core.MessageProperties
        {
            Type = "custom.type",
            Source = "/custom-source",
            Subject = "test-subject"
        };
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test" }, customProperties);
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Properties.Type.Should().Be("custom.type");
        message.Properties.Source.Should().Be("/custom-source");
        message.Properties.Subject.Should().Be("test-subject");
    }

    [Test]
    public async Task PublishDirectAsync_WithExtensions_IncludesExtensionsInProperties()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event", cfg => cfg
                .WithExtension("tenant", "test-tenant")
                .WithExtension("version", "v1")));
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test" });
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Properties.Extensions.Should().ContainKey("tenant");
        message.Properties.Extensions["tenant"].Should().Be("test-tenant");
        message.Properties.Extensions.Should().ContainKey("version");
        message.Properties.Extensions["version"].Should().Be("v1");
    }

    [Test]
    public async Task PublishDirectAsync_SetsTimestamp()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestCloudEventBus();
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICloudEventBus>();
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        
        var beforePublish = DateTimeOffset.UtcNow;
        
        // Act
        await bus.PublishDirectAsync(new TestEvent { Data = "test" });
        
        var afterPublish = DateTimeOffset.UtcNow;
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Properties.Time.Should().NotBeNull();
        message.Properties.Time!.Value.Should().BeOnOrAfter(beforePublish);
        message.Properties.Time!.Value.Should().BeOnOrBefore(afterPublish);
    }
}
