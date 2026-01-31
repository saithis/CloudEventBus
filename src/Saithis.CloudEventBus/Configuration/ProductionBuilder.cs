namespace Saithis.CloudEventBus.Configuration;

public class ProductionBuilder<T>
{
    public ProductionRegistration Registration { get; }

    public ProductionBuilder(ProductionRegistration registration)
    {
        Registration = registration;
    }
}
