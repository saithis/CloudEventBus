using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Ratatoskr.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL container for all tests in the session.
/// Starts once and reused across all tests for performance.
/// </summary>
public class PostgresContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    
    public string ConnectionString => _container?.GetConnectionString() 
        ?? throw new InvalidOperationException("Container not initialized");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
            
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
