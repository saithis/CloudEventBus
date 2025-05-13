using Microsoft.EntityFrameworkCore;
using PlaygroundApi.Database.Entities;
using Saithis.CloudEventBus.EfCoreOutbox;

namespace PlaygroundApi.Database;

public class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<Note> Notes { get; set; }
    public OutboxStagingCollection OutboxMessages { get; } = new();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxEntities();
    }
}