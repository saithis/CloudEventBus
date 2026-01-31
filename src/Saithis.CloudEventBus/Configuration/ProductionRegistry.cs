using System.Collections.Concurrent;

namespace Saithis.CloudEventBus.Configuration;

public class ProductionRegistry
{
    private readonly ConcurrentDictionary<Type, ProductionRegistration> _registrations = new();

    public void Add(ProductionRegistration registration)
    {
        _registrations.TryAdd(registration.MessageType, registration);
    }

    public ProductionRegistration? Get(Type messageType)
    {
        _registrations.TryGetValue(messageType, out var registration);
        return registration;
    }

    public IEnumerable<ProductionRegistration> GetAll() => _registrations.Values;
}
