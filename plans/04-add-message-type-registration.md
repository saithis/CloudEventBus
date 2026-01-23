# Plan 04: Add Message Type Registration

## Priority: 4 (Foundation for Routing and CloudEvents)

## Problem

There's currently no way to identify message types. This is needed for:
- Routing messages to the correct exchange/topic
- Deserializing to the correct type on the consumer side
- CloudEvents `type` field (Plan 05)
- AsyncAPI documentation generation
- Strongly-typed configuration

Users currently have to manually set `MessageProperties.Type` every time they publish.

## Solution

Implement a message type registration system with:
1. `[CloudEvent("type")]` attribute for decoration
2. Fluent registration API for programmatic configuration
3. Message type resolver that can look up type info
4. Integration with serialization and sending

### 1. Create Message Attribute

Create `src/Saithis.CloudEventBus/CloudEventAttribute.cs`:

```csharp
namespace Saithis.CloudEventBus;

/// <summary>
/// Marks a class as a CloudEvent message with the specified type identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class CloudEventAttribute : Attribute
{
    /// <summary>
    /// The CloudEvent type identifier (e.g., "com.example.order.created").
    /// </summary>
    public string Type { get; }
    
    /// <summary>
    /// Optional source URI for this event type.
    /// </summary>
    public string? Source { get; set; }
    
    public CloudEventAttribute(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Type cannot be null or whitespace", nameof(type));
        Type = type;
    }
}
```

### 2. Create Message Type Registry

Create `src/Saithis.CloudEventBus/Core/MessageTypeRegistry.cs`:

```csharp
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
```

### 3. Create Message Type Info and Builder

Create `src/Saithis.CloudEventBus/Core/MessageTypeInfo.cs`:

```csharp
namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Information about a registered message type.
/// </summary>
public class MessageTypeInfo
{
    public Type ClrType { get; }
    public string EventType { get; }
    public string? Source { get; }
    public IReadOnlyDictionary<string, string> Extensions { get; }
    
    public MessageTypeInfo(
        Type clrType, 
        string eventType, 
        string? source = null,
        IReadOnlyDictionary<string, string>? extensions = null)
    {
        ClrType = clrType;
        EventType = eventType;
        Source = source;
        Extensions = extensions ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// Builder for configuring a message type registration.
/// </summary>
public class MessageTypeBuilder
{
    private readonly Type _clrType;
    private readonly string _eventType;
    private string? _source;
    private readonly Dictionary<string, string> _extensions = new();
    
    internal MessageTypeBuilder(Type clrType, string eventType)
    {
        _clrType = clrType;
        _eventType = eventType;
    }
    
    /// <summary>
    /// Sets the CloudEvent source for this message type.
    /// </summary>
    public MessageTypeBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }
    
    /// <summary>
    /// Adds a custom extension that will be included with every message of this type.
    /// </summary>
    public MessageTypeBuilder WithExtension(string key, string value)
    {
        _extensions[key] = value;
        return this;
    }
    
    internal MessageTypeInfo Build()
    {
        return new MessageTypeInfo(_clrType, _eventType, _source, _extensions);
    }
}
```

### 4. Update CloudEventBus to Use Registry

Update `src/Saithis.CloudEventBus/CloudEventBus.cs`:

```csharp
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(
    IMessageSerializer serializer, 
    IMessageSender sender,
    MessageTypeRegistry typeRegistry) : ICloudEventBus
{
    public async Task PublishAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        props ??= new MessageProperties();
        
        // Auto-populate type from registry if not explicitly set
        if (string.IsNullOrEmpty(props.Type))
        {
            var typeInfo = typeRegistry.GetByClrType<TMessage>();
            if (typeInfo != null)
            {
                props.Type = typeInfo.EventType;
                
                // Copy registered extensions
                foreach (var ext in typeInfo.Extensions)
                {
                    props.Extensions.TryAdd(ext.Key, ext.Value);
                }
            }
            else
            {
                // Try attribute fallback
                props.Type = typeRegistry.ResolveEventType(typeof(TMessage));
            }
        }
        
        var serializedMessage = serializer.Serialize(message, props);
        await sender.SendAsync(serializedMessage, props, cancellationToken);
    }
}
```

