using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Dispatches incoming messages to all registered handlers.
/// Supports multiple handlers per message type.
/// </summary>
public class MessageDispatcher(
    MessageHandlerRegistry handlerRegistry,
    MessageTypeRegistry typeRegistry,
    IMessageSerializer deserializer,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageDispatcher> logger)
{
    /// <summary>
    /// Dispatches a message to all registered handlers.
    /// All handlers run in the same DI scope.
    /// </summary>
    public async Task<DispatchResult> DispatchAsync(byte[] body, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var registrations = handlerRegistry.GetHandlers(envelope.Type);
        if (registrations.Count == 0)
        {
            logger.LogWarning("No handlers registered for event type '{EventType}'", envelope.Type);
            return DispatchResult.NoHandlers;
        }
        
        // All handlers share the same message type, so deserialize once
        var messageType = registrations[0].MessageType;
        object? message;
        try
        {
            message = deserializer.Deserialize(body, messageType, envelope);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize message of type '{EventType}'", envelope.Type);
            return DispatchResult.DeserializationFailed;
        }
        if (message == null)
        {
            logger.LogError("Failed to deserialize message of type '{EventType}'", envelope.Type);
            return DispatchResult.DeserializationFailed;
        }
        
        using var scope = scopeFactory.CreateScope();
        var errors = new List<Exception>();
        
        foreach (var registration in registrations)
        {
            try
            {
                // Support multiple handlers of the same interface type by resolving all and matching by concrete type
                // 1. Try to find the specific handler registration among all instances of the interface
                var handlers = scope.ServiceProvider.GetServices(registration.HandlerInterfaceType);
                object? handler = handlers.FirstOrDefault(h => h != null && registration.HandlerType.IsInstanceOfType(h));
                
                // 2. Fallback to resolving the concrete type directly from DI
                handler ??= scope.ServiceProvider.GetService(registration.HandlerType);
                
                // 3. Last resort - handle resolution failure with a clear error
                if (handler == null)
                {
                    throw new InvalidOperationException($"Could not resolve handler of type {registration.HandlerType.FullName} via DI.");
                }

                var handleMethod = registration.HandlerInterfaceType.GetMethod("HandleAsync")!;
                var task = (Task)handleMethod.Invoke(handler, [message, envelope, cancellationToken])!;
                await task;
                
                logger.LogDebug("Handler '{Handler}' processed message '{Id}' of type '{Type}'", 
                    registration.HandlerType.Name, envelope.Id, envelope.Type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler '{Handler}' failed for message '{Id}' of type '{Type}'", 
                    registration.HandlerType.Name, envelope.Id, envelope.Type);
                errors.Add(ex);
            }
        }
        
        if (errors.Count > 0)
        {
            // If some handlers succeeded and some failed, throw aggregate
            throw new AggregateException(
                $"One or more handlers failed for message '{envelope.Id}'", errors);
        }
        
        return DispatchResult.Success;
    }
}

public enum DispatchResult
{
    Success,
    NoHandlers,
    DeserializationFailed
}
