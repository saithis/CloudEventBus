using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Configuration;
using Saithis.CloudEventBus.RabbitMq;
using Saithis.CloudEventBus.RabbitMq.Configuration;
using TUnit.Core;
using CloudEventBus.Tests.Fixtures;

namespace CloudEventBus.Tests.RabbitMq;

[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerTestSession)]
public class TopologyTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    public async Task Topology_Provisions_Owned_Resources()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddCloudEventBus(builder =>
        {
            builder.UseRabbitMq(mq => 
                mq.ConnectionString(rabbitMq.ConnectionString));

            builder.ProducesEvent<TopologyTestEvent>("topology.test.exchange")
                   .WithRabbitMq(cfg => cfg.ExchangeType(ExchangeType.Topic));
                   
            builder.ConsumesEvent<TopologyTestEvent>("topology.test.queue")
                   .WithRabbitMq(cfg => cfg.RoutingKey("#"));
        });

        // Use a hosted service to run startup logic
        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        
        // Act
        // Start Topology Manager (it's a hosted service)
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }

        // Assert
        // Verify exchange and queue exist using RabbitMQ client
        var connectionFactory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        using var connection = await connectionFactory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        // Passive declare should succeed if exists
        await channel.ExchangeDeclarePassiveAsync("topology.test.exchange");
        await channel.QueueDeclarePassiveAsync("topology.test.queue");
    }

    [Test]
    public async Task Topology_Validates_External_Resources_FailsIfMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System); // Needed for generic registration
        services.AddCloudEventBus(builder =>
        {
            builder.UseRabbitMq(mq => 
                mq.ConnectionString(rabbitMq.ConnectionString));

            // Sender expects "external.exchange" to exist. We assume it doesn't.
            builder.SendsCommand<TopologyTestCommand>("external.exchange");
        });

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        // Act & Assert
        // Starting should fail validation
        // Note: RabbitMqTopologyManager iterates hosted services order. 
        // We find the manager and run it.
        // It's registered as IHostedService.
        
        var exception = await Assert.ThrowsAsync<Exception>(async () =>
        {
            foreach (var service in hostedServices)
            {
                await service.StartAsync(CancellationToken.None);
            }
        });
        
        // Exception should come from RabbitMQ client saying exchange not found (404)
        // or TopologyManager validation logic
    }
}

public class TopologyTestEvent { }
public class TopologyTestCommand { }
