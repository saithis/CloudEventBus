using System.Text;
using System.Text.Json;

namespace Saithis.CloudEventBus;

public class ConsoleMessageSender() : IMessageSender
{
    public Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        Console.WriteLine("= Sending Message ===========");
        Console.WriteLine(Encoding.UTF8.GetString(content));
        Console.WriteLine("= Properties ================");
        Console.WriteLine(JsonSerializer.Serialize(props));
        Console.WriteLine("= End =======================");
        return Task.CompletedTask;
    }
}