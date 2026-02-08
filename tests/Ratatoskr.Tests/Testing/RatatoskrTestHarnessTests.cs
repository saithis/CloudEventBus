using Microsoft.Extensions.DependencyInjection;
using Ratatoskr.Core;
using Ratatoskr.Testing;
using TUnit.Core;
using AwesomeAssertions;

namespace Ratatoskr.Tests.Testing;

public class RatatoskrTestHarnessTests
{
    [Test]
    public async Task SimulateReceiveAsync_ShouldDispatchMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<MessageTracker>();
        services.AddRatatoskr(builder =>
        {
            builder.UseInMemory();
            builder.AddHandler<TestMessage, TestHandler>();
            builder.AddEventConsumeChannel("test-in", c => c.Consumes<TestMessage>());
        });

        await using var provider = services.BuildServiceProvider();

        var harness = provider.GetRequiredService<RatatoskrTestHarness>();
        var tracker = provider.GetRequiredService<MessageTracker>();

        var message = new TestMessage { Content = "Test" };
        await harness.SimulateReceiveAsync(message);

        tracker.ReceivedMessages.Should().ContainSingle()
            .Which.Content.Should().Be("Test");
    }

    [Test]
    public async Task AssertSent_ShouldVerifyMessageSending()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRatatoskr(builder =>
        {
            builder.UseInMemory();
            builder.AddEventPublishChannel("test-out", c => c.Produces<TestMessage>());
        });

        await using var provider = services.BuildServiceProvider();

        var harness = provider.GetRequiredService<RatatoskrTestHarness>();
        var ratatoskr = provider.GetRequiredService<IRatatoskr>();

        await ratatoskr.PublishDirectAsync(new TestMessage { Content = "Hello" });

        harness.Sender.ShouldHaveSent<TestMessage>(m => m.Content == "Hello");
    }

    public class MessageTracker
    {
        public List<TestMessage> ReceivedMessages { get; } = new();
    }

    public class TestHandler(MessageTracker tracker) : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, MessageProperties properties, CancellationToken cancellationToken)
        {
            tracker.ReceivedMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    [RatatoskrMessage("test-message")]
    public class TestMessage
    {
        public string? Id { get; set; }
        public string? Content { get; set; }
    }
}
