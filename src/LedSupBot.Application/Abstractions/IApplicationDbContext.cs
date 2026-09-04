using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.Abstractions;

/// <summary>
/// Persistence boundary. EF Core implementation lives in Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<Message> Messages { get; }

    DbSet<TechnicalSupportCase> TechnicalSupportCases { get; }

    DbSet<ScreenInformation> ScreenInformations { get; }

    DbSet<ReceiverInformation> ReceiverInformations { get; }

    DbSet<PanelLabelPhoto> PanelLabelPhotos { get; }

    DbSet<ConfigConnectionInfo> ConfigConnections { get; }

    DbSet<AnyDeskInformation> AnyDeskInformations { get; }

    DbSet<AiExtractionResult> AiExtractionResults { get; }

    DbSet<BotModeChangeAudit> BotModeChangeAudits { get; }

    DbSet<KnowledgeDocument> KnowledgeDocuments { get; }

    DbSet<AiReplyContextSnapshot> AiReplyContextSnapshots { get; }

    DbSet<AiTrainingExample> AiTrainingExamples { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
