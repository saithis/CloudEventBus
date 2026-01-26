using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Saithis.CloudEventBus.Core;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class MessagePropertiesEnricherTests
{
    [Test]
    public void Enrich_WithNullProperties_CreatesNewInstance()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithSource("/test-service")
            .WithExtension("version", "v1"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        
        // Act
        var result = enricher.Enrich<TestEvent>(null);
        
        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be("test.event");
        result.Source.Should().Be("/test-service");
        result.TransportMetadata.Should().ContainKey("version");
        result.TransportMetadata["version"].Should().Be("v1");
    }

    [Test]
    public void Enrich_WithEmptyProperties_FillsAllFields()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<OrderCreatedEvent>("order.created", cfg => cfg
            .WithSource("/orders-service")
            .WithExtension("team", "orders")
            .WithExtension("version", "v2"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<OrderCreatedEvent>(properties);
        
        // Assert
        result.Should().BeSameAs(properties); // Should modify in place
        result.Type.Should().Be("order.created");
        result.Source.Should().Be("/orders-service");
        result.TransportMetadata.Should().ContainKey("team");
        result.TransportMetadata["team"].Should().Be("orders");
        result.TransportMetadata.Should().ContainKey("version");
        result.TransportMetadata["version"].Should().Be("v2");
    }

    [Test]
    public void Enrich_WithTypeAlreadySet_DoesNotOverwrite()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event");
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties
        {
            Type = "custom.override.type"
        };
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Type.Should().Be("custom.override.type");
    }

    [Test]
    public void Enrich_WithSourceAlreadySet_DoesNotOverwrite()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithSource("/registry-source"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties
        {
            Source = "/custom-source"
        };
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Source.Should().Be("/custom-source");
    }

    [Test]
    public void Enrich_WithPartialTransportMetadata_OnlyAddsNewKeys()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithExtension("version", "v1")
            .WithExtension("team", "platform"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        properties.TransportMetadata.Add("team", "custom-team");
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.TransportMetadata.Should().ContainKey("version");
        result.TransportMetadata["version"].Should().Be("v1");
        result.TransportMetadata.Should().ContainKey("team");
        result.TransportMetadata["team"].Should().Be("custom-team"); // Should not overwrite
    }

    [Test]
    public void Enrich_WithTypeNotInRegistry_FallsBackToAttribute()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        // Don't register TestEvent - should fall back to attribute
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Type.Should().Be("test.event.basic"); // From [CloudEvent] attribute
        result.Source.Should().BeNull(); // No source in attribute
        result.TransportMetadata.Should().BeEmpty(); // No extensions
    }

    [Test]
    public void Enrich_WithAttributeAndCustomSource_UsesAttribute()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        // Don't register CustomerUpdatedEvent - should fall back to attribute with source
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<CustomerUpdatedEvent>(properties);
        
        // Assert
        result.Type.Should().Be("customer.updated");
        // Note: attribute source is not handled by enricher (only registry source)
        // This is a design limitation - enricher only enriches Source from registry, not from attribute
    }

    [Test]
    public void Enrich_WithNoRegistryAndNoAttribute_LeavesTypeNull()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<OrderCreatedEvent>(properties);
        
        // Assert
        result.Type.Should().BeNull(); // No registry entry and no attribute
        result.Source.Should().BeNull();
        result.TransportMetadata.Should().BeEmpty();
    }

    [Test]
    public void Enrich_NonGeneric_WithTypeArgument_Works()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithSource("/service"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich(typeof(TestEvent), properties);
        
        // Assert
        result.Type.Should().Be("test.event");
        result.Source.Should().Be("/service");
    }

    [Test]
    public void Enrich_WithAllFieldsSet_DoesNotChangeAnything()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithSource("/registry-source")
            .WithExtension("version", "v1"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties
        {
            Type = "custom.type",
            Source = "/custom-source"
        };
        properties.TransportMetadata.Add("custom", "value");
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Type.Should().Be("custom.type");
        result.Source.Should().Be("/custom-source");
        result.TransportMetadata.Should().ContainKey("custom");
        result.TransportMetadata["custom"].Should().Be("value");
        result.TransportMetadata.Should().ContainKey("version");
        result.TransportMetadata["version"].Should().Be("v1"); // Only adds new extension
    }

    [Test]
    public void Enrich_WithSourceInRegistryOnly_EnrichesSource()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<OrderCreatedEvent>("order.created", cfg => cfg
            .WithSource("/orders"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<OrderCreatedEvent>(properties);
        
        // Assert
        result.Source.Should().Be("/orders");
    }

    [Test]
    public void Enrich_WithEmptyStringType_EnrichesType()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event");
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties
        {
            Type = "" // Empty string should be treated as missing
        };
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Type.Should().Be("test.event");
    }

    [Test]
    public void Enrich_WithEmptyStringSource_EnrichesSource()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithSource("/service"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties
        {
            Source = "" // Empty string should be treated as missing
        };
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.Source.Should().Be("/service");
    }

    [Test]
    public void Enrich_WithMultipleExtensions_AddsAllExtensions()
    {
        // Arrange
        var registry = new MessageTypeRegistry();
        registry.Register<TestEvent>("test.event", cfg => cfg
            .WithExtension("version", "v1")
            .WithExtension("team", "platform")
            .WithExtension("region", "eu-west")
            .WithExtension("env", "prod"));
        registry.Freeze();
        
        var enricher = new MessagePropertiesEnricher(registry);
        var properties = new MessageProperties();
        
        // Act
        var result = enricher.Enrich<TestEvent>(properties);
        
        // Assert
        result.TransportMetadata.Should().HaveCount(4);
        result.TransportMetadata["version"].Should().Be("v1");
        result.TransportMetadata["team"].Should().Be("platform");
        result.TransportMetadata["region"].Should().Be("eu-west");
        result.TransportMetadata["env"].Should().Be("prod");
    }
}
