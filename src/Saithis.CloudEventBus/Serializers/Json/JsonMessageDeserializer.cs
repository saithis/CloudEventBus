using System.Text.Json;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Serializers.Json;

public class JsonMessageDeserializer : IMessageDeserializer
{
    private readonly CloudEventsOptions _cloudEventsOptions;
    
    public JsonMessageDeserializer(CloudEventsOptions cloudEventsOptions)
    {
        _cloudEventsOptions = cloudEventsOptions;
    }
    
    public object? Deserialize(byte[] body, Type targetType, MessageContext context)
    {
        if (_cloudEventsOptions.Enabled && 
            _cloudEventsOptions.ContentMode == CloudEventsContentMode.Structured)
        {
            // Parse CloudEvents envelope and extract data
            var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(body);
            if (envelope?.Data != null)
            {
                // Data is already deserialized as JsonElement, need to convert
                var dataJson = JsonSerializer.Serialize(envelope.Data);
                return JsonSerializer.Deserialize(dataJson, targetType);
            }
            return null;
        }
        
        // Binary mode or CloudEvents disabled - body is the raw data
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageContext context)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), context);
    }
}
