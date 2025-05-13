namespace Saithis.CloudEventBus.Core;

public class MessageProperties
{
    public string? Type { get; init; }
    
    public string? ContentType { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    
    /// <summary>
    /// You can store additional metadata for the <see cref="IMessageSender"/> here.
    /// </summary>
    public Dictionary<string, string> Extensions { get; set; } = new();
}