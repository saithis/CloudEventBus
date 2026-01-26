using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class CloudEventsSerializerTests
{
    [Test]
    public void Serialize_StructuredMode_CreatesValidEnvelope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Structured));
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new TestEvent { Data = "test data" };
        var props = new MessageProperties { Source = "/test" };
        
        // Act
        var serialized = serializer.Serialize(message, props);
        
        // Assert
        var json = Encoding.UTF8.GetString(serialized);
        var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(json);
        
        envelope.Should().NotBeNull();
        envelope!.SpecVersion.Should().Be("1.0");
        envelope.Type.Should().Be("test.event");
        envelope.Source.Should().Be("/test");
        envelope.Id.Should().NotBeNull();
        envelope.Data.Should().NotBeNull();
        props.ContentType.Should().Be(CloudEventsConstants.JsonContentType);
    }

    [Test]
    public void Serialize_BinaryMode_SetsHeaders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Binary));
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new TestEvent { Data = "test data" };
        var props = new MessageProperties { Source = "/test" };
        
        // Act
        var serialized = serializer.Serialize(message, props);
        
        // Assert
        props.Headers.Should().ContainKey(CloudEventsConstants.SpecVersionHeader);
        props.Headers[CloudEventsConstants.SpecVersionHeader].Should().Be(CloudEventsConstants.SpecVersion);
        
        props.Headers.Should().ContainKey(CloudEventsConstants.IdHeader);
        props.Headers.Should().ContainKey(CloudEventsConstants.SourceHeader);
        props.Headers[CloudEventsConstants.SourceHeader].Should().Be("/test");
        
        props.Headers.Should().ContainKey(CloudEventsConstants.TypeHeader);
        props.Headers[CloudEventsConstants.TypeHeader].Should().Be("test.event");
        
        props.Headers.Should().ContainKey(CloudEventsConstants.TimeHeader);
        
        // Verify the body is just the serialized data, not wrapped in envelope
        var json = Encoding.UTF8.GetString(serialized);
        var deserialized = JsonSerializer.Deserialize<TestEvent>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Data.Should().Be("test data");
    }

    [Test]
    public void Serialize_WithoutType_ThrowsInvalidOperation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(); // No message registered
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new EventWithoutAttribute("123"); // No CloudEvent attribute
        var props = new MessageProperties(); // No explicit type
        
        // Act & Assert
        Action act = () => serializer.Serialize(message, props);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudEvents 'type' is required*");
    }

    [Test]
    public void Serialize_WithCustomExtensions_IncludesInEnvelope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Structured));
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new TestEvent { Data = "test" };
        var props = new MessageProperties 
        { 
            Source = "/test",
            CloudEventExtensions = 
            {
                ["tenant"] = "test-tenant",
                ["version"] = "v1"
            }
        };
        
        // Act
        var serialized = serializer.Serialize(message, props);
        
        // Assert
        var json = Encoding.UTF8.GetString(serialized);
        var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(json);
        
        envelope.Should().NotBeNull();
        envelope!.Extensions.Should().NotBeNull();
        envelope.Extensions.Should().ContainKey("tenant");
        envelope.Extensions!["tenant"].ToString().Should().Be("test-tenant");
    }

    [Test]
    public void Serialize_BinaryMode_WithCustomExtensions_AddsHeaders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .ConfigureCloudEvents(opts => opts.ContentMode = CloudEventsContentMode.Binary));
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new TestEvent { Data = "test" };
        var props = new MessageProperties 
        { 
            Source = "/test",
            CloudEventExtensions = 
            {
                ["tenant"] = "test-tenant"
            }
        };
        
        // Act
        serializer.Serialize(message, props);
        
        // Assert
        props.Headers.Should().ContainKey("ce-tenant");
        props.Headers["ce-tenant"].Should().Be("test-tenant");
    }

    [Test]
    public void Serialize_WithCloudEventsDisabled_UsesInnerSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCloudEventBus(bus => bus
            .AddMessage<TestEvent>("test.event")
            .DisableCloudEvents());
        
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        
        var message = new TestEvent { Data = "test data" };
        var props = new MessageProperties();
        
        // Act
        var serialized = serializer.Serialize(message, props);
        
        // Assert - should be plain JSON, not CloudEvents envelope
        var json = Encoding.UTF8.GetString(serialized);
        var deserialized = JsonSerializer.Deserialize<TestEvent>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Data.Should().Be("test data");
        
        // Should not have CloudEvents headers
        props.Headers.Should().NotContainKey(CloudEventsConstants.SpecVersionHeader);
    }
}

public record EventWithoutAttribute(string Data);
