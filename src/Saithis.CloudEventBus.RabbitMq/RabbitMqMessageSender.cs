namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender : IMessageSender
{
    public Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
        // byte[] messageBodyBytes = System.Text.Encoding.UTF8.GetBytes("Hello, world!");
        //
        // var props = new BasicProperties();
        // props.ContentType = "text/plain";
        // props.DeliveryMode = 2;
        // props.Headers = new Dictionary<string, object>();
        // props.Headers.Add("latitude",  51.5252949);
        // props.Headers.Add("longitude", -0.0905493);
        //
        // await channel.BasicPublishAsync(exchangeName, routingKey, true, props, messageBodyBytes);
    }
}
