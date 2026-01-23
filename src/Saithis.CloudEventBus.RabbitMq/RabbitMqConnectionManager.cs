using RabbitMQ.Client;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    
    public RabbitMqConnectionManager(RabbitMqOptions options)
    {
        _options = options;
    }
    
    public async Task<IChannel> CreateChannelAsync(bool enablePublisherConfirms, CancellationToken cancellationToken = default)
    {
        var connection = await GetOrCreateConnectionAsync(cancellationToken);
        
        if (enablePublisherConfirms)
        {
            var options = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );
            return await connection.CreateChannelAsync(options, cancellationToken);
        }
        
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }
    
    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;
            
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;
                
            var factory = CreateConnectionFactory();
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    private ConnectionFactory CreateConnectionFactory()
    {
        if (!string.IsNullOrEmpty(_options.ConnectionString))
        {
            return new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
        }
        
        return new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
        };
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
        _connectionLock.Dispose();
    }
}
