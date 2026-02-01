using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Core;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class CloudEventBusBuilderTests
{
    [Test]
    public void AddEventConsumeChannel_RegistersChannel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddEventConsumeChannel("test-channel", _ => {});
        
        // Assert
        var channel = builder.ChannelRegistry.GetPublishChannel("test-channel");
        channel.Should().NotBeNull();
        channel!.Intent.Should().Be(ChannelType.EventConsume);
    }

    [Test]
    public void AddEventPublishChannel_RegistersChannel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddEventPublishChannel("pub-channel", _ => {});
        
        // Assert
        var channel = builder.ChannelRegistry.GetPublishChannel("pub-channel");
        channel.Should().NotBeNull();
        channel!.Intent.Should().Be(ChannelType.EventPublish);
    }

    [Test]
    public void Consumes_RegistersMessageInChannel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddEventConsumeChannel("test-channel", c => c.Consumes<TestEvent>());
        
        // Assert
        var channel = builder.ChannelRegistry.GetPublishChannel("test-channel");
        channel.Should().NotBeNull();
        var msg = channel!.GetMessage(typeof(TestEvent));
        msg.Should().NotBeNull();
        msg!.MessageTypeName.Should().Be("test.event");
    }

    [Test]
    public void Produces_RegistersMessageInChannel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddEventPublishChannel("pub-channel", c => c.Produces<TestEvent>());
        
        // Assert
        var channel = builder.ChannelRegistry.GetPublishChannel("pub-channel");
        channel.Should().NotBeNull();
        var msg = channel!.GetMessage(typeof(TestEvent));
        msg.Should().NotBeNull();
    }

    [Test]
    public void AddHandler_RegistersHandlerAsBothTypesInDI()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddHandler<TestEvent, TestEventHandler>();
        var provider = services.BuildServiceProvider();
        
        // Assert - Can resolve as concrete type
        using (var scope = provider.CreateScope())
        {
            var concreteHandler = scope.ServiceProvider.GetService<TestEventHandler>();
            concreteHandler.Should().NotBeNull();
            
            // Assert - Can resolve as interface
            var interfaceHandler = scope.ServiceProvider.GetService<IMessageHandler<TestEvent>>();
            interfaceHandler.Should().NotBeNull();
            interfaceHandler.Should().BeSameAs(concreteHandler);
        }
    }
}
