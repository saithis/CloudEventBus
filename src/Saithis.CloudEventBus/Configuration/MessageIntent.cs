namespace Saithis.CloudEventBus.Configuration;

/// <summary>
/// Defines the intent of a message registration.
/// </summary>
public enum MessageIntent
{
    /// <summary>
    /// We produce this event. We own the definition and the exchange.
    /// </summary>
    EventProduction,
    
    /// <summary>
    /// We send this command. We validate the exchange exists.
    /// </summary>
    CommandSending,
    
    /// <summary>
    /// We consume this command. We own the queue and the exchange.
    /// </summary>
    CommandConsumption,
    
    /// <summary>
    /// We consume this event. We validate the exchange exists, but own our queue/bindings.
    /// </summary>
    EventConsumption
}