### 5. Create Builder Pattern for Service Registration

Create `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBusBuilder
{
    internal IServiceCollection Services { get; }
    internal MessageTypeRegistry TypeRegistry { get; } = new();
    
    internal CloudEventBusBuilder(IServiceCollection services)
    {
        Services = services;
    }
    
    /// <summary>
    /// Registers a message type with the specified event type.
    /// </summary>
    public CloudEventBusBuilder AddMessage<TMessage>(string eventType, Action<MessageTypeBuilder>? configure = null)
        where TMessage : class
    {
        TypeRegistry.Register<TMessage>(eventType, configure);
        return this;
    }
    
    /// <summary>
    /// Scans the assembly containing TMarker for [CloudEvent] decorated types.
    /// </summary>
    public CloudEventBusBuilder AddMessagesFromAssemblyContaining<TMarker>()
    {
        TypeRegistry.RegisterFromAssembly(typeof(TMarker).Assembly);
        return this;
    }
}
```

### 6. Update Service Collection Extensions

Update `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;
using Saithis.CloudEventBus.Testing;

namespace Saithis.CloudEventBus;

public static class MessageBusServiceCollectionExtensions
{
    public static IServiceCollection AddCloudEventBus(
        this IServiceCollection services,
        Action<CloudEventBusBuilder>? configure = null)
    {
        var builder = new CloudEventBusBuilder(services);
        configure?.Invoke(builder);
        
        // Freeze registry for optimal performance
        builder.TypeRegistry.Freeze();
        
        services.AddSingleton(builder.TypeRegistry);
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddSingleton<IMessageSender, ConsoleMessageSender>();
        services.AddSingleton<ICloudEventBus, CloudEventBus>();
        
        return services;
    }
    
    [Obsolete("Use AddCloudEventBus instead")]
    public static IServiceCollection AddMessageBus(this IServiceCollection services)
    {
        return services.AddCloudEventBus();
    }
}
```

---

## Example Usage

### With Attribute

```csharp
[CloudEvent("com.example.notes.added")]
public class NoteAddedEvent
{
    public required int Id { get; init; }
    public required string Text { get; init; }
}

// Registration
services.AddCloudEventBus(bus => bus
    .AddMessagesFromAssemblyContaining<NoteAddedEvent>());

// Publishing - type is auto-resolved
await bus.PublishAsync(new NoteAddedEvent { Id = 1, Text = "Hello" });
```

### With Fluent API

```csharp
services.AddCloudEventBus(bus => bus
    .AddMessage<NoteAddedEvent>("com.example.notes.added", m => m
        .WithSource("/notes-service")
        .WithExtension("rabbitmq.exchange", "notes-exchange")
        .WithExtension("rabbitmq.routingKey", "notes.added")));
```

---

## Files to Create

1. `src/Saithis.CloudEventBus/CloudEventAttribute.cs`
2. `src/Saithis.CloudEventBus/Core/MessageTypeRegistry.cs`
3. `src/Saithis.CloudEventBus/Core/MessageTypeInfo.cs`
4. `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus/CloudEventBus.cs` - Add type registry integration
2. `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs` - New registration API

## Files to Update in Examples

1. `examples/PlaygroundApi/Program.cs` - Use new registration API
2. `examples/PlaygroundApi/Events/NoteAddedEvent.cs` - Add `[CloudEvent]` attribute

---

## Testing Considerations

- Test attribute-based registration
- Test fluent API registration
- Test assembly scanning
- Test registry freezing prevents modifications
- Test auto-population of Type in CloudEventBus
- Test extension copying from registration

## Dependencies

Need to add `System.Collections.Frozen` which is available in .NET 8+. Since the project targets .NET 9, this should be available.
