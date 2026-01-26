using System.Reflection;
using System.Text.Json;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Core;


namespace Saithis.CloudEventBus.Testing;

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
        Func<TMessage, bool>? predicate = null)
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
                .Where(m => predicate(m.Deserialize<TMessage>()!))
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
    
    private static bool MatchesType<TMessage>(SentMessage message, MessageTypeRegistry? registry)
    {
        var type = typeof(TMessage);
        
        // 1. Check registry first if available
        if (registry != null)
        {
            var registryType = registry.ResolveEventType(type);
            if (!string.IsNullOrEmpty(registryType) && 
                message.Properties.Type?.Equals(registryType, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        
        // 2. Check for CloudEvent attribute
        var cloudEventAttr = type.GetCustomAttribute<CloudEventAttribute>();
        if (cloudEventAttr != null)
        {
            // Exact match on the declared CloudEvent type
            if (message.Properties.Type?.Equals(cloudEventAttr.Type, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        
        // 3. Try to match by class name (convention)
        var expectedType = type.Name;
        
        // Check if it's a CloudEvents envelope (structured mode)
        try
        {
            var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(message.Content);
            if (envelope?.Type != null)
            {
                // If the envelope type matches attribute OR class name
                if (cloudEventAttr != null && envelope.Type.Equals(cloudEventAttr.Type, StringComparison.OrdinalIgnoreCase))
                    return true;
                
                // If registry knows the type, check against that too
                if (registry != null)
                {
                    var registryType = registry.ResolveEventType(type);
                    if (!string.IsNullOrEmpty(registryType) && 
                        envelope.Type.Equals(registryType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                    
                return envelope.Type.Contains(expectedType, StringComparison.OrdinalIgnoreCase)
                    || message.Properties.Type?.Contains(expectedType, StringComparison.OrdinalIgnoreCase) == true;
            }
        }
        catch
        {
            // Not a CloudEvents envelope, continue with other checks
        }
        
        // 4. Check message properties for class name match (fallback convention)
        if (message.Properties.Type?.Contains(expectedType, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // We can't determine if it matches just by looking at content because JSON deserialization 
        // is too permissive (it will deserialize anything into an empty object).
        // If we don't have type information in the envelope or properties, we should assume it doesn't match
        // rather than giving a false positive.
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
