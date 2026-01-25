using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class JsonMessageDeserializerTests
{
    [Test]
    public void Deserialize_StructuredMode_ExtractsDataFromEnvelope()
    {
        // Arrange
        var options = new CloudEventsOptions
        {
            Enabled = true,
            ContentMode = CloudEventsContentMode.Structured
        };
        var deserializer = new JsonMessageDeserializer(options);
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var envelope = new
        {
            specversion = "1.0",
            type = "test.event",
            source = "/test",
            id = "event-123",
            data = testEvent
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize<TestEvent>(body, context);
        
        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("123");
        result.Data.Should().Be("test data");
    }

    [Test]
    public void Deserialize_StructuredModeNullData_ReturnsNull()
    {
        // Arrange
        var options = new CloudEventsOptions
        {
            Enabled = true,
            ContentMode = CloudEventsContentMode.Structured
        };
        var deserializer = new JsonMessageDeserializer(options);
        
        var envelope = new
        {
            specversion = "1.0",
            type = "test.event",
            source = "/test",
            id = "event-123",
            data = (TestEvent?)null
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize<TestEvent>(body, context);
        
        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void Deserialize_BinaryMode_DeserializesDirectly()
    {
        // Arrange
        var options = new CloudEventsOptions
        {
            Enabled = true,
            ContentMode = CloudEventsContentMode.Binary
        };
        var deserializer = new JsonMessageDeserializer(options);
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize<TestEvent>(body, context);
        
        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("123");
        result.Data.Should().Be("test data");
    }

    [Test]
    public void Deserialize_CloudEventsDisabled_DeserializesDirectly()
    {
        // Arrange
        var options = new CloudEventsOptions
        {
            Enabled = false
        };
        var deserializer = new JsonMessageDeserializer(options);
        
        var testEvent = new OrderCreatedEvent 
        { 
            OrderId = "order-123", 
            Amount = 99.99m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "order.created",
            Source = "/orders",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize<OrderCreatedEvent>(body, context);
        
        // Assert
        result.Should().NotBeNull();
        result!.OrderId.Should().Be("order-123");
        result.Amount.Should().Be(99.99m);
    }

    [Test]
    public void Deserialize_NonGeneric_ReturnsCorrectType()
    {
        // Arrange
        var options = new CloudEventsOptions
        {
            Enabled = false
        };
        var deserializer = new JsonMessageDeserializer(options);
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize(body, typeof(TestEvent), context);
        
        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<TestEvent>();
        var typedResult = (TestEvent)result!;
        typedResult.Id.Should().Be("123");
        typedResult.Data.Should().Be("test data");
    }

    [Test]
    public void Deserialize_ComplexType_DeserializesAllProperties()
    {
        // Arrange
        var options = new CloudEventsOptions { Enabled = false };
        var deserializer = new JsonMessageDeserializer(options);
        
        var orderEvent = new OrderCreatedEvent 
        { 
            OrderId = "order-456", 
            Amount = 123.45m,
            CreatedAt = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero)
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(orderEvent));
        var context = new MessageContext
        {
            Id = "event-123",
            Type = "order.created",
            Source = "/orders",
            RawBody = body
        };
        
        // Act
        var result = deserializer.Deserialize<OrderCreatedEvent>(body, context);
        
        // Assert
        result.Should().NotBeNull();
        result!.OrderId.Should().Be("order-456");
        result.Amount.Should().Be(123.45m);
        result.CreatedAt.Should().Be(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero));
    }
}
