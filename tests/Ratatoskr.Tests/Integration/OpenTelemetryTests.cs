using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ratatoskr.Core;
using Ratatoskr.EfCore;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;

namespace Ratatoskr.Tests.Integration;

public class OpenTelemetryTests(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture postgres)
    : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string ExchangeName => $"otel-test-{TestId}";
    private string QueueName => $"otel-queue-{TestId}";

    [Test]
    public async Task Tracing_WithOutbox_PropagatesContext()
    {
        // 1. Setup ActivityListener to capture spans
        var activities = new ConcurrentBag<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        // 2. Setup Ratatoskr with Outbox
        var handler = new TestEventHandler();
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus =>
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c
                    .WithRabbitMq(r => r.ExchangeTypeTopic())
                    .Produces<TestEvent>());
                bus.AddEventConsumeChannel(ExchangeName, c => c
                    .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(false, autoDelete: true))
                    .Consumes<TestEvent>());
                bus.AddHandler<TestEvent, TestEventHandler>(handler);
                bus.AddEfCoreOutbox<TestDbContext>(o => o.Options.PollingInterval = TimeSpan.FromSeconds(1));
            });

            services.AddDbContext<TestDbContext>((sp, options) =>
            {
                options.UseNpgsql(PostgresConnectionString);
                options.RegisterOutbox<TestDbContext>(sp);
            });
        });

        await InitializeDatabase();

        // 3. Act - Publish Message
        var eventId = "otel-outbox-1";
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();
            // Use Outbox explicitly
            var initialTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
            dbContext.OutboxMessages.Add(new TestEvent { Id = eventId, Data = "trace-me" }, new MessageProperties { Id = eventId, TraceParent = initialTraceParent });
            await dbContext.SaveChangesAsync();
        });

        // 4. Wait for processing
        await WaitForConditionAsync(() => handler.HandledMessages.Any(m => m.Id == eventId), TimeSpan.FromSeconds(10));

        // Wait a bit for activities to be fully finished/recorded
        await Task.Delay(500);

        // 5. Assert Activity Structure
        var relevantActivities = GetRelevantActivities(activities, eventId);

        // Expected flow (Outbox):
        // [Ratatoskr.OutboxProcess] (Producer) -> started in OutboxProcessor
        //   -> calls Sender.SendAsync
        //      -> [Ratatoskr.Send] (Client) -> started in RabbitMqMessageSender
        //         -> [Ratatoskr.Receive] (Consumer) -> started in RabbitMqConsumer

        // Note: No "Ratatoskr.Publish" here because we added directly to DB. 
        // If we want "Ratatoskr.Publish", we'd need an Outbox-aware publisher wrapper, but standard use is DbContext.Add.

        var outboxActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.OutboxProcess");
        var sendActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.Send");
        var receiveActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.Receive");

        outboxActivity.Should().NotBeNull("OutboxProcess activity should exist");
        sendActivity.Should().NotBeNull("Send activity should exist");
        receiveActivity.Should().NotBeNull("Receive activity should exist");

        // Verify Hierarchy
        sendActivity.ParentId.Should().Be(outboxActivity.Id);
        receiveActivity.ParentId.Should().Be(sendActivity.Id);

        // Verify Tags
        outboxActivity.Kind.Should().Be(ActivityKind.Producer);
        sendActivity.Kind.Should().Be(ActivityKind.Client);
        receiveActivity.Kind.Should().Be(ActivityKind.Consumer);

        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "rabbitmq");
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.name" && (string?)t.Value == ExchangeName);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.subscription.name" && (string?)t.Value == QueueName);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.rabbitmq.destination.routing_key" && (string?)t.Value == "test.event");
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.body.size");

        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "rabbitmq");
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.name" && (string?)t.Value == ExchangeName);
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.rabbitmq.destination.routing_key" && (string?)t.Value == "test.event");
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.body.size");

        outboxActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "ratatoskr");
        outboxActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
    }

    [Test]
    public async Task Tracing_WithoutOutbox_PropagatesContext()
    {
        // 1. Setup ActivityListener to capture spans
        var activities = new ConcurrentBag<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        // 2. Setup Ratatoskr WITHOUT Outbox
        var handler = new TestEventHandler();
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus =>
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddEventPublishChannel(ExchangeName, c => c
                    .WithRabbitMq(r => r.ExchangeTypeTopic())
                    .Produces<TestEvent>());
                bus.AddEventConsumeChannel(ExchangeName, c => c
                    .WithRabbitMq(o => o.QueueName(QueueName).AutoAck(false).QueueOptions(false, autoDelete: true))
                    .Consumes<TestEvent>());
                bus.AddHandler<TestEvent, TestEventHandler>(handler);
                // NO Outbox here
            });

            services.AddDbContext<TestDbContext>((sp, options) => { options.UseNpgsql(PostgresConnectionString); });
        });

        await InitializeDatabase();

        // 3. Act - Publish Message
        var eventId = "otel-direct-1";
        await InScopeAsync(async ctx =>
        {
            var bus = ctx.ServiceProvider.GetRequiredService<IRatatoskr>();
            await bus.PublishDirectAsync(new TestEvent { Id = eventId, Data = "trace-me-direct" }, new MessageProperties { Id = eventId });
        });

        // 4. Wait for processing
        await WaitForConditionAsync(() => handler.HandledMessages.Any(m => m.Id == eventId), TimeSpan.FromSeconds(10));

        // Wait a bit for activities to be fully finished/recorded
        await Task.Delay(500);

        // 5. Assert Activity Structure
        var relevantActivities = GetRelevantActivities(activities, eventId);

        // Expected flow (Direct):
        // [Ratatoskr.Publish] (Producer) -> started in Ratatoskr.PublishDirectAsync
        //   -> calls Sender.SendAsync
        //      -> [Ratatoskr.Send] (Client) -> started in RabbitMqMessageSender
        //         -> [Ratatoskr.Receive] (Consumer) -> started in RabbitMqConsumer

        var publishActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.Publish");
        var sendActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.Send");
        var receiveActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.Receive");
        var outboxActivity = relevantActivities.FirstOrDefault(a => a.OperationName == "Ratatoskr.OutboxProcess");

        publishActivity.Should().NotBeNull("Publish activity should exist");
        sendActivity.Should().NotBeNull("Send activity should exist");
        receiveActivity.Should().NotBeNull("Receive activity should exist");
        outboxActivity.Should().BeNull("OutboxProcess activity should NOT exist");

        // Verify Hierarchy
        sendActivity.ParentId.Should().Be(publishActivity.Id);
        receiveActivity.ParentId.Should().Be(sendActivity.Id);

        // Verify Tags
        publishActivity.Kind.Should().Be(ActivityKind.Producer);
        sendActivity.Kind.Should().Be(ActivityKind.Client);
        receiveActivity.Kind.Should().Be(ActivityKind.Consumer);

        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "rabbitmq");
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.name" && (string?)t.Value == ExchangeName);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.subscription.name" && (string?)t.Value == QueueName);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.rabbitmq.destination.routing_key" && (string?)t.Value == "test.event");
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
        receiveActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.body.size");

        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "rabbitmq");
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.destination.name" && (string?)t.Value == ExchangeName);
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.rabbitmq.destination.routing_key" && (string?)t.Value == "test.event");
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
        sendActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.body.size");

        publishActivity.TagObjects.Should().Contain(t => t.Key == "messaging.system" && (string?)t.Value == "ratatoskr");
        publishActivity.TagObjects.Should().Contain(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId);
    }

    private ActivityListener CreateActivityListener(ConcurrentBag<Activity> activities)
    {
        return new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Ratatoskr",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add
        };
    }

    private List<Activity> GetRelevantActivities(IEnumerable<Activity> activities, string eventId)
    {
        // Ideally we filter by TraceId containing the message
        return activities
            .GroupBy(a => a.TraceId)
            .Where(g => g.Any(a => a.TagObjects.Any(t => t.Key == "messaging.message.id" && (string?)t.Value == eventId)))
            .SelectMany(g => g)
            .OrderBy(a => a.StartTimeUtc)
            .ToList();
    }

    private async Task InitializeDatabase()
    {
        await InScopeAsync(async ctx =>
        {
            var dbContext = ctx.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        });
    }
}