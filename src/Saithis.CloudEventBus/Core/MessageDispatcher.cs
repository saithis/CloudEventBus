using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Dispatches incoming messages to all registered handlers.
/// Supports multiple handlers per message type.
/// </summary>
public class MessageDispatcher(
    MessageTypeRegistry typeRegistry,
    IMessageSerializer deserializer,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageDispatcher> logger)
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> _dispatchMethods = new();

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
        
        // Resolve CLR type from the event type string
        var typeInfo = typeRegistry.GetByEventType(properties.Type);
        if (typeInfo == null)
        {
             logger.LogWarning("Unknown event type '{EventType}'. No message type mapped.", properties.Type);
             return DispatchResult.NoHandlers; // Or should we fail? If we don't know the type, we can't deserialize.
        }
        
        var messageType = typeInfo.ClrType;

        object? message;
        try
        {
            message = deserializer.Deserialize(body, messageType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize message of type '{EventType}' (CLR: {ClrType})", properties.Type, messageType.Name);
            return DispatchResult.PermanentError;
        }
        if (message == null)
        {
            logger.LogError("Message of type '{EventType}' deserialized to null", properties.Type);
            return DispatchResult.PermanentError;
        }
        
        // Invoke generic dispatch method
        try 
        {
            var method = _dispatchMethods.GetOrAdd(messageType, t => 
                typeof(MessageDispatcher).GetMethod(nameof(DispatchTypedAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(t));

            var task = (Task<DispatchResult>)method.Invoke(this, [message, properties, cancellationToken])!;
            return await task;
        }
        catch (Exception ex)
        {
             logger.LogError(ex, "Error invoking dispatch for type '{EventType}'", properties.Type);
             return DispatchResult.RecoverableError;
        }
    }

    private async Task<DispatchResult> DispatchTypedAsync<TMessage>(TMessage message, MessageProperties properties, CancellationToken cancellationToken)
        where TMessage : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMessageHandler<TMessage>>().ToList();

        if (handlers.Count == 0)
        {
            logger.LogWarning("No handlers registered for message type '{ClrType}' (Event: {EventType})", typeof(TMessage).Name, properties.Type);
            return DispatchResult.NoHandlers;
        }

        var errors = 0;

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(message, properties, cancellationToken);
                logger.LogDebug("Handler '{Handler}' processed message '{Id}' of type '{EventType}'", 
                    handler.GetType().Name, properties.Id, properties.Type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler '{Handler}' failed for message '{Id}' of type '{EventType}'", 
                    handler.GetType().Name, properties.Id, properties.Type);
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
