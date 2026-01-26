using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq;

namespace CloudEventBus.Tests.RabbitMq;

public class CloudEventsAmqpMapperTests
{
    [Test]
    public void MapOutgoing_BinaryMode_SetsCloudEventsHeaders()
    {
        // Arrange
        var options = new CloudEventsOptions 
        { 
            ContentMode = CloudEventsContentMode.Binary 
        };
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event");
        registry.Freeze();
        
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        var serializedData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new TestEvent { Data = "test" }));
        var props = new MessageProperties 
        { 
            Type = "test.event",
            Source = "/test",
            ContentType = "application/json"
        };
        var basicProps = new BasicProperties();
        
        // Act
        var result = mapper.MapOutgoing(serializedData, props, basicProps);
        
        // Assert
        result.Should().BeSameAs(serializedData); // Body unchanged in binary mode
        basicProps.ContentType.Should().Be("application/json");
        basicProps.Type.Should().Be("test.event");
        basicProps.AppId.Should().Be("/test");
        basicProps.MessageId.Should().NotBeNull();
        basicProps.Headers.Should().NotBeNull();
        basicProps.Headers.Should().ContainKey(CloudEventsAmqpConstants.SpecVersionHeader);
        basicProps.Headers![CloudEventsAmqpConstants.SpecVersionHeader].Should().Be("1.0");
        basicProps.Headers.Should().ContainKey(CloudEventsAmqpConstants.TypeHeader);
        basicProps.Headers[CloudEventsAmqpConstants.TypeHeader].Should().Be("test.event");
        basicProps.Headers.Should().ContainKey(CloudEventsAmqpConstants.SourceHeader);
        basicProps.Headers[CloudEventsAmqpConstants.SourceHeader].Should().Be("/test");
    }

    [Test]
    public void MapOutgoing_StructuredMode_CreatesCloudEventsEnvelope()
    {
        // Arrange
        var options = new CloudEventsOptions 
        { 
            ContentMode = CloudEventsContentMode.Structured 
        };
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event");
        registry.Freeze();
        
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        var testEvent = new TestEvent { Data = "test" };
        var serializedData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var props = new MessageProperties 
        { 
            Type = "test.event",
            Source = "/test",
            ContentType = "application/json"
        };
        var basicProps = new BasicProperties();
        
        // Act
        var result = mapper.MapOutgoing(serializedData, props, basicProps);
        
        // Assert
        basicProps.ContentType.Should().Be(CloudEventsAmqpConstants.JsonContentType);
        
        var json = Encoding.UTF8.GetString(result);
        var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(json);
        envelope.Should().NotBeNull();
        envelope!.SpecVersion.Should().Be("1.0");
        envelope.Type.Should().Be("test.event");
        envelope.Source.Should().Be("/test");
        envelope.Data.Should().NotBeNull();
    }

    [Test]
    public void MapOutgoing_WithExtensions_IncludesInHeaders()
    {
        // Arrange
        var options = new CloudEventsOptions 
        { 
            ContentMode = CloudEventsContentMode.Binary 
        };
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var serializedData = Encoding.UTF8.GetBytes("{}");
        var props = new MessageProperties 
        { 
            Type = "test.event",
            Source = "/test",
            CloudEventExtensions = 
            {
                ["tenant"] = "test-tenant",
                ["version"] = "v1"
            }
        };
        var basicProps = new BasicProperties();
        
        // Act
        mapper.MapOutgoing(serializedData, props, basicProps);
        
        // Assert
        basicProps.Headers.Should().ContainKey("cloudEvents_tenant");
        basicProps.Headers!["cloudEvents_tenant"].Should().Be("test-tenant");
        basicProps.Headers.Should().ContainKey("cloudEvents_version");
        basicProps.Headers["cloudEvents_version"].Should().Be("v1");
    }

    [Test]
    public void MapIncoming_BinaryMode_ExtractsCloudEventsAttributes()
    {
        // Arrange
        var options = new CloudEventsOptions();
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var testEvent = new TestEvent { Data = "test" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        
        var basicProps = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = "msg-123",
            Type = "test.event",
            AppId = "/test"
        };
        basicProps.Headers = new Dictionary<string, object?>
        {
            [CloudEventsAmqpConstants.SpecVersionHeader] = "1.0",
            [CloudEventsAmqpConstants.IdHeader] = "event-123",
            [CloudEventsAmqpConstants.TypeHeader] = "test.event",
            [CloudEventsAmqpConstants.SourceHeader] = "/test",
            [CloudEventsAmqpConstants.TimeHeader] = "2024-01-15T10:30:00Z"
        };
        
        var deliverArgs = new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test.exchange",
            routingKey: "test.event",
            properties: basicProps,
            body: body,
            cancellationToken: CancellationToken.None);
        
        // Act
        var (resultBody, resultProps) = mapper.MapIncoming(deliverArgs);
        
        // Assert
        resultBody.Should().BeEquivalentTo(body); // Content equality, not reference equality
        resultProps.Id.Should().Be("msg-123"); // Prefer standard property
        resultProps.Type.Should().Be("test.event");
        resultProps.Source.Should().Be("/test");
        resultProps.ContentType.Should().Be("application/json");
    }

    [Test]
    public void MapIncoming_StructuredMode_ExtractsDataFromEnvelope()
    {
        // Arrange
        var options = new CloudEventsOptions();
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var testEvent = new TestEvent { Data = "test" };
        var envelope = new CloudEventEnvelope
        {
            SpecVersion = "1.0",
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            Time = DateTimeOffset.Parse("2024-01-15T10:30:00Z"),
            DataContentType = "application/json",
            Data = JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(testEvent))
        };
        
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var basicProps = new BasicProperties
        {
            ContentType = CloudEventsAmqpConstants.JsonContentType
        };
        
        var deliverArgs = new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test.exchange",
            routingKey: "test.event",
            properties: basicProps,
            body: body,
            cancellationToken: CancellationToken.None);
        
        // Act
        var (resultBody, resultProps) = mapper.MapIncoming(deliverArgs);
        
        // Assert
        resultProps.Id.Should().Be("event-123");
        resultProps.Type.Should().Be("test.event");
        resultProps.Source.Should().Be("/test");
        resultProps.ContentType.Should().Be("application/json");
        
        // Body should be extracted data, not the envelope
        var extractedData = JsonSerializer.Deserialize<TestEvent>(resultBody);
        extractedData.Should().NotBeNull();
        extractedData!.Data.Should().Be("test");
    }

    [Test]
    public void MapIncoming_WithAlternativeHeaderPrefix_ReadsCorrectly()
    {
        // Arrange (Wolverine compatibility: support both cloudEvents_ and cloudEvents: prefixes)
        var options = new CloudEventsOptions();
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var body = Encoding.UTF8.GetBytes("{}");
        var basicProps = new BasicProperties { ContentType = "application/json" };
        basicProps.Headers = new Dictionary<string, object?>
        {
            ["cloudEvents:id"] = "event-456", // Colon prefix
            ["cloudEvents:type"] = "test.event",
            ["cloudEvents:source"] = "/alternative"
        };
        
        var deliverArgs = new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test.exchange",
            routingKey: "test.event",
            properties: basicProps,
            body: body,
            cancellationToken: CancellationToken.None);
        
        // Act
        var (_, resultProps) = mapper.MapIncoming(deliverArgs);
        
        // Assert
        resultProps.Id.Should().Be("event-456");
        resultProps.Type.Should().Be("test.event");
        resultProps.Source.Should().Be("/alternative");
    }

    [Test]
    public void MapOutgoing_WithoutType_ThrowsInvalidOperation()
    {
        // Arrange
        var options = new CloudEventsOptions();
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var serializedData = Encoding.UTF8.GetBytes("{}");
        var props = new MessageProperties { Source = "/test" }; // No type
        var basicProps = new BasicProperties();
        
        // Act & Assert
        Action act = () => mapper.MapOutgoing(serializedData, props, basicProps);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudEvents 'type' is required*");
    }

    [Test]
    public void MapIncoming_PrefersStandardRabbitMqProperties_OverCloudEventsHeaders()
    {
        // Arrange (Wolverine compatibility)
        var options = new CloudEventsOptions();
        var registry = new MessageTypeRegistry();
        var mapper = new CloudEventsAmqpMapper(options, TimeProvider.System, registry);
        
        var body = Encoding.UTF8.GetBytes("{}");
        var basicProps = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = "standard-id",
            Type = "standard-type",
            AppId = "standard-source"
        };
        basicProps.Headers = new Dictionary<string, object?>
        {
            [CloudEventsAmqpConstants.IdHeader] = "ce-id",
            [CloudEventsAmqpConstants.TypeHeader] = "ce-type",
            [CloudEventsAmqpConstants.SourceHeader] = "ce-source"
        };
        
        var deliverArgs = new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test.exchange",
            routingKey: "test.event",
            properties: basicProps,
            body: body,
            cancellationToken: CancellationToken.None);
        
        // Act
        var (_, resultProps) = mapper.MapIncoming(deliverArgs);
        
        // Assert - Should prefer standard RabbitMQ properties
        resultProps.Id.Should().Be("standard-id");
        resultProps.Type.Should().Be("standard-type");
        resultProps.Source.Should().Be("standard-source");
    }
}
