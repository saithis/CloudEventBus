using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Saithis.CloudEventBus.Core;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class MessageTypeRegistryTests
{
    [Test]
    public void Register_WithBuilder_ConfiguresAllOptions()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        
        // Act
        registry.Register<OrderCreatedEvent>("order.created", cfg => cfg
            .WithSource("/orders-service")
            .WithExtension("version", "v1")
            .WithExtension("team", "orders"));
        registry.Freeze();
        
        // Assert
        var info = registry.GetByClrType<OrderCreatedEvent>();
        info.Should().NotBeNull();
        info!.EventType.Should().Be("order.created");
        info.Source.Should().Be("/orders-service");
        info.Extensions.Should().ContainKey("version");
        info.Extensions["version"].Should().Be("v1");
        info.Extensions.Should().ContainKey("team");
        info.Extensions["team"].Should().Be("orders");
    }

    [Test]
    public void RegisterFromAssembly_FindsDecoratedTypes()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        
        // Act
        registry.RegisterFromAssembly(typeof(TestEvent).Assembly);
        registry.Freeze();
        
        // Assert
        var testEventInfo = registry.GetByClrType<TestEvent>();
        testEventInfo.Should().NotBeNull();
        testEventInfo!.EventType.Should().Be("test.event.basic");
        
        var customerEventInfo = registry.GetByClrType<CustomerUpdatedEvent>();
        customerEventInfo.Should().NotBeNull();
        customerEventInfo!.EventType.Should().Be("customer.updated");
        customerEventInfo.Source.Should().Be("/test-service");
    }

    [Test]
    public void GetByClrType_AfterFreeze_ReturnsCorrectInfo()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event");
        registry.Freeze();
        
        // Act
        var info = registry.GetByClrType<TestEvent>();
        
        // Assert
        info.Should().NotBeNull();
        info!.EventType.Should().Be("test.event");
    }

    [Test]
    public void GetByEventType_ReturnsCorrectInfo()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<OrderCreatedEvent>("order.created");
        registry.Freeze();
        
        // Act
        var info = registry.GetByEventType("order.created");
        
        // Assert
        info.Should().NotBeNull();
        info!.ClrType.Should().Be(typeof(OrderCreatedEvent));
    }

    [Test]
    public void ResolveEventType_FallsBackToAttribute()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        // Don't register TestEvent - should fall back to attribute
        registry.Freeze();
        
        // Act
        var eventType = registry.ResolveEventType(typeof(TestEvent));
        
        // Assert
        eventType.Should().Be("test.event.basic");
    }

    [Test]
    public void ResolveEventType_PrefersRegisteredType()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("custom.event.type");
        registry.Freeze();
        
        // Act
        var eventType = registry.ResolveEventType(typeof(TestEvent));
        
        // Assert - should use registered type, not attribute
        eventType.Should().Be("custom.event.type");
    }

    [Test]
    public void GetByClrType_NotRegistered_ReturnsNull()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Freeze();
        
        // Act
        var info = registry.GetByClrType<OrderCreatedEvent>();
        
        // Assert
        info.Should().BeNull();
    }
}
