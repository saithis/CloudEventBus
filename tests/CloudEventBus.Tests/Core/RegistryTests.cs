using AwesomeAssertions;
using Saithis.CloudEventBus.Configuration;
using Saithis.CloudEventBus.RabbitMq.Configuration;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class RegistryTests
{
    [Test]
    public void ProductionRegistry_StoresRegistrationWithMetadata()
    {
        // Arrange
        var registry = new ProductionRegistry();
        var reg = new ProductionRegistration(typeof(string), MessageIntent.EventProduction, "test-channel");
        
        var rabbitOptions = new RabbitMqProductionOptions { ExchangeType = "fanout", RoutingKey = "key" };
        reg.Metadata[RabbitMqExtensions.RabbitMqMetadataKey] = rabbitOptions;

        // Act
        registry.Add(reg);
        var stored = registry.Get(typeof(string));

        // Assert
        stored.Should().NotBeNull();
        stored!.Intent.Should().Be(MessageIntent.EventProduction);
        stored.ChannelName.Should().Be("test-channel");
        stored.Metadata.Should().ContainKey(RabbitMqExtensions.RabbitMqMetadataKey);
        stored.Metadata[RabbitMqExtensions.RabbitMqMetadataKey].Should().Be(rabbitOptions);
    }

    [Test]
    public void ConsumptionRegistry_StoresRegistration()
    {
        // Arrange
        var registry = new ConsumptionRegistry();
        var reg = new ConsumptionRegistration(typeof(int), MessageIntent.CommandConsumption, "cmd-exchange", "cmd-queue");

        // Act
        registry.Add(reg);
        var stored = registry.Get(typeof(int));

        // Assert
        stored.Should().NotBeNull();
        stored!.Intent.Should().Be(MessageIntent.CommandConsumption);
        stored.ChannelName.Should().Be("cmd-exchange");
        stored.SubscriptionName.Should().Be("cmd-queue");
    }
}
