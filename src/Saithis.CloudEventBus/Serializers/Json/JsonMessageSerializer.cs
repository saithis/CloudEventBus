using System.Text;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Serializers.Json;

public class JsonMessageSerializer : IMessageSerializer
{
    public byte[] Serialize(object message, MessageProperties properties)
    {
        var json = JsonSerializer.Serialize(message);
        properties.ContentType = "application/json";
        return Encoding.UTF8.GetBytes(json);
    }
    
    public object? Deserialize(byte[] body, Type targetType, MessageProperties properties)
    {
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageProperties properties)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), properties);
    }
}