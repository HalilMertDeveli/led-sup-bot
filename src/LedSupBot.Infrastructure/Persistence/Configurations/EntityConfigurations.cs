using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace LedSupBot.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(256);

        builder.Property(x => x.LinkedWhatsAppLid)
            .HasMaxLength(64);

        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique()
            .HasDatabaseName("ix_customers_phone_number");

        builder.HasMany(x => x.Conversations)
            .WithOne(x => x.Customer)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Manufacturer).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProductType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProductFamily).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DocumentVersion).HasMaxLength(64);
        builder.Property(x => x.Language).HasMaxLength(16).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExtractionMethod).HasMaxLength(64);
        builder.Property(x => x.ExtractionVersion).HasMaxLength(64);
        builder.Property(x => x.ExtractionQuality).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => x.Sha256).IsUnique().HasDatabaseName("ux_knowledge_documents_sha256");
        builder.HasIndex(x => x.Model).HasDatabaseName("ix_knowledge_documents_model");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_knowledge_documents_status");
    }
}

public sealed class PanelFilePackageConfiguration : IEntityTypeConfiguration<PanelFilePackage>
{
    public void Configure(EntityTypeBuilder<PanelFilePackage> builder)
    {
        builder.ToTable("panel_file_packages", table => table.HasCheckConstraint(
            "ck_panel_file_packages_canonical_identity",
            "\"CanonicalKey\" = \"PixelPitch\" || '-' || \"PanelCode\" || '-' || \"SecondaryCode\""));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PixelPitch).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PanelCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SecondaryCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CanonicalKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Manufacturer).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => x.CanonicalKey).IsUnique().HasDatabaseName("ux_panel_file_packages_canonical_key");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_panel_file_packages_status");
    }
}

public sealed class PanelFilePackageAssetConfiguration : IEntityTypeConfiguration<PanelFilePackageAsset>
{
    public void Configure(EntityTypeBuilder<PanelFilePackageAsset> builder)
    {
        builder.ToTable("panel_file_package_assets"); builder.HasKey(x => x.Id);
        builder.Property(x => x.AssetType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.HasOne(x => x.Package).WithMany(x => x.Assets).HasForeignKey(x => x.PanelFilePackageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PanelFilePackageId, x.AssetType, x.Version }).IsUnique()
            .HasDatabaseName("ux_panel_file_package_assets_version");
        builder.HasIndex(x => new { x.PanelFilePackageId, x.AssetType }).IsUnique()
            .HasFilter("\"Status\" = 'Active'").HasDatabaseName("ux_panel_file_package_assets_active_type");
        builder.HasIndex(x => x.Sha256).HasDatabaseName("ix_panel_file_package_assets_sha256");
    }
}

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks"); builder.HasKey(x => x.Id);
        builder.Property(x => x.SectionTitle).HasMaxLength(256);
        builder.Property(x => x.Content).HasColumnType("text").IsRequired();
        builder.Property(x => x.Embedding).HasColumnType("vector(768)");
        builder.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique().HasDatabaseName("ux_knowledge_chunks_document_index");
        builder.HasOne(x => x.Document).WithMany(x => x.Chunks).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.BotMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CurrentStage).HasConversion<string>().HasMaxLength(64);

        builder.Property(x => x.LastInboundWhatsAppAddress)
            .HasMaxLength(64);

        builder.Ignore(x => x.WhatsAppRouteJid);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_conversations_customer_id");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_conversations_status");
        builder.HasIndex(x => x.UpdatedAt).HasDatabaseName("ix_conversations_updated_at");
        builder.HasIndex(x => new { x.Status, x.UpdatedAt }).HasDatabaseName("ix_conversations_status_updated_at");

        builder.HasMany(x => x.Messages)
            .WithOne(x => x.Conversation)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.TechnicalSupportCases)
            .WithOne(x => x.Conversation)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.AiExtractionResults)
            .WithOne(x => x.Conversation)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.MessageType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SentBy).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Content).HasMaxLength(8000);
        builder.Property(x => x.ExternalMessageId).HasMaxLength(128);
        builder.Property(x => x.StorageKey).HasMaxLength(512);

        builder.HasIndex(x => x.ConversationId).HasDatabaseName("ix_messages_conversation_id");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_messages_created_at");
        builder.HasIndex(x => x.ExternalMessageId)
            .IsUnique()
            .HasFilter("\"ExternalMessageId\" IS NOT NULL")
            .HasDatabaseName("ix_messages_external_message_id");
    }
}

