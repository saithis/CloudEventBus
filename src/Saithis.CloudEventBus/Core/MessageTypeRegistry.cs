using System.Collections.Frozen;
using System.Reflection;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Stores registered message types and their metadata.
/// </summary>
public class MessageTypeRegistry
{
    private readonly Dictionary<Type, MessageTypeInfo> _byClrType = new();
    private readonly Dictionary<string, MessageTypeInfo> _byEventType = new();
    private FrozenDictionary<Type, MessageTypeInfo>? _frozenByClrType;
    private FrozenDictionary<string, MessageTypeInfo>? _frozenByEventType;
    private bool _isFrozen;
    
    /// <summary>
    /// Registers a message type with explicit configuration.
    /// </summary>
    public void Register<TMessage>(string eventType, Action<MessageTypeBuilder>? configure = null)
        where TMessage : class
    {
        Register(typeof(TMessage), eventType, configure);
    }
    
    /// <summary>
    /// Registers a message type with explicit configuration.
    /// </summary>
    public void Register(Type clrType, string eventType, Action<MessageTypeBuilder>? configure = null)
    {
        EnsureNotFrozen();
        
        var builder = new MessageTypeBuilder(clrType, eventType);
        configure?.Invoke(builder);
        var info = builder.Build();
        
        _byClrType[clrType] = info;
        _byEventType[eventType] = info;
    }
    
    /// <summary>
    /// Scans an assembly for types decorated with [CloudEvent] and registers them.
    /// </summary>
    public void RegisterFromAssembly(Assembly assembly)
    {
        EnsureNotFrozen();
        
        var types = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<CloudEventAttribute>() != null);
            
        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<CloudEventAttribute>()!;
            var info = new MessageTypeInfo(type, attr.Type, attr.Source);
            _byClrType[type] = info;
            _byEventType[attr.Type] = info;
        }
    }
    
    /// <summary>
    /// Freezes the registry for optimal read performance. Called after all registrations.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen) return;
        
        _frozenByClrType = _byClrType.ToFrozenDictionary();
        _frozenByEventType = _byEventType.ToFrozenDictionary();
        _isFrozen = true;
    }
    
    /// <summary>
    /// Gets type info by CLR type.
    /// </summary>
    public MessageTypeInfo? GetByClrType(Type type)
    {
        if (_isFrozen)
        {
            return _frozenByClrType!.GetValueOrDefault(type);
        }
        return _byClrType.GetValueOrDefault(type);
    }
    
    /// <summary>
    /// Gets type info by CLR type.
    /// </summary>
    public MessageTypeInfo? GetByClrType<TMessage>() => GetByClrType(typeof(TMessage));
    
    /// <summary>
    /// Gets type info by event type string.
    /// </summary>
    public MessageTypeInfo? GetByEventType(string eventType)
    {
        if (_isFrozen)
        {
            return _frozenByEventType!.GetValueOrDefault(eventType);
        }
        return _byEventType.GetValueOrDefault(eventType);
    }
    
    /// <summary>
    /// Tries to resolve the event type for a CLR type, falling back to attribute if not registered.
    /// </summary>
    public string? ResolveEventType(Type type)
    {
        var info = GetByClrType(type);
        if (info != null)
            return info.EventType;
            
        // Fall back to attribute
        var attr = type.GetCustomAttribute<CloudEventAttribute>();
        return attr?.Type;
    }
    
    private void EnsureNotFrozen()
    {
        if (_isFrozen)
            throw new InvalidOperationException("Registry is frozen and cannot be modified.");
    }
}
