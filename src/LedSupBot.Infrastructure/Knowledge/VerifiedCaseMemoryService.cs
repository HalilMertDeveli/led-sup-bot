using System.Text.Json;
using LedSupBot.Application.CaseMemory;
using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Entities;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed class VerifiedCaseMemoryService(ApplicationDbContext db, IEmbeddingService embeddings) : IVerifiedCaseMemoryService
{
    public async Task<VerifiedSupportCaseMemory?> CreateDraftAsync(Guid caseId, CaseMemoryEdit? edit, CancellationToken ct = default)
    {
        var existing = await db.VerifiedSupportCaseMemories.SingleOrDefaultAsync(x => x.TechnicalSupportCaseId == caseId, ct);
        if (existing is not null) return existing;
        var support = await db.TechnicalSupportCases.AsNoTracking().Include(x => x.ReceiverInformation)
            .SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (support is null) return null;
        var seed = edit ?? new CaseMemoryEdit(support.IssueSummary ?? "Operator review required", null,
            support.ReceiverInformation?.Model, null, null, support.DiagnosticStateJson ?? "{}", "", "", 0.5);
        var memory = new VerifiedSupportCaseMemory { TechnicalSupportCaseId = caseId, ConversationId = support.ConversationId };
        Apply(memory, seed); db.VerifiedSupportCaseMemories.Add(memory); await db.SaveChangesAsync(ct); return memory;
    }

    public async Task<VerifiedSupportCaseMemory?> UpdateDraftAsync(Guid id, CaseMemoryEdit edit, CancellationToken ct = default)
    {
        var memory = await db.VerifiedSupportCaseMemories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (memory is null || memory.Status is VerifiedCaseMemoryStatus.Archived) return null;
        Apply(memory, edit); memory.Status = VerifiedCaseMemoryStatus.Draft; memory.VerifiedAt = null; memory.VerifiedBy = null;
        memory.Embedding = null; await db.SaveChangesAsync(ct); return memory;
    }

    public async Task<VerifiedSupportCaseMemory?> VerifyAsync(Guid id, string operatorReference, CancellationToken ct = default)
    {
        var memory = await db.VerifiedSupportCaseMemories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (memory is null || string.IsNullOrWhiteSpace(memory.FinalResolution)) return null;
        if (memory.Status == VerifiedCaseMemoryStatus.Verified) return memory;
        var vector = (await embeddings.EmbedAsync([memory.RetrievalText], ct)).Single();
        memory.Embedding = new Vector(vector); memory.Status = VerifiedCaseMemoryStatus.Verified;
        memory.VerifiedAt = DateTimeOffset.UtcNow; memory.VerifiedBy = CaseMemoryPrivacy.Sanitize(operatorReference, 128);
        await db.SaveChangesAsync(ct); return memory;
    }

    public async Task<VerifiedSupportCaseMemory?> SetStatusAsync(Guid id, VerifiedCaseMemoryStatus status, CancellationToken ct = default)
    {
        if (status == VerifiedCaseMemoryStatus.Verified) return null; // verification requires the explicit operator path
        var memory = await db.VerifiedSupportCaseMemories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (memory is null) return null;
        memory.Status = status; await db.SaveChangesAsync(ct); return memory;
    }

    public async Task<IReadOnlyList<VerifiedSupportCaseMemory>> ListAsync(VerifiedCaseMemoryStatus? status, string? model, string? category, CancellationToken ct = default)
    {
        var query = db.VerifiedSupportCaseMemories.AsNoTracking();
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(model)) query = query.Where(x => x.DeviceModel == model);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.ProblemCategory == category);
        return await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(ct);
    }

    public async Task<CaseMemorySearchResult> SearchAsync(string symptom, string? deviceModel, IReadOnlyCollection<string> completedChecks, CancellationToken ct = default)
    {
        try
        {
            var clean = CaseMemoryPrivacy.Sanitize(symptom, 1000);
            if (string.IsNullOrWhiteSpace(clean)) return new(true, []);
            var vector = new Vector((await embeddings.EmbedAsync([clean], ct)).Single());
            var query = db.VerifiedSupportCaseMemories.AsNoTracking()
                .Where(x => x.Status == VerifiedCaseMemoryStatus.Verified && x.Embedding != null);
            if (!string.IsNullOrWhiteSpace(deviceModel))
                query = query.Where(x => x.DeviceModel == deviceModel || x.DeviceModel == null);
            var rows = await query.Select(x => new { Memory = x, Distance = x.Embedding!.CosineDistance(vector) })
                .OrderBy(x => x.Distance).Take(3).ToListAsync(ct);
            var completed = completedChecks.Select(Normalize).ToHashSet();
            return new(true, rows.Where(x => 1d - x.Distance >= 0.72)
                .Select(x => new CaseMemoryCandidate(x.Memory.Id, x.Memory.PrimarySymptom, x.Memory.DeviceModel,
                    x.Memory.ProblemCategory, RemoveRepeatedChecks(x.Memory.ResolutionSummary, completed),
                    x.Memory.Confidence, 1d - x.Distance)).Where(x => !string.IsNullOrWhiteSpace(x.Resolution)).ToList());
        }
        catch { return CaseMemorySearchResult.Failed; }
    }

    private static void Apply(VerifiedSupportCaseMemory memory, CaseMemoryEdit edit)
    {
        memory.PrimarySymptom = CaseMemoryPrivacy.Sanitize(edit.PrimarySymptom, 1000);
        memory.NormalizedSymptom = Normalize(memory.PrimarySymptom);
        memory.DeviceManufacturer = Clean(edit.DeviceManufacturer, 128); memory.DeviceModel = Clean(edit.DeviceModel, 128);
        memory.DeviceFamily = Clean(edit.DeviceFamily, 128); memory.ProblemCategory = Clean(edit.ProblemCategory, 128);
        memory.DiagnosticSummary = CaseMemoryPrivacy.Sanitize(edit.DiagnosticSummary);
        memory.FinalResolution = CaseMemoryPrivacy.Sanitize(edit.FinalResolution);
        memory.ResolutionSummary = CaseMemoryPrivacy.Sanitize(edit.ResolutionSummary, 1500);
        memory.Confidence = Math.Clamp(edit.Confidence, 0, 1);
        memory.RetrievalText = CaseMemoryPrivacy.Sanitize($"Symptom: {memory.PrimarySymptom}; Device: {memory.DeviceModel}; Category: {memory.ProblemCategory}; Experience: {memory.ResolutionSummary}", 6000);
    }
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : CaseMemoryPrivacy.Sanitize(value, max);
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string RemoveRepeatedChecks(string resolution, HashSet<string> completed) =>
        completed.Any(x => resolution.Contains(x, StringComparison.OrdinalIgnoreCase)) ? string.Empty : resolution;
}
