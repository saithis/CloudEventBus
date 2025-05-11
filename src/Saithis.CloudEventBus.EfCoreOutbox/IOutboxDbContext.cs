namespace Saithis.CloudEventBus.EfCoreOutbox;

public interface IOutboxDbContext
{
    // TODO: use dotnet 10 extension properties
    public OutboxStagingCollection OutboxMessages { get; }
}