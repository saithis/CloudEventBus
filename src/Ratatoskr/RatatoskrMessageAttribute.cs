namespace Ratatoskr;

/// <summary>
/// Marks a class as a Ratatoskr message with the specified type identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RatatoskrMessageAttribute : Attribute
{
    /// <summary>
    /// The CloudEvent type identifier (e.g., "com.example.order.created").
    /// </summary>
    public string Type { get; }
    
    public RatatoskrMessageAttribute(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Type cannot be null or whitespace", nameof(type));
        Type = type;
    }
}
