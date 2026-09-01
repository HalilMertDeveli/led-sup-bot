using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<TechnicalSupportCase> TechnicalSupportCases => Set<TechnicalSupportCase>();

    public DbSet<ScreenInformation> ScreenInformations => Set<ScreenInformation>();

    public DbSet<ReceiverInformation> ReceiverInformations => Set<ReceiverInformation>();

    public DbSet<PanelLabelPhoto> PanelLabelPhotos => Set<PanelLabelPhoto>();

    public DbSet<ConfigConnectionInfo> ConfigConnections => Set<ConfigConnectionInfo>();

    public DbSet<AnyDeskInformation> AnyDeskInformations => Set<AnyDeskInformation>();

    public DbSet<AiExtractionResult> AiExtractionResults => Set<AiExtractionResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Domain.Common.Entity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(Domain.Common.Entity.UpdatedAt)).CurrentValue = utcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
