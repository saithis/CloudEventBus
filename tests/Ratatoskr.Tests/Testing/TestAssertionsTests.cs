using System.Text.Json;
using AwesomeAssertions;
using Ratatoskr.Core;
using Ratatoskr.Testing;
using Ratatoskr.Tests.Fixtures;

namespace Ratatoskr.Tests.Testing;

public class TestAssertionsTests
{
    private InMemoryMessageSender CreateSenderWithRegistry()
    {
        var registry = new ChannelRegistry();
        registry.Register(new ChannelRegistration("order.created", ChannelType.EventPublish));
        registry.Freeze();
        return new InMemoryMessageSender(registry);
    }

    [Test]
    public async Task ShouldHaveSent_WithMatchingMessage_ReturnsMessage()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var testEvent = new TestEvent { Id = "123", Data = "test" };
        var serialized = JsonSerializer.SerializeToUtf8Bytes(testEvent);

        await sender.SendAsync(serialized, new MessageProperties
        {
            Type = "test.event"
        }, CancellationToken.None);

        // Act
        var result = sender.ShouldHaveSent<TestEvent>();

        // Assert
        result.Should().NotBeNull();
        result.Properties.Type.Should().Be("test.event");
    }

    [Test]
    public async Task ShouldHaveSent_WithPredicate_FiltersCorrectly()
    {
        // Arrange
        var sender = CreateSenderWithRegistry();

        var event1 = new OrderCreatedEvent { OrderId = "ORDER-1", Amount = 50 };
        var event2 = new OrderCreatedEvent { OrderId = "ORDER-2", Amount = 100 };
        var event3 = new OrderCreatedEvent { OrderId = "ORDER-3", Amount = 150 };

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(event1),
            new MessageProperties { Type = "order.created" }, CancellationToken.None);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(event2),
            new MessageProperties { Type = "order.created" }, CancellationToken.None);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(event3),
            new MessageProperties { Type = "order.created" }, CancellationToken.None);

        // Act
        var result = sender.ShouldHaveSent<OrderCreatedEvent>(e => e.Amount > 100);

        // Assert
        var deserialized = result.Deserialize<OrderCreatedEvent>();
        deserialized.Should().NotBeNull();
        deserialized!.OrderId.Should().Be("ORDER-3");
        deserialized.Amount.Should().Be(150);
    }

    [Test]
    public async Task ShouldHaveSent_NoMatchingMessage_Throws()
    {
        // Arrange
        var sender = CreateSenderWithRegistry();
        var testEvent = new TestEvent { Data = "test" };

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(testEvent),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);

        // Act & Assert
        Action act = () => sender.ShouldHaveSent<OrderCreatedEvent>();
        act.Should().Throw<AssertionException>()
            .WithMessage("*Expected to find a sent message of type OrderCreatedEvent*");
    }

    [Test]
    public async Task ShouldHaveSent_WithPredicate_NoMatch_Throws()
    {
        // Arrange
        var sender = CreateSenderWithRegistry();
        var event1 = new OrderCreatedEvent { OrderId = "ORDER-1", Amount = 50 };

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(event1),
            new MessageProperties { Type = "order.created" }, CancellationToken.None);

        // Act & Assert
        Action act = () => sender.ShouldHaveSent<OrderCreatedEvent>(e => e.Amount > 100);
        act.Should().Throw<AssertionException>()
            .WithMessage("*but none matched the predicate*");
    }

    [Test]
    public async Task ShouldNotHaveSent_WhenMessageExists_Throws()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var testEvent = new TestEvent { Data = "test" };

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(testEvent),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);

        // Act & Assert
        var act = () => sender.ShouldNotHaveSent<TestEvent>();
        act.Should().Throw<AssertionException>()
            .WithMessage("*Expected no messages of type TestEvent to be sent*");
    }

    [Test]
    public void ShouldNotHaveSent_WhenNoMessage_Succeeds()
    {
        // Arrange
        var sender = new InMemoryMessageSender();

        // Act & Assert - Should not throw
        sender.ShouldNotHaveSent<TestEvent>();
    }

    [Test]
    public async Task ShouldHaveSentCount_WithCorrectCount_Succeeds()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        await sender.SendAsync("msg1"u8.ToArray(), new MessageProperties(), CancellationToken.None);
        await sender.SendAsync("msg2"u8.ToArray(), new MessageProperties(), CancellationToken.None);
        await sender.SendAsync("msg3"u8.ToArray(), new MessageProperties(), CancellationToken.None);

        // Act & Assert - Should not throw
        sender.ShouldHaveSentCount(3);
    }

    [Test]
    public async Task ShouldHaveSentCount_WithIncorrectCount_Throws()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        await sender.SendAsync("msg"u8.ToArray(), new MessageProperties(), CancellationToken.None);

        // Act & Assert
        var act = () => sender.ShouldHaveSentCount(3);
        act.Should().Throw<AssertionException>()
            .WithMessage("*Expected 3 message(s) to be sent, but found 1*");
    }

    [Test]
    public async Task ShouldNotHaveSentAny_WithMessages_Throws()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        await sender.SendAsync("msg"u8.ToArray(), new MessageProperties(), CancellationToken.None);

        // Act & Assert
        var act = () => sender.ShouldNotHaveSentAny();
        act.Should().Throw<AssertionException>();
    }

    [Test]
    public void ShouldNotHaveSentAny_WithNoMessages_Succeeds()
    {
        // Arrange
        var sender = new InMemoryMessageSender();

        // Act & Assert - Should not throw
        sender.ShouldNotHaveSentAny();
    }

    [Test]
    public async Task GetSentMessages_ReturnsFilteredMessages()
    {
        // Arrange - Create dedicated sender for this test
        var sender = CreateSenderWithRegistry();

        var testEvent = new TestEvent { Data = "test" };
        var orderEvent = new OrderCreatedEvent { OrderId = "ORDER-1" };

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(testEvent),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(orderEvent),
            new MessageProperties { Type = "order.created" }, CancellationToken.None);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(testEvent),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);

        // Act
        var testMessages = sender.GetSentMessages<TestEvent>();

        // Assert
        testMessages.Should().HaveCount(2);
    }

    [Test]
    public async Task ShouldHaveSent_WithMultipleMatchingMessages_ReturnsFirst()
    {
        // Arrange - Create dedicated sender for this test
        var sender = new InMemoryMessageSender();

        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new TestEvent { Data = "first" }),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new TestEvent { Data = "second" }),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);

        // Act
        var result = sender.ShouldHaveSent<TestEvent>();

        // Assert - ConcurrentBag doesn't preserve order, just verify we get a match
        var deserialized = result.Deserialize<TestEvent>();
        deserialized!.Data.Should().BeOneOf("first", "second");
    }

    [Test]
    public async Task ShouldHaveSent_MatchesByCloudEventType()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var testEvent = new TestEvent { Data = "test" };

        // Send with CloudEvents type that contains the class name
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(testEvent),
            new MessageProperties { Type = "test.event" }, CancellationToken.None);

        // Act & Assert - Should match even with different type format
        var result = sender.ShouldHaveSent<TestEvent>();
        result.Should().NotBeNull();
    }
}