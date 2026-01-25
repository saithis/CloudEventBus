using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;

namespace CloudEventBus.Tests.Core;

public class JsonMessageSerializerTests
{
    [Test]
    public void Serialize_WritesProperJson_AndMessagePropertiesContentType()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        var messageProperties = new MessageProperties();
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = serializer.Serialize(testEvent, messageProperties);
        
        // Act
        var result = JsonSerializer.Deserialize<TestEvent>(body);
        
        // Assert
        messageProperties.ContentType.Should().Be("application/json");
        result.Should().NotBeNull();
        result.Id.Should().Be("123");
        result.Data.Should().Be("test data");
    }
    
    [Test]
    public void Serialize_NullData_ReturnsNull()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        var messageProperties = new MessageProperties();
        var body = serializer.Serialize(null!, messageProperties);
        
        // Act
        var result = JsonSerializer.Deserialize<TestEvent>(body);
        
        // Assert
        messageProperties.ContentType.Should().Be("application/json");
        result.Should().BeNull();
    }
    
    [Test]
    public void Deserialize_ExtractsDataFromBody()
    {
        // Arrange
        var deserializer = new JsonMessageSerializer();
        
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
        result.Id.Should().Be("123");
        result.Data.Should().Be("test data");
    }

    [Test]
    public void Deserialize_NullData_ReturnsNull()
    {
        // Arrange
        var deserializer = new JsonMessageSerializer();
        
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize((TestEvent?)null));
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
    public void Deserialize_NonGeneric_ReturnsCorrectType()
    {
        // Arrange
        var deserializer = new JsonMessageSerializer();
        
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
        var typedResult = (TestEvent)result;
        typedResult.Id.Should().Be("123");
        typedResult.Data.Should().Be("test data");
    }

    [Test]
    public void Deserialize_ComplexType_DeserializesAllProperties()
    {
        // Arrange
        var deserializer = new JsonMessageSerializer();
        
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
        result.OrderId.Should().Be("order-456");
        result.Amount.Should().Be(123.45m);
        result.CreatedAt.Should().Be(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero));
    }
}
