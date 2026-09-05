using System.Text.Json;
using LedSupBot.Application.Media;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.IntegrationTests;

[Collection("workflow")]
public sealed class PanelIdentityPhotoWorkflowTests(WorkflowFixture fixture)
{
    [Fact]
    public async Task PF4_Clear_ocr_persists_exact_identity_and_uses_existing_resolver()
    {
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        Guid packageId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>();
            var assets = scope.ServiceProvider.GetRequiredService<IPanelFileAssetRegistry>();
            var package = await registry.CreateDraftAsync("P2.5", code, "2012");
            packageId = package.Id;
            await assets.StoreAsync(package.Id, PanelFileAssetType.FirmwareHex, "safe.hex", new MemoryStream([1]));
            await assets.StoreAsync(package.Id, PanelFileAssetType.ReceiverConfigRcvbp, "safe.rcvbp", new MemoryStream([2]));
            Assert.True(await registry.ActivateAsync(package.Id));
        }
        Configure(Observation("P2.5", code, "2012", PanelLabelRecognitionStatus.CompleteHighConfidence, true, false));
        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-clear-{Guid.NewGuid():N}", MessageType.Image, null);

        await using var verify = fixture.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var support = await db.TechnicalSupportCases.AsNoTracking().SingleAsync(x => x.ConversationId == conversation.Id);
        Assert.Contains($"P2.5-{code}-2012", support.PanelIdentityStateJson);
        Assert.Contains(packageId.ToString(), support.PanelIdentityStateJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Etiketi P2.5", Assert.Single(fixture.WhatsApp.SentTexts));
        var snapshot = await db.AiReplyContextSnapshots.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstAsync(x => x.ConversationId == conversation.Id);
        Assert.Contains("PanelLabelOcr", snapshot.PanelIdentityContextSnapshot);
        Assert.DoesNotContain("StorageKey", snapshot.PanelIdentityContextSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".hex", fixture.WhatsApp.SentTexts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".rcvbp", fixture.WhatsApp.SentTexts.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PF4_Missing_pitch_uses_only_current_case_pitch_and_preserves_provenance()
    {
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf3-pitch-{Guid.NewGuid():N}", MessageType.Text, $"P2.5+{code}");
        fixture.WhatsApp.SentTexts.Clear();
        Configure(Observation(null, code, "2012", PanelLabelRecognitionStatus.Partial, true, false));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-mixed-{Guid.NewGuid():N}", MessageType.Image, null);
        var support = await fixture.LoadCaseAsync(conversation.Id);
        Assert.Contains($"P2.5-{code}-2012", support!.PanelIdentityStateJson);
        Assert.Contains("MixedContext", support.PanelIdentityStateJson);
        Assert.Contains("CustomerPattern", support.PanelIdentityStateJson);
        Assert.Contains("PanelLabelOcr", support.PanelIdentityStateJson);
    }

    [Fact]
    public async Task PF4_Text_photo_conflict_does_not_overwrite_authoritative_identity()
    {
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf3-full-{Guid.NewGuid():N}", MessageType.Text, $"P2.5+{code}+2012");
        var before = (await fixture.LoadCaseAsync(conversation.Id))!.PanelIdentityStateJson;
        fixture.WhatsApp.SentTexts.Clear();
        Configure(Observation("P2.5", code, "2013", PanelLabelRecognitionStatus.CompleteHighConfidence, true, false));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-conflict-{Guid.NewGuid():N}", MessageType.Image, null);
        Assert.Equal(before, (await fixture.LoadCaseAsync(conversation.Id))!.PanelIdentityStateJson);
        Assert.Contains("2012", Assert.Single(fixture.WhatsApp.SentTexts));
        Assert.Contains("2013", fixture.WhatsApp.SentTexts.Single());
    }

    [Theory]
    [InlineData(PanelLabelRecognitionStatus.Ambiguous)]
    [InlineData(PanelLabelRecognitionStatus.Unreadable)]
    [InlineData(PanelLabelRecognitionStatus.MultipleLabels)]
    [InlineData(PanelLabelRecognitionStatus.ProviderFailure)]
    public async Task PF4_Non_authoritative_observation_never_creates_identity(PanelLabelRecognitionStatus status)
    {
        Configure(Observation("P2.5", "1065S", null, status, true, status == PanelLabelRecognitionStatus.ProviderFailure));
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-safe-{Guid.NewGuid():N}", MessageType.Image, null);
        var support = await fixture.LoadCaseAsync(conversation.Id);
        Assert.True(string.IsNullOrWhiteSpace(support!.PanelIdentityStateJson));
    }

    [Fact]
    public async Task PF4_Same_image_analysis_is_cached()
    {
        Configure(Observation("P2.5", "1065S", "2012", PanelLabelRecognitionStatus.CompleteHighConfidence, true, false));
        var (conversation, customer) = await fixture.SeedConversationAsync();
        var external = $"pf4-cache-{Guid.NewGuid():N}";
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, external, MessageType.Image, null);
        var calls = fixture.Images.CallCount;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(x => x.ExternalMessageId == external);
        var analysis = scope.ServiceProvider.GetRequiredService<TechnicalImageAnalysisService>();
        await analysis.AnalyzeAsync([message.Id], null, default);
        Assert.Equal(calls, fixture.Images.CallCount);
    }

    [Fact]
    public async Task PF4_Wrong_photo_retraction_clears_photo_identity_and_stale_match()
    {
        Configure(Observation("P2.5", "1065S", "2012", PanelLabelRecognitionStatus.CompleteHighConfidence, true, false));
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-photo-{Guid.NewGuid():N}", MessageType.Image, null);
        Assert.False(string.IsNullOrWhiteSpace((await fixture.LoadCaseAsync(conversation.Id))!.PanelIdentityStateJson));
        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"pf4-retract-{Guid.NewGuid():N}", MessageType.Text, "Yanlış panelin fotoğrafını attım.");
        Assert.Null((await fixture.LoadCaseAsync(conversation.Id))!.PanelIdentityStateJson);
        Assert.Contains("geçersiz", Assert.Single(fixture.WhatsApp.SentTexts), StringComparison.OrdinalIgnoreCase);
    }

    private void Configure(PanelLabelIdentityObservation observation)
    {
        fixture.Images.Reset(); fixture.Planner.Reset(); fixture.Knowledge.Reset();
        fixture.Images.NextResult = new TechnicalImageAnalysisResult(TechnicalImageCategory.PanelLabel, observation.Evidence, null, [], null, null, null,
            [], [], [], .95, observation.Warnings, observation.VisionUsed ? ImageAnalysisSource.Vision : ImageAnalysisSource.Ocr,
            OcrAttempted: true, OcrSucceeded: observation.OcrUsed, VisionAttempted: observation.VisionUsed,
            VisionSucceeded: observation.VisionUsed, PanelIdentityObservation: observation);
    }

    private static PanelLabelIdentityObservation Observation(string? pitch, string? code, string? secondary,
        PanelLabelRecognitionStatus status, bool ocr, bool vision) =>
        new(pitch, code, secondary, status, pitch is null ? 0 : .95, code is null ? 0 : .95, secondary is null ? 0 : .95,
            string.Join(' ', new[] { pitch, code, secondary }.Where(x => x is not null)), [], ocr, vision);
}
