using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.EfCoreOutbox;

public class OutboxStagingCollection
{
    internal readonly Queue<Item> Queue = new();

    public void Add(object message, MessageProperties? properties = null)
    {
        Queue.Enqueue(new Item
        {
            Message = message,
            Properties = properties ?? new MessageProperties()
        });
    }

    internal class Item
    {
        internal required object Message { get; set; }
        internal required MessageProperties Properties { get; set; }
    }
}