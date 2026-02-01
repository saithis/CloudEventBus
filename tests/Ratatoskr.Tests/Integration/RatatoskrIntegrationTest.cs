using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Ratatoskr.Tests.Fixtures;
using System.Text;
using TUnit.Core.Interfaces;

namespace Ratatoskr.Tests.Integration;

[ClassDataSource<RabbitMqContainerFixture, PostgresContainerFixture>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]
public abstract class RatatoskrIntegrationTest(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture? postgres = null) 
    : IAsyncInitializer, IAsyncDisposable
{
    private ServiceProvider? _provider;
    private IServiceScope? _scope;
    
    protected IServiceProvider Services => _scope?.ServiceProvider ?? throw new InvalidOperationException("Test not initialized");
    public RabbitMqContainerFixture RabbitMq => rabbitMq;
    public PostgresContainerFixture Postgres => postgres ?? throw new InvalidOperationException("Postgres not available");

    protected string RabbitMqConnectionString => rabbitMq.ConnectionString;
    // Override the connection string to point to the unique database for this test
    protected string PostgresConnectionString 
    {
        get 
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(postgres?.ConnectionString ?? "")
            {
                Database = $"test_{TestId}"
            };
            return builder.ToString();
        }
    }

    // Unique ID for this test instance to isolate resources
    protected string TestId { get; } = Guid.NewGuid().ToString("N");


    public virtual async Task InitializeAsync()
    {
        await CreateDatabaseAsync();

        var services = new ServiceCollection();
        ConfigureServices(services);
        
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        await OnInitializedAsync();
    }

    private async Task CreateDatabaseAsync()
    {
        if (postgres == null) return;
        
        // Connect to the maintenance database (usually 'postgres' or the one from fixture) to create the new one
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"test_{TestId}\"";
        await command.ExecuteNonQueryAsync();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
    }

    protected virtual Task OnInitializedAsync()
    {
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_scope is IAsyncDisposable asyncScope)
            await asyncScope.DisposeAsync();
        else
            _scope?.Dispose();

        if (_provider != null)
            await _provider.DisposeAsync();
            
        await OnDisposedAsync();

        await DropDatabaseAsync();
    }

    private async Task DropDatabaseAsync()
    {
        if (postgres == null) return;

        try
        {
            // Connect to the maintenance database to drop the test database
            await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            // Force drop by terminating other connections if any exist (though there shouldn't be any at this point)
            command.CommandText = $"DROP DATABASE IF EXISTS \"test_{TestId}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to drop database test_{TestId}: {ex.Message}");
            // Don't fail the test if cleanup fails, but log it
        }
    }

    protected virtual Task OnDisposedAsync()
    {
        return Task.CompletedTask;
    }

    // --- Helper Methods ---

    protected async Task PublishToRabbitMqAsync<T>(string exchange, string routingKey, T message, string? type = null)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = type ?? System.Reflection.CustomAttributeExtensions.GetCustomAttribute<RatatoskrMessageAttribute>(typeof(T))?.Type ?? typeof(T).Name,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await channel.BasicPublishAsync(exchange, routingKey, false, props, body);
    }
    
    protected async Task<uint> GetMessageCountAsync(string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        try 
        {
            return await channel.MessageCountAsync(queueName);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            // Queue might not exist
            return 0;
        }
    }

    protected async Task<BasicGetResult?> GetMessageAsync(string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        return await channel.BasicGetAsync(queueName, autoAck: true);
    }

    protected async Task EnsureQueueBoundAsync(string queueName, string exchange, string routingKey)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: true);
        
        if (!string.IsNullOrEmpty(exchange))
        {
            await channel.ExchangeDeclarePassiveAsync(exchange);
            await channel.QueueBindAsync(queueName, exchange, routingKey);
        }
    }

    protected async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(100);
        }
    }
}
