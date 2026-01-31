using System.Collections.Concurrent;

namespace Saithis.CloudEventBus.Configuration;

public class ConsumptionRegistry
{
    private readonly ConcurrentDictionary<Type, ConsumptionRegistration> _registrations = new();
    
    // Reverse lookup for runtime dispatch: "eventType" -> Type
    // Note: Multiple types might map to same eventType? Usually 1:1. 
    // Plan says: Dispatcher resolves `string type` -> `Type messageType`.
    private readonly ConcurrentDictionary<string, Type> _eventTypeToType = new();

    public void Add(ConsumptionRegistration registration)
    {
        _registrations.TryAdd(registration.MessageType, registration);
        // We'll need a way to map "rabbit routing key" or "cloud event type" to C# Type.
        // For now, let's assume we can augment this later or use a separate mechanism if needed.
        // But wait, the plan implies options/metadata might define the unique key.
        // The ConsumptionRegistration doesn't enforce "EventType" string, but RabbitMQ options will.
    }

    public ConsumptionRegistration? Get(Type messageType)
    {
        _registrations.TryGetValue(messageType, out var registration);
        return registration;
    }

    public IEnumerable<ConsumptionRegistration> GetAll() => _registrations.Values;
}
