using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ratatoskr.Core;
using Ratatoskr.EfCore.Testing;
using Ratatoskr.RabbitMq;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;
using TUnit.Core;

namespace Ratatoskr.Tests.Integration;

public class OutboxTests(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture postgres) : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string ExchangeName => $"outbox-test-{TestId}";
    private string QueueName => $"outbox-queue-{TestId}";
    private string DefaultRoutingKey => "test.event";

    [Test]
    public async Task Outbox_TransactionCommitted_MessagePublished()
    {
        // Arrange
        var services = new ServiceCollection();
        base.ConfigureServices(services);
        
        services.AddRatatoskr(bus => 
        {
            bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
            bus.AddEventPublishChannel(ExchangeName, c => c.Produces<TestEvent>());
        });
        
        services.AddTestDbContext(PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();

        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);

        await EnsureQueueBoundAsync(QueueName, ExchangeName, DefaultRoutingKey);
        
        // Ensure DB Created
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            // Add entity and event
            dbContext.TestEntities.Add(new TestEntity { Name = "Outbox Test", CreatedAt = DateTimeOffset.UtcNow });
            
            dbContext.OutboxMessages.Add(new TestEvent { Id = "outbox-1", Data = "committed" }, new MessageProperties().SetRoutingKey(DefaultRoutingKey));
            
            await dbContext.SaveChangesAsync();
        }
        
        // Process Outbox
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }

        // Assert
        await Task.Delay(500);
        var message = await GetMessageAsync(QueueName);
        message.Should().NotBeNull();
    }

    [Test]
    public async Task Outbox_ToConsumer_EndToEnd()
    {
        // Arrange
        var handler = new TestEventHandler();
        var services = new ServiceCollection();
        base.ConfigureServices(services);

        services.AddRatatoskr(bus => 
        {
            bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
            bus.AddCommandConsumeChannel(QueueName, c => c
                .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(durable: false, autoDelete: true))
                .Consumes<TestEvent>());
            bus.AddHandler<TestEvent, TestEventHandler>();
        });

        services.AddSingleton(handler);
        services.AddTestDbContext(PostgresConnectionString);
        services.AddTestOutbox<TestDbContext>();

        var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<RabbitMqTopologyManager>();
        await topology.ProvisionTopologyAsync(CancellationToken.None);
        
        // Ensure DB Created
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var consumer = provider.GetRequiredService<IHostedService>();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            // Act - Stage message
            using (var scope = provider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                
                // We are sending Command style to the queue name
                dbContext.OutboxMessages.Add(new TestEvent { Id = "e2e-1", Data = "outbox->consumer" }, new MessageProperties().SetExchange(QueueName));
                
                await dbContext.SaveChangesAsync();
            }

            // Process Outbox
            using (var scope = provider.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
                await processor.ProcessAllAsync();
            }

            // Assert
            await WaitForConditionAsync(() => handler.HandledMessages.Count > 0, TimeSpan.FromSeconds(2));
            handler.HandledMessages.Should().HaveCount(1);
            handler.HandledMessages[0].Id.Should().Be("e2e-1");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
