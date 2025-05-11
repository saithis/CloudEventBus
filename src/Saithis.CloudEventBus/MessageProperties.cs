namespace Saithis.CloudEventBus;

public class MessageProperties
{
    public string? Type { get; init; }
    
    public string? ContentType { get; set; }
    public Dictionary<string, object>? Headers { get; set; }
    
    /// <summary>
    /// You can store additional metadata for the <see cref="IMessageSender"/> here.
    /// </summary>
    public Dictionary<string, object>? Extensions { get; set; }
}