public sealed class TechnicalSupportCaseConfiguration : IEntityTypeConfiguration<TechnicalSupportCase>
{
    public void Configure(EntityTypeBuilder<TechnicalSupportCase> builder)
    {
        builder.ToTable("technical_support_cases");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.SummaryMarkdown).HasMaxLength(16000);
        builder.Property(x => x.IssueSummary).HasMaxLength(2000);
        builder.Property(x => x.AiOperatorSummary).HasMaxLength(2000);
        builder.Property(x => x.PlayerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProcessorHdmiConnected);
        builder.Property(x => x.QuestionAttemptsJson).HasMaxLength(2000);
        builder.Property(x => x.DiagnosticStateJson).HasColumnType("jsonb");
        builder.Property(x => x.PanelIdentityStateJson).HasColumnType("jsonb");
        builder.Property(x => x.PanelFileDeliveryConfirmationJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.ConversationId)
            .HasDatabaseName("ix_technical_support_cases_conversation_id");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_technical_support_cases_status");
    }
}

public sealed class VerifiedSupportCaseMemoryConfiguration : IEntityTypeConfiguration<VerifiedSupportCaseMemory>
{
    public void Configure(EntityTypeBuilder<VerifiedSupportCaseMemory> builder)
    {
        builder.ToTable("verified_support_case_memories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.VerifiedBy).HasMaxLength(128);
        builder.Property(x => x.PrimarySymptom).HasMaxLength(1000);
        builder.Property(x => x.NormalizedSymptom).HasMaxLength(1000);
        builder.Property(x => x.DeviceManufacturer).HasMaxLength(128);
        builder.Property(x => x.DeviceModel).HasMaxLength(128);
        builder.Property(x => x.DeviceFamily).HasMaxLength(128);
        builder.Property(x => x.ProblemCategory).HasMaxLength(128);
        builder.Property(x => x.DiagnosticSummary).HasMaxLength(3000);
        builder.Property(x => x.FinalResolution).HasMaxLength(3000);
        builder.Property(x => x.ResolutionSummary).HasMaxLength(1500);
        builder.Property(x => x.CompletedChecks).HasColumnType("jsonb");
        builder.Property(x => x.FailedChecks).HasColumnType("jsonb");
        builder.Property(x => x.SuccessfulChecks).HasColumnType("jsonb");
        builder.Property(x => x.RelevantWorkflowFacts).HasColumnType("jsonb");
        builder.Property(x => x.RelevantDiagnosticStateSnapshot).HasColumnType("jsonb");
        builder.Property(x => x.SourceMessageIds).HasColumnType("jsonb");
        builder.Property(x => x.RetrievalText).HasMaxLength(6000);
        builder.Property(x => x.Embedding).HasColumnType("vector(768)");
        builder.HasIndex(x => x.TechnicalSupportCaseId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.DeviceModel });
    }
}

