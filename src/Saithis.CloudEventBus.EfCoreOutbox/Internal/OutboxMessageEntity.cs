using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.EfCoreOutbox.Internal;

internal class OutboxMessageEntity
{
    public Guid Id { get; private set; }
    
    public required byte[] Content { get; init; }

    /// <summary>
    /// JSON serialized properties
    /// </summary>
    public required string SerializedProperties { get; init; }
    
    public required DateTimeOffset CreatedAt { get; init; }
    
    public DateTimeOffset? ProcessedAt { get; private set; }

    public short ErrorCount { get; private set; }

    [MaxLength(2000)] 
    public string Error { get; private set; } = string.Empty;
    
    public DateTimeOffset? FailedAt { get; private set; }
    
    public MessageProperties GetProperties() => 
        JsonSerializer.Deserialize<MessageProperties>(SerializedProperties)
        ?? throw new OutboxMessageSerializationException("Could not deserialize the message properties.", SerializedProperties);

    private OutboxMessageEntity(){}
    public static OutboxMessageEntity Create(byte[] message, MessageProperties props, TimeProvider timeProvider)
    {
        return new OutboxMessageEntity
        {
            Id = Guid.CreateVersion7(),
            SerializedProperties = JsonSerializer.Serialize(props),
            Content = message,
            CreatedAt = timeProvider.GetUtcNow(),
        };
    }

    public void MarkAsProcessed(TimeProvider timeProvider)
    {
        ProcessedAt = timeProvider.GetUtcNow();
    }

    public void PublishFailed(string error, TimeProvider timeProvider)
    {
        ErrorCount++;
        Error = error;
        FailedAt = timeProvider.GetUtcNow();
    }
}