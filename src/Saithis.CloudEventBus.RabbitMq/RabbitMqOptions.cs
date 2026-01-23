namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqOptions
{
    /// <summary>
    /// RabbitMQ connection string (e.g., "amqp://guest:guest@localhost:5672/")
    /// </summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// Alternative to ConnectionString - individual settings
    /// </summary>
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    
    /// <summary>
    /// Default exchange to publish to if not specified in MessageProperties.Extensions
    /// </summary>
    public string DefaultExchange { get; set; } = "";
    
    /// <summary>
    /// Whether to wait for publisher confirms
    /// </summary>
    public bool UsePublisherConfirms { get; set; } = true;
    
    /// <summary>
    /// Timeout for publisher confirms
    /// </summary>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
