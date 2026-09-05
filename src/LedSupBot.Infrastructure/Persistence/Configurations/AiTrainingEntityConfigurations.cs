using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedSupBot.Infrastructure.Persistence.Configurations;

public sealed class AiReplyContextSnapshotConfiguration : IEntityTypeConfiguration<AiReplyContextSnapshot>
{
    public void Configure(EntityTypeBuilder<AiReplyContextSnapshot> builder)
    {
        builder.ToTable("ai_reply_context_snapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OriginalCustomerMessage).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.OriginalAiResponse).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.CurrentIntent).HasMaxLength(128);
        builder.Property(x => x.ResponseAction).HasMaxLength(128);
        builder.Property(x => x.ReplySource).HasMaxLength(128);
        builder.Property(x => x.GeminiModel).HasMaxLength(128);
        builder.Property(x => x.PromptPersonaVersion).HasMaxLength(128);
        builder.Property(x => x.KnownFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MissingFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DeferredMissingFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PendingWorkflowFactSnapshot).HasMaxLength(128);
        builder.Property(x => x.WorkflowStageSnapshot).HasMaxLength(128);
        builder.Property(x => x.DiagnosticContextSnapshot).HasMaxLength(4000);
        builder.Property(x => x.DetectedModelsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ImageContextSummary).HasMaxLength(4000);
        builder.Property(x => x.PanelIdentityContextSnapshot).HasColumnType("jsonb");
        builder.Property(x => x.GroundingLevel).HasMaxLength(64);
        builder.Property(x => x.KnowledgeSourceReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.WebSourceReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CaseMemoryReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BotModeSnapshot).HasMaxLength(32);
        builder.Property(x => x.SourceDecision).HasConversion<string>().HasMaxLength(64);

        builder.HasIndex(x => x.AiResponseMessageId)
            .IsUnique()
            .HasDatabaseName("ux_ai_reply_context_snapshots_ai_response_message_id");
        builder.HasIndex(x => x.ConversationId)
            .HasDatabaseName("ix_ai_reply_context_snapshots_conversation_id");
        builder.HasIndex(x => x.CustomerMessageId)
            .HasDatabaseName("ix_ai_reply_context_snapshots_customer_message_id");
        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .HasDatabaseName("ix_ai_reply_context_snapshots_case_id");
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_ai_reply_context_snapshots_created_at");

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TechnicalSupportCase>()
            .WithMany()
            .HasForeignKey(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.CustomerMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.AiResponseMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AiTrainingExampleConfiguration : IEntityTypeConfiguration<AiTrainingExample>
{
    public void Configure(EntityTypeBuilder<AiTrainingExample> builder)
    {
        builder.ToTable("ai_training_examples");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FeedbackStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TrainingEligibility).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SourceDecision).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.OriginalCustomerMessage).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.OriginalAiResponse).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.PreferredTechnicianResponse).HasMaxLength(8000);
        builder.Property(x => x.FeedbackReasonTags).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CurrentIntent).HasMaxLength(128);
        builder.Property(x => x.ResponseAction).HasMaxLength(128);
        builder.Property(x => x.ReplySource).HasMaxLength(128);
        builder.Property(x => x.GeminiModel).HasMaxLength(128);
        builder.Property(x => x.PromptPersonaVersion).HasMaxLength(128);
        builder.Property(x => x.KnownFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MissingFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DeferredMissingFactsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PendingWorkflowFactSnapshot).HasMaxLength(128);
        builder.Property(x => x.WorkflowStageSnapshot).HasMaxLength(128);
        builder.Property(x => x.DiagnosticContextSnapshot).HasMaxLength(4000);
        builder.Property(x => x.DetectedModelsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ImageContextSummary).HasMaxLength(4000);
        builder.Property(x => x.GroundingLevel).HasMaxLength(64);
        builder.Property(x => x.KnowledgeSourceReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.WebSourceReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CaseMemoryReferences).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BotModeSnapshot).HasMaxLength(32);

        builder.HasIndex(x => x.AiResponseMessageId)
            .IsUnique()
            .HasDatabaseName("ux_ai_training_examples_ai_response_message_id");
        builder.HasIndex(x => x.ConversationId)
            .HasDatabaseName("ix_ai_training_examples_conversation_id");
        builder.HasIndex(x => x.CustomerMessageId)
            .HasDatabaseName("ix_ai_training_examples_customer_message_id");
        builder.HasIndex(x => x.TechnicalSupportCaseId)
            .HasDatabaseName("ix_ai_training_examples_case_id");
        builder.HasIndex(x => new { x.FeedbackStatus, x.CreatedAt })
            .HasDatabaseName("ix_ai_training_examples_feedback_created_at");
        builder.HasIndex(x => new { x.TrainingEligibility, x.CreatedAt })
            .HasDatabaseName("ix_ai_training_examples_eligibility_created_at");
        builder.HasIndex(x => new { x.SourceDecision, x.CreatedAt })
            .HasDatabaseName("ix_ai_training_examples_source_created_at");
        builder.HasIndex(x => x.CurrentIntent)
            .HasDatabaseName("ix_ai_training_examples_current_intent");

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TechnicalSupportCase>()
            .WithMany()
            .HasForeignKey(x => x.TechnicalSupportCaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.CustomerMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.AiResponseMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
