using System.Text;
using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Serializers.Json;

public class JsonMessageSerializer : IMessageSerializer
{
    public byte[] Serialize(object message, MessageProperties messageProperties)
    {
        var json = JsonSerializer.Serialize(message);
        messageProperties.ContentType = "application/json";
        return Encoding.UTF8.GetBytes(json);
    }
    
    public object? Deserialize(byte[] body, Type targetType, MessageContext context)
    {
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageContext context)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), context);
    }
}