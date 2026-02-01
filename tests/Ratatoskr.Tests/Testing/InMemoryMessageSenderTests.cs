using System.Text.Json;
using AwesomeAssertions;
using Ratatoskr.Tests.Fixtures;
using Ratatoskr.Core;
using Ratatoskr.Testing;
using TUnit.Core;

namespace Ratatoskr.Tests.Testing;

public class InMemoryMessageSenderTests
{
    [Test]
    public async Task SendAsync_CapturesMessage()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var content = "test message"u8.ToArray();
        var props = new MessageProperties { Type = "test.event" };
        
        // Act
        await sender.SendAsync(content, props, CancellationToken.None);
        
        // Assert
        sender.SentMessages.Should().HaveCount(1);
        var message = sender.SentMessages.First();
        message.Content.Should().BeEquivalentTo(content);
        message.Properties.Type.Should().Be("test.event");
    }

    [Test]
    public async Task SendAsync_MultipleMessages_CapturesAll()
    {
        // Arrange - Create a dedicated sender for this test
        var sender = new InMemoryMessageSender();
        
        // Act
        await sender.SendAsync("msg1"u8.ToArray(), new MessageProperties { Type = "type1" }, CancellationToken.None);
        await sender.SendAsync("msg2"u8.ToArray(), new MessageProperties { Type = "type2" }, CancellationToken.None);
        await sender.SendAsync("msg3"u8.ToArray(), new MessageProperties { Type = "type3" }, CancellationToken.None);
        
        // Assert - ConcurrentBag doesn't guarantee order, just verify all are captured
        sender.SentMessages.Should().HaveCount(3);
        var types = sender.SentMessages.Select(m => m.Properties.Type).OrderBy(t => t).ToList();
        types.Should().BeEquivalentTo(new[] { "type1", "type2", "type3" });
    }

    [Test]
    public async Task Clear_RemovesAllMessages()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        await sender.SendAsync("msg1"u8.ToArray(), new MessageProperties(), CancellationToken.None);
        await sender.SendAsync("msg2"u8.ToArray(), new MessageProperties(), CancellationToken.None);
        sender.SentMessages.Should().HaveCount(2);
        
        // Act
        sender.Clear();
        
        // Assert
        sender.SentMessages.Should().BeEmpty();
    }

    [Test]
    public async Task SentMessages_IsThreadSafe()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var tasks = new List<Task>();
        
        // Act - Send messages concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var content = System.Text.Encoding.UTF8.GetBytes($"msg{index}");
                await sender.SendAsync(content, new MessageProperties { Type = $"type{index}" }, CancellationToken.None);
            }));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert - All messages should be captured without loss
        sender.SentMessages.Should().HaveCount(100);
    }

    [Test]
    public async Task SentMessage_Deserialize_ReturnsCorrectObject()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var serialized = JsonSerializer.SerializeToUtf8Bytes(testEvent);
        
        await sender.SendAsync(serialized, new MessageProperties(), CancellationToken.None);
        
        // Act
        var message = sender.SentMessages.First();
        var deserialized = message.Deserialize<TestEvent>();
        
        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("123");
        deserialized.Data.Should().Be("test data");
    }

    [Test]
    public async Task SentMessage_ContentAsString_ReturnsUtf8String()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var content = "hello world"u8.ToArray();
        
        await sender.SendAsync(content, new MessageProperties(), CancellationToken.None);
        
        // Act
        var message = sender.SentMessages.First();
        var stringContent = message.ContentAsString;
        
        // Assert
        stringContent.Should().Be("hello world");
    }

    [Test]
    public async Task SentMessage_SentAt_RecordsTimestamp()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var beforeSend = DateTimeOffset.UtcNow;
        
        // Act
        await sender.SendAsync("test"u8.ToArray(), new MessageProperties(), CancellationToken.None);
        
        var afterSend = DateTimeOffset.UtcNow;
        
        // Assert
        var message = sender.SentMessages.First();
        message.SentAt.Should().BeOnOrAfter(beforeSend);
        message.SentAt.Should().BeOnOrBefore(afterSend);
    }

    [Test]
    public async Task SendAsync_WithComplexProperties_CapturesAll()
    {
        // Arrange
        var sender = new InMemoryMessageSender();
        var props = new MessageProperties
        {
            Type = "complex.event",
            Source = "/test-service",
            Subject = "test-subject",
            Id = "msg-123",
            Time = DateTimeOffset.UtcNow,
            ContentType = "application/json",
            Headers = { ["custom-header"] = "custom-value" },
            TransportMetadata = { ["tenant"] = "test-tenant" }
        };
        
        // Act
        await sender.SendAsync("test"u8.ToArray(), props, CancellationToken.None);
        
        // Assert
        var message = sender.SentMessages.First();
        message.Properties.Type.Should().Be("complex.event");
        message.Properties.Source.Should().Be("/test-service");
        message.Properties.Subject.Should().Be("test-subject");
        message.Properties.Id.Should().Be("msg-123");
        message.Properties.Headers.Should().ContainKey("custom-header");
        message.Properties.TransportMetadata.Should().ContainKey("tenant");
    }
}
