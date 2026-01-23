# Plan 05: Implement CloudEvents Format

## Priority: 5 (Core Library Identity)

## Depends On
- Plan 04 (Message Type Registration)

## Problem

Despite being named "CloudEventBus", the library doesn't actually produce CloudEvents-formatted messages. The [CloudEvents specification](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md) defines a standard envelope format for events that's widely adopted.

Current `MessageProperties` doesn't map to CloudEvents required fields:
- `id` - Required, unique identifier
- `source` - Required, URI identifying the event source
- `type` - Required, event type (e.g., "com.example.order.created")
- `specversion` - Required, always "1.0"

## Solution

Implement CloudEvents format support with:
1. CloudEvents envelope model
2. Structured content mode serialization (JSON envelope with data)
3. Binary content mode support via headers (for RabbitMQ/Kafka)
4. Option to disable CloudEvents wrapping for simpler use cases

### 1. Define CloudEvents Constants

Create `src/Saithis.CloudEventBus/CloudEvents/CloudEventsConstants.cs`:

```csharp
namespace Saithis.CloudEventBus.CloudEvents;

public static class CloudEventsConstants
{
    public const string SpecVersion = "1.0";
    public const string JsonContentType = "application/cloudevents+json";
    
    // Header prefixes for binary content mode
    public const string HeaderPrefix = "ce-";
    public const string IdHeader = "ce-id";
    public const string SourceHeader = "ce-source";
    public const string TypeHeader = "ce-type";
    public const string SpecVersionHeader = "ce-specversion";
    public const string TimeHeader = "ce-time";
    public const string DataContentTypeHeader = "ce-datacontenttype";
}
```

### 2. Create CloudEvent Envelope Model

Create `src/Saithis.CloudEventBus/CloudEvents/CloudEventEnvelope.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Saithis.CloudEventBus.CloudEvents;

/// <summary>
/// CloudEvents envelope in structured content mode.
/// </summary>
public class CloudEventEnvelope
{
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = CloudEventsConstants.SpecVersion;
    
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("source")]
    public required string Source { get; init; }
    
    [JsonPropertyName("type")]
    public required string Type { get; init; }
    
    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; init; }
    
    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; init; }
    
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }
    
    /// <summary>
    /// The event data (payload).
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
    
    /// <summary>
    /// Extension attributes (custom fields).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? Extensions { get; init; }
}
```

### 3. Update MessageProperties

Update `src/Saithis.CloudEventBus/Core/MessageProperties.cs`:

```csharp
namespace Saithis.CloudEventBus.Core;

public class MessageProperties
{
    /// <summary>
    /// Unique identifier for this event. Auto-generated if not set.
    /// Maps to CloudEvents "id" field.
    /// </summary>
    public string? Id { get; set; }
    
    /// <summary>
    /// The type of the message, e.g. "com.example.order-shipped".
    /// Maps to CloudEvents "type" field.
    /// </summary>
    public string? Type { get; init; }
    
    /// <summary>
    /// URI identifying the event source, e.g. "/orders-service".
    /// Maps to CloudEvents "source" field.
    /// </summary>
    public string? Source { get; init; }
    
    /// <summary>
    /// Subject of the event, e.g. order ID.
    /// Maps to CloudEvents "subject" field.
    /// </summary>
    public string? Subject { get; init; }
    
    /// <summary>
    /// Content type of the data payload.
    /// </summary>
    public string? ContentType { get; set; }
    
    /// <summary>
    /// Event timestamp. Auto-set to current time if not specified.
    /// </summary>
    public DateTimeOffset? Time { get; set; }
    
    /// <summary>
    /// Custom headers to include with the message.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();
    
    /// <summary>
    /// Transport-specific metadata (e.g., RabbitMQ exchange/routing key).
    /// Not included in CloudEvents envelope.
    /// </summary>
    public Dictionary<string, string> Extensions { get; set; } = new();
    
    /// <summary>
    /// CloudEvents extension attributes (included in envelope).
    /// </summary>
    public Dictionary<string, object> CloudEventExtensions { get; set; } = new();
}
```

### 4. Create CloudEvents Serializer Options

Create `src/Saithis.CloudEventBus/CloudEvents/CloudEventsOptions.cs`:

