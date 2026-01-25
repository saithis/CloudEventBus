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
    public void AddHandler_WithRegisteredMessage_RegistersHandlerSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddMessage<TestEvent>("test.event");
        builder.AddHandler<TestEvent, TestEventHandler>();
        builder.HandlerRegistry.Freeze();
        
        // Assert
        var handlers = builder.HandlerRegistry.GetHandlers("test.event");
        handlers.Should().HaveCount(1);
        handlers[0].MessageType.Should().Be(typeof(TestEvent));
        handlers[0].HandlerType.Should().Be(typeof(TestEventHandler));
        
        // Verify handler is registered in DI
        var provider = services.BuildServiceProvider();
        var handler = provider.CreateScope().ServiceProvider.GetService<TestEventHandler>();
        handler.Should().NotBeNull();
    }

    [Test]
    public void AddHandler_WithMultipleHandlers_RegistersBothHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddMessage<TestEvent>("test.event");
        builder.AddHandler<TestEvent, TestEventHandler>();
        builder.AddHandler<TestEvent, SecondTestEventHandler>();
        builder.HandlerRegistry.Freeze();
        
        // Assert
        var handlers = builder.HandlerRegistry.GetHandlers("test.event");
        handlers.Should().HaveCount(2);
        handlers[0].HandlerType.Should().Be(typeof(TestEventHandler));
        handlers[1].HandlerType.Should().Be(typeof(SecondTestEventHandler));
    }

    [Test]
    public void AddHandler_WithAttributeDecoratedMessage_UsesAttributeEventType()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // TestEvent has [CloudEvent("test.event.basic")] attribute
        // Act
        builder.AddHandler<TestEvent, TestEventHandler>();
        builder.HandlerRegistry.Freeze();
        
        // Assert
        var handlers = builder.HandlerRegistry.GetHandlers("test.event.basic");
        handlers.Should().HaveCount(1);
    }

    [Test]
    public void AddHandler_WithoutRegisteredMessage_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // OrderCreatedEvent has no [CloudEvent] attribute and wasn't registered
        // Act
        Action act = () => builder.AddHandler<OrderCreatedEvent, OrderCreatedHandler>();
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Cannot register handler*");
    }

    [Test]
    public void AddMessageWithHandler_RegistersBothMessageAndHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddMessageWithHandler<OrderCreatedEvent, OrderCreatedHandler>("order.created");
        builder.TypeRegistry.Freeze();
        builder.HandlerRegistry.Freeze();
        
        // Assert - Message is registered
        var messageInfo = builder.TypeRegistry.GetByClrType<OrderCreatedEvent>();
        messageInfo.Should().NotBeNull();
        messageInfo!.EventType.Should().Be("order.created");
        
        // Assert - Handler is registered
        var handlers = builder.HandlerRegistry.GetHandlers("order.created");
        handlers.Should().HaveCount(1);
        handlers[0].HandlerType.Should().Be(typeof(OrderCreatedHandler));
        
        // Verify handler is registered in DI
        var provider = services.BuildServiceProvider();
        var handler = provider.CreateScope().ServiceProvider.GetService<OrderCreatedHandler>();
        handler.Should().NotBeNull();
    }

    [Test]
    public void AddMessageWithHandler_WithMessageConfiguration_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddMessage<OrderCreatedEvent>("order.created", cfg => cfg
            .WithSource("/orders-service")
            .WithExtension("version", "v1"));
        builder.AddHandler<OrderCreatedEvent, OrderCreatedHandler>();
        builder.TypeRegistry.Freeze();
        builder.HandlerRegistry.Freeze();
        
        // Assert - Message configuration is applied
        var messageInfo = builder.TypeRegistry.GetByClrType<OrderCreatedEvent>();
        messageInfo.Should().NotBeNull();
        messageInfo!.Source.Should().Be("/orders-service");
        messageInfo.Extensions.Should().ContainKey("version");
        messageInfo.Extensions["version"].Should().Be("v1");
        
        // Assert - Handler is registered
        var handlers = builder.HandlerRegistry.GetHandlers("order.created");
        handlers.Should().HaveCount(1);
    }

    [Test]
    public void AddHandler_RegistersHandlerAsBothTypesInDI()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        builder.AddMessage<TestEvent>("test.event");
        
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

    [Test]
    public void AddHandler_WithDifferentMessages_RegistersSeparately()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        builder.AddMessage<TestEvent>("test.event");
        builder.AddMessage<OrderCreatedEvent>("order.created");
        builder.AddHandler<TestEvent, TestEventHandler>();
        builder.AddHandler<OrderCreatedEvent, OrderCreatedHandler>();
        builder.HandlerRegistry.Freeze();
        
        // Assert - TestEvent handlers
        var testHandlers = builder.HandlerRegistry.GetHandlers("test.event");
        testHandlers.Should().HaveCount(1);
        testHandlers[0].HandlerType.Should().Be(typeof(TestEventHandler));
        
        // Assert - OrderCreatedEvent handlers
        var orderHandlers = builder.HandlerRegistry.GetHandlers("order.created");
        orderHandlers.Should().HaveCount(1);
        orderHandlers[0].HandlerType.Should().Be(typeof(OrderCreatedHandler));
    }

    [Test]
    public void AddHandler_ReturnsSameBuilder_AllowsChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        var result = builder
            .AddMessage<TestEvent>("test.event")
            .AddHandler<TestEvent, TestEventHandler>()
            .AddHandler<TestEvent, SecondTestEventHandler>();
        
        // Assert
        result.Should().BeSameAs(builder);
    }

    [Test]
    public void AddMessageWithHandler_ReturnsSameBuilder_AllowsChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new CloudEventBusBuilder(services);
        
        // Act
        var result = builder
            .AddMessageWithHandler<TestEvent, TestEventHandler>("test.event")
            .AddMessageWithHandler<OrderCreatedEvent, OrderCreatedHandler>("order.created");
        
        // Assert
        result.Should().BeSameAs(builder);
    }
}
