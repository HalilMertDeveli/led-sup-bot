using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhaseT1TeacherModeTrainingData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_reply_context_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalSupportCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiResponseMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSnapshotSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OriginalCustomerMessage = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    OriginalAiResponse = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CurrentIntent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResponseAction = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReplySource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GeminiModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptPersonaVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    KnownFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    MissingFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    DeferredMissingFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    PendingWorkflowFactSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WorkflowStageSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DiagnosticContextSnapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DetectedModelsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    ImageContextSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RagAttempted = table.Column<bool>(type: "boolean", nullable: false),
                    RagUsed = table.Column<bool>(type: "boolean", nullable: false),
                    GroundingLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GroundingConfidence = table.Column<double>(type: "double precision", nullable: true),
                    GroundingConflictDetected = table.Column<bool>(type: "boolean", nullable: false),
                    KnowledgeSourceReferences = table.Column<string>(type: "jsonb", nullable: false),
                    VisionUsed = table.Column<bool>(type: "boolean", nullable: false),
                    OcrUsed = table.Column<bool>(type: "boolean", nullable: false),
                    BotModeSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceDecision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TechnicalClaimRequiresVerification = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresAnonymization = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_reply_context_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_reply_context_snapshots_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_reply_context_snapshots_messages_AiResponseMessageId",
                        column: x => x.AiResponseMessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_reply_context_snapshots_messages_CustomerMessageId",
                        column: x => x.CustomerMessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_reply_context_snapshots_technical_support_cases_Technica~",
                        column: x => x.TechnicalSupportCaseId,
                        principalTable: "technical_support_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_training_examples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalSupportCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiResponseMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSnapshotSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    FeedbackStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalCustomerMessage = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    OriginalAiResponse = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    PreferredTechnicianResponse = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    FeedbackReasonTags = table.Column<string>(type: "jsonb", nullable: false),
                    CurrentIntent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResponseAction = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReplySource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GeminiModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptPersonaVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    KnownFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    MissingFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    DeferredMissingFactsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    PendingWorkflowFactSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WorkflowStageSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DiagnosticContextSnapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DetectedModelsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    ImageContextSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RagAttempted = table.Column<bool>(type: "boolean", nullable: false),
                    RagUsed = table.Column<bool>(type: "boolean", nullable: false),
                    GroundingLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GroundingConfidence = table.Column<double>(type: "double precision", nullable: true),
                    GroundingConflictDetected = table.Column<bool>(type: "boolean", nullable: false),
                    KnowledgeSourceReferences = table.Column<string>(type: "jsonb", nullable: false),
                    VisionUsed = table.Column<bool>(type: "boolean", nullable: false),
                    OcrUsed = table.Column<bool>(type: "boolean", nullable: false),
                    BotModeSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceDecision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrainingEligibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EligibleForSft = table.Column<bool>(type: "boolean", nullable: false),
                    EligibleForPreference = table.Column<bool>(type: "boolean", nullable: false),
                    TechnicalClaimRequiresVerification = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresAnonymization = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_training_examples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_training_examples_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_training_examples_messages_AiResponseMessageId",
                        column: x => x.AiResponseMessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_training_examples_messages_CustomerMessageId",
                        column: x => x.CustomerMessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_training_examples_technical_support_cases_TechnicalSuppo~",
                        column: x => x.TechnicalSupportCaseId,
                        principalTable: "technical_support_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_reply_context_snapshots_case_id",
                table: "ai_reply_context_snapshots",
                column: "TechnicalSupportCaseId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_reply_context_snapshots_conversation_id",
                table: "ai_reply_context_snapshots",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_reply_context_snapshots_created_at",
                table: "ai_reply_context_snapshots",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_ai_reply_context_snapshots_customer_message_id",
                table: "ai_reply_context_snapshots",
                column: "CustomerMessageId");

            migrationBuilder.CreateIndex(
                name: "ux_ai_reply_context_snapshots_ai_response_message_id",
                table: "ai_reply_context_snapshots",
                column: "AiResponseMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_case_id",
                table: "ai_training_examples",
                column: "TechnicalSupportCaseId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_conversation_id",
                table: "ai_training_examples",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_current_intent",
                table: "ai_training_examples",
                column: "CurrentIntent");

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_customer_message_id",
                table: "ai_training_examples",
                column: "CustomerMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_eligibility_created_at",
                table: "ai_training_examples",
                columns: new[] { "TrainingEligibility", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_feedback_created_at",
                table: "ai_training_examples",
                columns: new[] { "FeedbackStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_training_examples_source_created_at",
                table: "ai_training_examples",
                columns: new[] { "SourceDecision", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_training_examples_ai_response_message_id",
                table: "ai_training_examples",
                column: "AiResponseMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_reply_context_snapshots");

            migrationBuilder.DropTable(
                name: "ai_training_examples");
        }
    }
}