```csharp
namespace Saithis.CloudEventBus.CloudEvents;

public class CloudEventsOptions
{
    /// <summary>
    /// Whether to wrap messages in CloudEvents envelope. Default true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Content mode for CloudEvents serialization.
    /// </summary>
    public CloudEventsContentMode ContentMode { get; set; } = CloudEventsContentMode.Structured;
    
    /// <summary>
    /// Default source URI if not specified per message or message type.
    /// </summary>
    public string DefaultSource { get; set; } = "/";
}

public enum CloudEventsContentMode
{
    /// <summary>
    /// Entire CloudEvent is serialized as JSON including envelope and data.
    /// Content-Type: application/cloudevents+json
    /// </summary>
    Structured,
    
    /// <summary>
    /// Data is serialized directly, CloudEvents attributes are in headers.
    /// More efficient for brokers that support headers well (RabbitMQ, Kafka).
    /// </summary>
    Binary
}
```

### 5. Create CloudEvents Serializer Wrapper

Create `src/Saithis.CloudEventBus/CloudEvents/CloudEventsSerializer.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.CloudEvents;

/// <summary>
/// Wraps another serializer to add CloudEvents envelope.
/// </summary>
public class CloudEventsSerializer : IMessageSerializer
{
    private readonly IMessageSerializer _innerSerializer;
    private readonly CloudEventsOptions _options;
    private readonly MessageTypeRegistry _typeRegistry;
    private readonly TimeProvider _timeProvider;
    
    public CloudEventsSerializer(
        IMessageSerializer innerSerializer,
        CloudEventsOptions options,
        MessageTypeRegistry typeRegistry,
        TimeProvider timeProvider)
    {
        _innerSerializer = innerSerializer;
        _options = options;
        _typeRegistry = typeRegistry;
        _timeProvider = timeProvider;
    }
    
    public byte[] Serialize(object message, MessageProperties props)
    {
        if (!_options.Enabled)
        {
            return _innerSerializer.Serialize(message, props);
        }
        
        // Ensure required fields are set
        props.Id ??= Guid.NewGuid().ToString();
        props.Time ??= _timeProvider.GetUtcNow();
        props.Source ??= ResolveSource(message.GetType()) ?? _options.DefaultSource;
        props.Type ??= _typeRegistry.ResolveEventType(message.GetType());
        
        if (string.IsNullOrEmpty(props.Type))
        {
            throw new InvalidOperationException(
                $"CloudEvents 'type' is required but not set for {message.GetType().Name}. " +
                "Either register the message type or set MessageProperties.Type explicitly.");
        }
        
        return _options.ContentMode switch
        {
            CloudEventsContentMode.Structured => SerializeStructured(message, props),
            CloudEventsContentMode.Binary => SerializeBinary(message, props),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    
    private byte[] SerializeStructured(object message, MessageProperties props)
    {
        // First serialize the data
        var dataBytes = _innerSerializer.Serialize(message, props);
        var dataContentType = props.ContentType;
        
        // Deserialize data to object for embedding in envelope
        // (This is inefficient but keeps the code simple - optimize later if needed)
        object? data = JsonSerializer.Deserialize<object>(dataBytes);
        
        var envelope = new CloudEventEnvelope
        {
            Id = props.Id!,
            Source = props.Source!,
            Type = props.Type!,
            Time = props.Time,
            DataContentType = dataContentType,
            Subject = props.Subject,
            Data = data,
            Extensions = props.CloudEventExtensions.Count > 0 ? props.CloudEventExtensions : null
        };
        
        props.ContentType = CloudEventsConstants.JsonContentType;
        
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }
    
    private byte[] SerializeBinary(object message, MessageProperties props)
    {
        // Serialize data directly
        var bytes = _innerSerializer.Serialize(message, props);
        
        // Add CloudEvents attributes as headers
        props.Headers[CloudEventsConstants.SpecVersionHeader] = CloudEventsConstants.SpecVersion;
        props.Headers[CloudEventsConstants.IdHeader] = props.Id!;
        props.Headers[CloudEventsConstants.SourceHeader] = props.Source!;
        props.Headers[CloudEventsConstants.TypeHeader] = props.Type!;
        
        if (props.Time.HasValue)
        {
            props.Headers[CloudEventsConstants.TimeHeader] = props.Time.Value.ToString("O");
        }
        
        if (!string.IsNullOrEmpty(props.ContentType))
        {
            props.Headers[CloudEventsConstants.DataContentTypeHeader] = props.ContentType;
        }
        
        // Add CloudEvent extensions as headers
        foreach (var ext in props.CloudEventExtensions)
        {
            props.Headers[$"{CloudEventsConstants.HeaderPrefix}{ext.Key}"] = ext.Value.ToString() ?? "";
        }
        
        return bytes;
    }
    
    private string? ResolveSource(Type messageType)
    {
        var typeInfo = _typeRegistry.GetByClrType(messageType);
        return typeInfo?.Source;
    }
}
```