public sealed class ScreenInformationConfiguration : IEntityTypeConfiguration<ScreenInformation>
{
    public void Configure(EntityTypeBuilder<ScreenInformation> builder)
    {
        builder.ToTable("screen_informations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Width).HasPrecision(12, 2);
        builder.Property(x => x.Height).HasPrecision(12, 2);
        builder.Property(x => x.Unit).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RawUtterance).HasMaxLength(1024);

        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .IsUnique()
            .HasDatabaseName("ix_screen_informations_case_id");

        builder.HasOne(x => x.TechnicalSupportCase)
            .WithOne(x => x.ScreenInformation)
            .HasForeignKey<ScreenInformation>(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReceiverInformationConfiguration : IEntityTypeConfiguration<ReceiverInformation>
{
    public void Configure(EntityTypeBuilder<ReceiverInformation> builder)
    {
        builder.ToTable("receiver_informations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Manufacturer).HasMaxLength(128);
        builder.Property(x => x.Model).HasMaxLength(128);
        builder.Property(x => x.ReceiverModel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.RawUtterance).HasMaxLength(1024);

        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .IsUnique()
            .HasDatabaseName("ix_receiver_informations_case_id");

        builder.HasOne(x => x.TechnicalSupportCase)
            .WithOne(x => x.ReceiverInformation)
            .HasForeignKey<ReceiverInformation>(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PanelLabelPhotoConfiguration : IEntityTypeConfiguration<PanelLabelPhoto>
{
    public void Configure(EntityTypeBuilder<PanelLabelPhoto> builder)
    {
        builder.ToTable("panel_label_photos");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(256);
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AnalysisStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AnalysisResultReference).HasMaxLength(1024);

        builder.HasIndex(x => x.TechnicalSupportCaseId).HasDatabaseName("ix_panel_label_photos_case_id");

        builder.HasOne(x => x.TechnicalSupportCase)
            .WithMany(x => x.PanelLabelPhotos)
            .HasForeignKey(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfigConnectionInfoConfiguration : IEntityTypeConfiguration<ConfigConnectionInfo>
{
    public void Configure(EntityTypeBuilder<ConfigConnectionInfo> builder)
    {
        builder.ToTable("config_connections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .IsUnique()
            .HasDatabaseName("ix_config_connections_case_id");

        builder.HasOne(x => x.TechnicalSupportCase)
            .WithOne(x => x.ConfigConnection)
            .HasForeignKey<ConfigConnectionInfo>(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AnyDeskInformationConfiguration : IEntityTypeConfiguration<AnyDeskInformation>
{
    public void Configure(EntityTypeBuilder<AnyDeskInformation> builder)
    {
        builder.ToTable("anydesk_informations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AnyDeskAddress).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RawUtterance).HasMaxLength(256);

        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .IsUnique()
            .HasDatabaseName("ix_anydesk_informations_case_id");

        builder.HasOne(x => x.TechnicalSupportCase)
            .WithOne(x => x.AnyDeskInformation)
            .HasForeignKey<AnyDeskInformation>(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AiExtractionResultConfiguration : IEntityTypeConfiguration<AiExtractionResult>
{
    public void Configure(EntityTypeBuilder<AiExtractionResult> builder)
    {
        builder.ToTable("ai_extraction_results");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExtractionType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PromptVersion).HasMaxLength(64);
        builder.Property(x => x.ModelName).HasMaxLength(128);

        builder.HasIndex(x => x.ConversationId).HasDatabaseName("ix_ai_extraction_results_conversation_id");
        builder.HasIndex(x => x.MessageId).HasDatabaseName("ix_ai_extraction_results_message_id");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_ai_extraction_results_created_at");

        builder.HasOne(x => x.Message)
            .WithMany(x => x.AiExtractionResults)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class BotModeChangeAuditConfiguration : IEntityTypeConfiguration<BotModeChangeAudit>
{
    public void Configure(EntityTypeBuilder<BotModeChangeAudit> builder)
    {
        builder.ToTable("bot_mode_change_audits");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PreviousMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.NewMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(64);

        builder.HasIndex(x => x.ConversationId).HasDatabaseName("ix_bot_mode_change_audits_conversation_id");
        builder.HasIndex(x => x.ChangedAt).HasDatabaseName("ix_bot_mode_change_audits_changed_at");

        builder.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
