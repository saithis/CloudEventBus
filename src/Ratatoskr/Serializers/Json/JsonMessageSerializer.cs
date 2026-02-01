using System.Text;
using System.Text.Json;
using Ratatoskr.Core;

namespace Ratatoskr.Serializers.Json;

public class JsonMessageSerializer : IMessageSerializer
{
    public string ContentType => "application/json";
    
    public byte[] Serialize(object message)
    {
        var json = JsonSerializer.Serialize(message);
        return Encoding.UTF8.GetBytes(json);
    }
    
    public object? Deserialize(byte[] body, Type targetType)
    {
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage));
    }
}