using TUnit.Core.Interfaces;

namespace CloudEventBus.Tests.Fixtures;

/// <summary>
/// Combined fixture providing both PostgreSQL and RabbitMQ containers.
/// Used for end-to-end tests that need both databases and message broker.
/// </summary>
public class CombinedContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private PostgresContainerFixture? _postgres;
    private RabbitMqContainerFixture? _rabbitMq;
    
    public string PostgresConnectionString => _postgres?.ConnectionString 
        ?? throw new InvalidOperationException("Postgres not initialized");
    
    public string RabbitMqConnectionString => _rabbitMq?.ConnectionString 
        ?? throw new InvalidOperationException("RabbitMQ not initialized");

    public async Task InitializeAsync()
    {
        _postgres = new PostgresContainerFixture();
        _rabbitMq = new RabbitMqContainerFixture();
        
        // Initialize both in parallel for efficiency
        await Task.WhenAll(
            _postgres.InitializeAsync(),
            _rabbitMq.InitializeAsync()
        );
    }

    public async ValueTask DisposeAsync()
    {
        var tasks = new List<ValueTask>();
        
        if (_postgres != null)
            tasks.Add(_postgres.DisposeAsync());
        
        if (_rabbitMq != null)
            tasks.Add(_rabbitMq.DisposeAsync());
        
        foreach (var task in tasks)
        {
            await task;
        }
    }
}
