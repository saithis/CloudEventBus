using System.Collections.Frozen;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Registry of message handlers mapped by event type.
/// Supports multiple handlers per message type.
/// </summary>
public class MessageHandlerRegistry // TODO: does it make sense to combine this with MessageTypeRegistry?
{
    private readonly Dictionary<string, List<HandlerRegistration>> _handlers = new();
    private FrozenDictionary<string, IReadOnlyList<HandlerRegistration>>? _frozen;
    private bool _isFrozen;
    
    /// <summary>
    /// Registers a handler for an event type.
    /// Multiple handlers can be registered for the same event type.
    /// </summary>
    public void Register<TMessage, THandler>(string eventType)
        where TMessage : notnull
        where THandler : IMessageHandler<TMessage>
    {
        EnsureNotFrozen();
        
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers = new List<HandlerRegistration>();
            _handlers[eventType] = handlers;
        }
        
        handlers.Add(new HandlerRegistration(
            typeof(TMessage),
            typeof(THandler),
            typeof(IMessageHandler<TMessage>)));
    }
    
    /// <summary>
    /// Freezes the registry for optimal performance.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen) return;
        _frozen = _handlers.ToFrozenDictionary(
            kvp => kvp.Key, 
            kvp => (IReadOnlyList<HandlerRegistration>)kvp.Value);
        _isFrozen = true;
    }
    
    /// <summary>
    /// Gets all handler registrations for an event type.
    /// Returns empty list if no handlers registered.
    /// </summary>
    public IReadOnlyList<HandlerRegistration> GetHandlers(string eventType)
    {
        if (_isFrozen)
            return _frozen!.GetValueOrDefault(eventType) ?? [];
        return _handlers.GetValueOrDefault(eventType) ?? [];
    }
    
    /// <summary>
    /// Gets all registered event types.
    /// </summary>
    public IEnumerable<string> GetRegisteredEventTypes()
    {
        return _isFrozen ? _frozen!.Keys : _handlers.Keys;
    }
    
    private void EnsureNotFrozen()
    {
        if (_isFrozen)
            throw new InvalidOperationException("Registry is frozen.");
    }
}

public record HandlerRegistration(
    Type MessageType,
    Type HandlerType,
    Type HandlerInterfaceType);
