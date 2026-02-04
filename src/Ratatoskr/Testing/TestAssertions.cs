using System.Reflection;
using System.Text.Json;
using Ratatoskr.CloudEvents;
using Ratatoskr.Core;
using Ratatoskr;


namespace Ratatoskr.Testing;

/// <summary>
/// Extension methods for testing message sending behavior.
/// </summary>
public static class TestAssertions
{
    /// <summary>
    /// Asserts that a message of the specified type was sent.
    /// </summary>
    public static SentMessage ShouldHaveSent<TMessage>(
        this InMemoryMessageSender sender,
        Func<TMessage, bool>? predicate = null,
        JsonSerializerOptions? options = null)
    {
        var matching = sender.SentMessages
            .Where(m => MatchesType<TMessage>(m, sender.Registry))
            .ToList();
        
        if (matching.Count == 0)
        {
            throw new AssertionException(
                $"Expected to find a sent message of type {typeof(TMessage).Name}, but none were found. " +
                $"Messages sent: [{string.Join(", ", sender.SentMessages.Select(m => m.Properties.Type))}]");
        }
        
        if (predicate != null)
        {
            var withPredicate = matching
                .Where(m => predicate(m.Deserialize<TMessage>(options)!))
                .ToList();
            
            if (withPredicate.Count == 0)
            {
                throw new AssertionException(
                    $"Found {matching.Count} message(s) of type {typeof(TMessage).Name}, " +
                    "but none matched the predicate.");
            }
            
            return withPredicate.First();
        }
        
        return matching.First();
    }
    
    /// <summary>
    /// Asserts that no message of the specified type was sent.
    /// </summary>
    public static void ShouldNotHaveSent<TMessage>(this InMemoryMessageSender sender)
    {
        var matching = sender.SentMessages.Where(m => MatchesType<TMessage>(m, sender.Registry)).ToList();
        
        if (matching.Count > 0)
        {
            throw new AssertionException(
                $"Expected no messages of type {typeof(TMessage).Name} to be sent, " +
                $"but found {matching.Count}.");
        }
    }
    
    /// <summary>
    /// Asserts that exactly the specified number of messages were sent.
    /// </summary>
    public static void ShouldHaveSentCount(this InMemoryMessageSender sender, int expectedCount)
    {
        var actualCount = sender.SentMessages.Count;
        if (actualCount != expectedCount)
        {
            throw new AssertionException(
                $"Expected {expectedCount} message(s) to be sent, but found {actualCount}.");
        }
    }

    /// <summary>
    /// Asserts that exactly the specified number of messages of the specified type were sent.
    /// </summary>
    public static void ShouldHaveSentCount<TMessage>(this InMemoryMessageSender sender, int expectedCount)
    {
        var matching = sender.SentMessages.Where(m => MatchesType<TMessage>(m, sender.Registry)).ToList();
        
        if (matching.Count != expectedCount)
        {
            throw new AssertionException(
                $"Expected {expectedCount} message(s) of type {typeof(TMessage).Name} to be sent, but found {matching.Count}.");
        }
    }
    
    /// <summary>
    /// Asserts that no messages were sent.
    /// </summary>
    public static void ShouldNotHaveSentAny(this InMemoryMessageSender sender)
    {
        sender.ShouldHaveSentCount(0);
    }
    
    /// <summary>
    /// Gets all sent messages of the specified type.
    /// </summary>
    public static IReadOnlyList<SentMessage> GetSentMessages<TMessage>(this InMemoryMessageSender sender)
    {
        return sender.SentMessages.Where(m => MatchesType<TMessage>(m, sender.Registry)).ToList();
    }

    /// <summary>
    /// Waits for a message of the specified type to be sent.
    /// </summary>
    public static async Task<SentMessage> WaitForSentAsync<TMessage>(
        this InMemoryMessageSender sender, 
        Func<TMessage, bool>? predicate = null,
        TimeSpan? timeout = null,
        JsonSerializerOptions? options = null)
    {
        return await sender.WaitForMessageAsync(m => 
        {
            if (!MatchesType<TMessage>(m, sender.Registry)) return false;
            if (predicate == null) return true;
            
            var item = m.Deserialize<TMessage>(options);
            return item != null && predicate(item);
        }, timeout);
    }
    
    private static bool MatchesType<TMessage>(SentMessage message, ChannelRegistry? registry)
    {
        var type = typeof(TMessage);
        string? expectedTypeName = null;
        
        // 1. Check registry first if available
        var messageRegistration = registry?.FindPublishChannelForMessage(type)?.GetMessage(type);
        if (messageRegistration != null)
        {
            expectedTypeName = messageRegistration.MessageTypeName;
        }
        
        // 2. Fallback to CloudEvent attribute if not found in registry (or registry missing)
        if (expectedTypeName == null)
        {
            var messageAttribute = type.GetCustomAttribute<RatatoskrMessageAttribute>();
            expectedTypeName = messageAttribute?.Type;
        }

        if (expectedTypeName != null)
        {
             return message.Properties.Type?.Equals(expectedTypeName, StringComparison.OrdinalIgnoreCase) == true;
        }
        
        // If we can't determine the expected type, we can't match it reliably.
        // We avoid sniffing the JSON content to prevent false positives and hidden bugs.
        return false;
    }
}

/// <summary>
/// Exception thrown when a test assertion fails.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
