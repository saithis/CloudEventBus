using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Testing;

/// <summary>
/// A message sender that stores messages in memory for testing.
/// Thread-safe for parallel test execution.
/// </summary>
public class InMemoryMessageSender : IMessageSender
{
    private readonly ConcurrentBag<SentMessage> _messages = new();

    /// <summary>
    /// Gets the message type registry used by this sender.
    /// </summary>
    public MessageTypeRegistry? Registry { get; }
    
    public InMemoryMessageSender(MessageTypeRegistry? registry = null)
    {
        Registry = registry;
    }
    
    /// <summary>
    /// All messages that have been sent.
    /// </summary>
    public IReadOnlyCollection<SentMessage> SentMessages => _messages.ToArray();
    
    public Task SendAsync(byte[] content, MessageEnvelope props, CancellationToken cancellationToken)
    {
        _messages.Add(new SentMessage(content, props, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Clears all sent messages. Call this in test setup/teardown.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
    }
}

/// <summary>
/// Represents a message that was sent via InMemoryMessageSender.
/// </summary>
public record SentMessage(byte[] Content, MessageEnvelope Envelope, DateTimeOffset SentAt)
{
    /// <summary>
    /// Deserializes the message content to the specified type.
    /// </summary>
    public T? Deserialize<T>()
    {
        return JsonSerializer.Deserialize<T>(Content);
    }
    
    /// <summary>
    /// Gets the content as a string (assumes UTF-8 encoding).
    /// </summary>
    public string ContentAsString => Encoding.UTF8.GetString(Content);
}
