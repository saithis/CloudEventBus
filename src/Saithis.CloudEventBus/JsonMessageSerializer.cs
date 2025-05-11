using System.Text;
using System.Text.Json;

namespace Saithis.CloudEventBus;

public class JsonMessageSerializer : IMessageSerializer
{
    public byte[] Serialize(object message, MessageProperties messageProperties)
    {
        var json = JsonSerializer.Serialize(message);
        messageProperties.ContentType = "application/json";
        return Encoding.UTF8.GetBytes(json);
    }
}