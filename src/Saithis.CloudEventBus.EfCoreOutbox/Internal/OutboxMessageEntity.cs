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
    
    /// <summary>
    /// When the message should next be attempted. Null means ready to process.
    /// Used for exponential backoff.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }
    
    /// <summary>
    /// True if the message has permanently failed and should not be retried.
    /// </summary>
    public bool IsPoisoned { get; private set; }
    
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

    public void PublishFailed(string error, TimeProvider timeProvider, int maxRetries)
    {
        ErrorCount++;
        Error = error.Length > 2000 ? error[..2000] : error;
        FailedAt = timeProvider.GetUtcNow();
        
        if (ErrorCount >= maxRetries)
        {
            IsPoisoned = true;
            NextAttemptAt = null;
        }
        else
        {
            // Exponential backoff: 2^attempt seconds (2s, 4s, 8s, 16s, 32s, 64s, 128s, 256s...)
            // Cap at 5 minutes
            var delaySeconds = Math.Min(Math.Pow(2, ErrorCount), 300);
            NextAttemptAt = timeProvider.GetUtcNow().AddSeconds(delaySeconds);
        }
    }
    
    public void MarkAsPoisoned(string reason, TimeProvider timeProvider)
    {
        IsPoisoned = true;
        Error = reason.Length > 2000 ? reason[..2000] : reason;
        FailedAt = timeProvider.GetUtcNow();
        NextAttemptAt = null;
    }
}