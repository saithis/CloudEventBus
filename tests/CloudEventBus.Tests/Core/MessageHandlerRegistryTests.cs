using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Saithis.CloudEventBus.Core;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class MessageHandlerRegistryTests
{
    [Test]
    public void Register_SingleHandler_RegistersSuccessfully()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        
        // Act
        registry.Register<TestEvent, TestEventHandler>("test.event");
        registry.Freeze();
        
        // Assert
        var handlers = registry.GetHandlers("test.event");
        handlers.Should().HaveCount(1);
        handlers[0].MessageType.Should().Be(typeof(TestEvent));
        handlers[0].HandlerType.Should().Be(typeof(TestEventHandler));
        handlers[0].HandlerInterfaceType.Should().Be(typeof(IMessageHandler<TestEvent>));
    }

    [Test]
    public void Register_MultipleHandlers_RegistersAllHandlers()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        
        // Act
        registry.Register<TestEvent, TestEventHandler>("test.event");
        registry.Register<TestEvent, SecondTestEventHandler>("test.event");
        registry.Freeze();
        
        // Assert
        var handlers = registry.GetHandlers("test.event");
        handlers.Should().HaveCount(2);
        handlers[0].HandlerType.Should().Be(typeof(TestEventHandler));
        handlers[1].HandlerType.Should().Be(typeof(SecondTestEventHandler));
    }

    [Test]
    public void GetHandlers_NoHandlersRegistered_ReturnsEmptyList()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Freeze();
        
        // Act
        var handlers = registry.GetHandlers("unknown.event");
        
        // Assert
        handlers.Should().BeEmpty();
    }

    [Test]
    public void GetHandlers_BeforeFreeze_ReturnsHandlers()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Register<TestEvent, TestEventHandler>("test.event");
        
        // Act
        var handlers = registry.GetHandlers("test.event");
        
        // Assert
        handlers.Should().HaveCount(1);
    }

    [Test]
    public void Freeze_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Register<TestEvent, TestEventHandler>("test.event");
        
        // Act & Assert
        registry.Freeze();
        registry.Freeze(); // Should not throw
    }

    [Test]
    public void Register_AfterFreeze_ThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Freeze();
        
        // Act
        Action act = () => registry.Register<TestEvent, TestEventHandler>("test.event");
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*frozen*");
    }

    [Test]
    public void GetRegisteredEventTypes_ReturnsAllEventTypes()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Register<TestEvent, TestEventHandler>("test.event");
        registry.Register<OrderCreatedEvent, OrderCreatedHandler>("order.created");
        registry.Freeze();
        
        // Act
        var eventTypes = registry.GetRegisteredEventTypes().ToList();
        
        // Assert
        eventTypes.Should().HaveCount(2);
        eventTypes.Should().Contain("test.event");
        eventTypes.Should().Contain("order.created");
    }

    [Test]
    public void GetRegisteredEventTypes_BeforeFreeze_ReturnsEventTypes()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        registry.Register<TestEvent, TestEventHandler>("test.event");
        
        // Act
        var eventTypes = registry.GetRegisteredEventTypes().ToList();
        
        // Assert
        eventTypes.Should().HaveCount(1);
        eventTypes.Should().Contain("test.event");
    }

    [Test]
    public void Register_DifferentMessageTypesWithSameEventType_AllowsRegistration()
    {
        // Arrange
        var registry = new MessageHandlerRegistry();
        
        // Act - Register handlers for different message types with same event type
        registry.Register<TestEvent, TestEventHandler>("shared.event");
        registry.Register<OrderCreatedEvent, OrderCreatedHandler>("shared.event");
        
        // Assert - Both should be registered (though this is unusual usage)
        var handlers = registry.GetHandlers("shared.event");
        handlers.Should().HaveCount(2);
    }
}
