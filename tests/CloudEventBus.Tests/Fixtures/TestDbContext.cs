using Microsoft.EntityFrameworkCore;
using Saithis.CloudEventBus.EfCoreOutbox;

namespace CloudEventBus.Tests.Fixtures;

/// <summary>
/// Test DbContext that implements IOutboxDbContext for testing outbox pattern
/// </summary>
public class TestDbContext : DbContext, IOutboxDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<TestEntity> TestEntities { get; set; } = null!;
    
    public OutboxStagingCollection OutboxMessages { get; } = new();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure test entity
        modelBuilder.Entity<TestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });
        
        // Configure outbox entities
        modelBuilder.AddOutboxEntities();
    }
}