### 6. Update Builder and DI Registration

Update `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs`:

```csharp
public class CloudEventBusBuilder
{
    internal IServiceCollection Services { get; }
    internal MessageTypeRegistry TypeRegistry { get; } = new();
    internal CloudEventsOptions CloudEventsOptions { get; } = new();
    
    // ... existing code ...
    
    /// <summary>
    /// Configures CloudEvents format options.
    /// </summary>
    public CloudEventBusBuilder ConfigureCloudEvents(Action<CloudEventsOptions> configure)
    {
        configure(CloudEventsOptions);
        return this;
    }
    
    /// <summary>
    /// Disables CloudEvents envelope wrapping.
    /// </summary>
    public CloudEventBusBuilder DisableCloudEvents()
    {
        CloudEventsOptions.Enabled = false;
        return this;
    }
}
```

Update `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddCloudEventBus(
    this IServiceCollection services,
    Action<CloudEventBusBuilder>? configure = null)
{
    var builder = new CloudEventBusBuilder(services);
    configure?.Invoke(builder);
    
    builder.TypeRegistry.Freeze();
    
    services.AddSingleton(builder.TypeRegistry);
    services.AddSingleton(builder.CloudEventsOptions);
    
    // Register inner serializer
    services.AddSingleton<JsonMessageSerializer>();
    
    // Register CloudEvents wrapper as the main serializer
    services.AddSingleton<IMessageSerializer>(sp => new CloudEventsSerializer(
        sp.GetRequiredService<JsonMessageSerializer>(),
        sp.GetRequiredService<CloudEventsOptions>(),
        sp.GetRequiredService<MessageTypeRegistry>(),
        sp.GetRequiredService<TimeProvider>()));
    
    services.AddSingleton<IMessageSender, ConsoleMessageSender>();
    services.AddSingleton<ICloudEventBus, CloudEventBus>();
    
    return services;
}
```

---

## Example Output

### Structured Mode (default)

```json
{
  "specversion": "1.0",
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "source": "/notes-service",
  "type": "com.example.notes.added",
  "time": "2026-01-23T12:00:00Z",
  "datacontenttype": "application/json",
  "data": {
    "id": 1,
    "text": "Hello World"
  }
}
```

### Binary Mode

Message body:
```json
{"id": 1, "text": "Hello World"}
```

Headers:
```
ce-specversion: 1.0
ce-id: 550e8400-e29b-41d4-a716-446655440000
ce-source: /notes-service
ce-type: com.example.notes.added
ce-time: 2026-01-23T12:00:00Z
ce-datacontenttype: application/json
```

---

## Files to Create

1. `src/Saithis.CloudEventBus/CloudEvents/CloudEventsConstants.cs`
2. `src/Saithis.CloudEventBus/CloudEvents/CloudEventEnvelope.cs`
3. `src/Saithis.CloudEventBus/CloudEvents/CloudEventsOptions.cs`
4. `src/Saithis.CloudEventBus/CloudEvents/CloudEventsSerializer.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus/Core/MessageProperties.cs` - Add CloudEvents fields
2. `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs` - Add CloudEvents configuration
3. `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs` - Wire up serializer

---

## Example Usage

```csharp
// Structured mode (default)
services.AddCloudEventBus(bus => bus
    .AddMessage<NoteAddedEvent>("com.example.notes.added", m => m
        .WithSource("/notes-service"))
    .ConfigureCloudEvents(ce => 
    {
        ce.DefaultSource = "/my-app";
    }));

// Binary mode (better for RabbitMQ)
services.AddCloudEventBus(bus => bus
    .ConfigureCloudEvents(ce => 
    {
        ce.ContentMode = CloudEventsContentMode.Binary;
    }));

// Disable CloudEvents entirely
services.AddCloudEventBus(bus => bus
    .DisableCloudEvents());
```

---

## Testing Considerations

- Test structured mode produces valid CloudEvents JSON
- Test binary mode adds correct headers
- Test required fields are auto-populated
- Test exception thrown when Type is not set
- Test CloudEvent extensions are included
- Validate output against CloudEvents spec

## Future Enhancements

- Support for data_base64 (binary data in structured mode)
- CloudEvents validation
- Support for CloudEvents SDK interop
