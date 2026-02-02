using System.Text;
using AwesomeAssertions;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ratatoskr.Core;
using Ratatoskr.EfCore;
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
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c.Produces<TestEvent>());
            });
            
            services.AddTestDbContext(PostgresConnectionString);
            services.AddTestOutbox<TestDbContext>();
        });

        await EnsureQueueBoundAsync(QueueName, ExchangeName, DefaultRoutingKey);
        
        // Ensure DB Created
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        });

        // Act
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();

            // Add entity and event
            dbContext.TestEntities.Add(new TestEntity { Name = "Outbox Test", CreatedAt = DateTimeOffset.UtcNow });

            dbContext.OutboxMessages.Add(new TestEvent { Id = "outbox-1", Data = "committed" },
                new MessageProperties().SetRoutingKey(DefaultRoutingKey));

            await dbContext.SaveChangesAsync();
        });
        
        // Process Outbox
        await InScopeAsync(async ctx =>
        {
            var processor = ctx.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        });

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
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(durable: false, autoDelete: true))
                    .Consumes<TestEvent>());
                bus.AddHandler<TestEvent, TestEventHandler>(handler);
                bus.AddEfCoreOutbox<TestDbContext>();
            });
            
            services.AddDbContext<TestDbContext>((sp, options) =>
            {
                options.UseNpgsql(PostgresConnectionString);
                options.RegisterOutbox<TestDbContext>(sp);
            });
        });

        // Ensure DB Created
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        });
        
        // The Host is already running (started in InitializeAsync), so the consumer should be active.

        // Act - Stage message
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();

            // We are sending Command style to the queue name
            dbContext.OutboxMessages.Add(new TestEvent { Id = "e2e-1", Data = "outbox->consumer" },
                new MessageProperties().SetExchange(QueueName));

            await dbContext.SaveChangesAsync();
        });

        // Assert
        await WaitForConditionAsync(() => handler.HandledMessages.Count > 0 && handler.HandledMessages.Any(m => m.Id == "e2e-1"), TimeSpan.FromSeconds(5));
        
        handler.HandledMessages.Should().Contain(m => m.Id == "e2e-1");
    }
}
