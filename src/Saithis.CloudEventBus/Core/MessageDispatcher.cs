using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Dispatches incoming messages to all registered handlers.
/// Supports multiple handlers per message type.
/// </summary>
public class MessageDispatcher(
    MessageHandlerRegistry handlerRegistry,
    IMessageSerializer deserializer,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageDispatcher> logger)
{
    /// <summary>
    /// Dispatches a message to all registered handlers.
    /// All handlers run in the same DI scope.
    /// </summary>
    public async Task<DispatchResult> DispatchAsync(byte[] body, MessageProperties properties, CancellationToken cancellationToken)
    {
        if (properties.Type == null)
        {
            logger.LogError("Received message without a type");
            return DispatchResult.PermanentError;
        }
        
        var registrations = handlerRegistry.GetHandlers(properties.Type);
        if (registrations.Count == 0)
        {
            logger.LogWarning("No handlers registered for event type '{EventType}'", properties.Type);
            return DispatchResult.NoHandlers;
        }
        
        // All handlers share the same message type, so deserialize once
        var messageType = registrations[0].MessageType;
        object? message;
        try
        {
            message = deserializer.Deserialize(body, messageType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize message of type '{EventType}'", properties.Type);
            return DispatchResult.PermanentError;
        }
        if (message == null)
        {
            logger.LogError("Message of type '{EventType}' deserialized to null", properties.Type);
            return DispatchResult.PermanentError;
        }
        
        var errors = 0;
        
        foreach (var registration in registrations)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                
                // Support multiple handlers of the same interface type by resolving all and matching by concrete type
                // 1. Try to find the specific handler registration among all instances of the interface
                var handlers = scope.ServiceProvider.GetServices(registration.HandlerInterfaceType);
                object? handler = handlers.FirstOrDefault(h => h != null && registration.HandlerType.IsInstanceOfType(h));
                
                // 2. Fallback to resolving the concrete type directly from DI
                handler ??= scope.ServiceProvider.GetService(registration.HandlerType);
                
                // 3. Last resort - handle resolution failure with a clear error
                if (handler == null)
                {
                    logger.LogWarning("Could not resolve handler of type '{HandlerType}' via DI", registration.HandlerType.FullName);
                    return DispatchResult.PermanentError;
                }

                var handleMethod = registration.HandlerInterfaceType.GetMethod(nameof(IMessageHandler<>.HandleAsync))!;
                await (Task)handleMethod.Invoke(handler, [message, properties, cancellationToken])!;
                
                logger.LogDebug("Handler '{Handler}' processed message '{Id}' of type '{Type}'", 
                    registration.HandlerType.Name, properties.Id, properties.Type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler '{Handler}' failed for message '{Id}' of type '{Type}'", 
                    registration.HandlerType.Name, properties.Id, properties.Type);
                errors++;
            }
        }
        
        return errors > 0 ? DispatchResult.RecoverableError : DispatchResult.Success;
    }
}

public enum DispatchResult
{
    Success,
    NoHandlers,
    RecoverableError,
    PermanentError,
}
