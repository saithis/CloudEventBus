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
}