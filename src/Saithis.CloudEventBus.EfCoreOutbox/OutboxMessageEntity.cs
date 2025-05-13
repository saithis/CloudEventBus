using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Saithis.CloudEventBus.EfCoreOutbox;

public class OutboxMessageEntity
{
    public Guid Id { get; private set; }
    
    public required byte[] Content { get; init; }

    public required string PropertiesAsJson { get; init; }
    
    public required DateTimeOffset CreatedAt { get; init; }
    
    public DateTimeOffset? ProcessedAt { get; private set; }

    public short ErrorCount { get; private set; } = 0;

    [MaxLength(2000)] 
    public string Error { get; private set; } = string.Empty;
    
    public DateTimeOffset? FailedAt { get; private set; }
    
    public MessageProperties GetProperties() => 
        JsonSerializer.Deserialize<MessageProperties>(PropertiesAsJson)
        ?? throw new MessagePropertyDeserializationOutboxException("Could not deserialize the message properties.", PropertiesAsJson);

    private OutboxMessageEntity(){}
    public static OutboxMessageEntity Create(byte[] message, MessageProperties props, TimeProvider timeProvider)
    {
        return new OutboxMessageEntity
        {
            Id = Guid.CreateVersion7(),
            PropertiesAsJson = JsonSerializer.Serialize(props),
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