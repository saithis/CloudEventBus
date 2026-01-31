namespace Saithis.CloudEventBus.Configuration;

public class ConsumptionBuilder<T>
{
    public ConsumptionRegistration Registration { get; }

    public ConsumptionBuilder(ConsumptionRegistration registration)
    {
        Registration = registration;
    }
}
