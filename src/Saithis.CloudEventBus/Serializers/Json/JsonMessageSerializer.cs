using System.Text;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Serializers.Json;

public class JsonMessageSerializer : IMessageSerializer
{
    public byte[] Serialize(object message, MessageEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(message);
        envelope.ContentType = "application/json";
        return Encoding.UTF8.GetBytes(json);
    }
    
    public object? Deserialize(byte[] body, Type targetType, MessageEnvelope envelope)
    {
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageEnvelope envelope)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), envelope);
    }
}