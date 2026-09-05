using System.Text.Json;
using LedSupBot.Application.Media;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.IntegrationTests;

[Collection("workflow")]
public sealed class PanelIdentityCoordinatorTests(WorkflowFixture fixture)
{
    [Fact]
    public async Task PF5_Agreement_converges_to_one_durable_identity()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Configure(Obs("P2.5", code, "2012"));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        var state = await State(conversation.Id);
        Assert.Equal(PanelIdentityResolutionState.PackageNoMatch, state.ResolutionState);
        Assert.Equal($"P2.5-{code}-2012", state.CanonicalKey);
        Assert.Empty(state.Conflicts!);
        Assert.Single((await Cases(conversation.Id)));
    }

    [Fact]
    public async Task PF5_Conflict_is_durable_and_blocks_old_package()
    {
        var code = Code();
        var packageId = await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Assert.Equal(packageId, (await State(conversation.Id)).PackageId);
        Configure(Obs("P2.5", code, "2013"));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        var state = await State(conversation.Id);
        Assert.Equal(PanelIdentityResolutionState.IdentityConflict, state.ResolutionState);
        Assert.Null(state.PackageId);
        Assert.Null(state.CanonicalKey);
        Assert.Equal(PanelIdentityField.SecondaryCode, Assert.Single(state.Conflicts!).Field);
    }

    [Fact]
    public async Task PF5_Customer_correction_resolves_conflict_and_never_restores_stale_match()
    {
        var code = Code();
        var oldPackage = await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Configure(Obs("P2.5", code, "2013"));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "2013 doğru.");
        var state = await State(conversation.Id);
        Assert.Equal($"P2.5-{code}-2013", state.CanonicalKey);
        Assert.Equal(PanelIdentityResolutionState.PackageNoMatch, state.ResolutionState);
        Assert.Null(state.PackageId);
        Assert.NotEqual(oldPackage, state.PackageId);
        Assert.Equal(PanelIdentitySource.CustomerCorrection, state.SecondaryCodeSource);
        Assert.Empty(state.Conflicts!);
    }

    [Fact]
    public async Task PF5_Partial_photo_then_isolated_pending_secondary_completes_safely()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        Configure(Obs("P2.5", code, null, PanelLabelRecognitionStatus.Partial));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "2012");
        var state = await State(conversation.Id);
        Assert.Equal($"P2.5-{code}-2012", state.CanonicalKey);
        Assert.Equal(PanelIdentitySource.PanelLabelOcr, state.PixelPitchSource);
        Assert.Equal(PanelIdentitySource.PanelLabelOcr, state.PanelCodeSource);
        Assert.Equal(PanelIdentitySource.CustomerNaturalText, state.SecondaryCodeSource);
    }

    [Fact]
    public async Task PF5_Multiple_field_conflict_preserves_each_field()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Configure(Obs("P3.91", code, "2013"));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        var conflicts = (await State(conversation.Id)).Conflicts!;
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, x => x.Field == PanelIdentityField.PixelPitch);
        Assert.Contains(conflicts, x => x.Field == PanelIdentityField.SecondaryCode);
    }

    [Fact]
    public async Task PF5_Wrong_photo_retraction_keeps_only_text_fields_and_invalidates_package()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}");
        Configure(Obs(null, code, "2012", PanelLabelRecognitionStatus.Partial));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Image, null);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Yanlış panelin fotoğrafını attım.");
        var state = await State(conversation.Id);
        Assert.Equal("P2.5", state.PixelPitch);
        Assert.Equal(code, state.PanelCode); // supplied by text before corroborating photo
        Assert.Null(state.SecondaryCode);
        Assert.Null(state.PackageId);
        Assert.Null(state.CanonicalKey);
    }

    [Fact]
    public async Task PF5_Text_retraction_does_not_guess_replacement()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Az önce yazdığım kod yanlıştı.");
        var state = await State(conversation.Id);
        Assert.Null(state.CanonicalKey);
        Assert.Null(state.PackageId);
        Assert.Null(state.PixelPitch);
        Assert.Null(state.PanelCode);
        Assert.Null(state.SecondaryCode);
    }

    [Fact]
    public async Task PF5_Package_readiness_is_refreshed_on_new_explicit_identity_evidence()
    {
        var code = Code();
        var packageId = await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>();
            Assert.True(await registry.ArchiveAsync(packageId));
        }
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        var state = await State(conversation.Id);
        Assert.Equal(PanelIdentityResolutionState.PackageInactive, state.ResolutionState);
        Assert.Null(state.PackageId);
    }

    [Fact]
    public async Task PF5_Resume_question_uses_durable_missing_field_state()
    {
        var code = Code();
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}");
        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Nerede kalmıştık?");
        Assert.Contains("Son kod", Assert.Single(fixture.WhatsApp.SentTexts));
        Assert.Null((await State(conversation.Id)).CanonicalKey);
    }

    private void Configure(PanelLabelIdentityObservation observation)
    {
        fixture.Images.Reset(); fixture.Planner.Reset(); fixture.Knowledge.Reset();
        fixture.Images.NextResult = new(TechnicalImageCategory.PanelLabel, observation.Evidence, null, [], null, null, null,
            [], [], [], .95, [], ImageAnalysisSource.Ocr, true, true, false, false, observation);
    }
    private static PanelLabelIdentityObservation Obs(string? pitch, string? code, string? secondary,
        PanelLabelRecognitionStatus status = PanelLabelRecognitionStatus.CompleteHighConfidence) =>
        new(pitch, code, secondary, status, pitch is null ? 0 : .95, code is null ? 0 : .95, secondary is null ? 0 : .95,
            string.Join(' ', new[] { pitch, code, secondary }.Where(x => x is not null)), [], true, false);
    private async Task<Guid> ReadyPackage(string code, string secondary)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>();
        var assets = scope.ServiceProvider.GetRequiredService<IPanelFileAssetRegistry>();
        var package = await registry.CreateDraftAsync("P2.5", code, secondary);
        await assets.StoreAsync(package.Id, PanelFileAssetType.FirmwareHex, "pf5.hex", new MemoryStream([1]));
        await assets.StoreAsync(package.Id, PanelFileAssetType.ReceiverConfigRcvbp, "pf5.rcvbp", new MemoryStream([2]));
        Assert.True(await registry.ActivateAsync(package.Id));
        return package.Id;
    }
    private async Task<PanelIdentityCaseState> State(Guid conversationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var json = await db.TechnicalSupportCases.AsNoTracking().Where(x => x.ConversationId == conversationId).Select(x => x.PanelIdentityStateJson).SingleAsync();
        return JsonSerializer.Deserialize<PanelIdentityCaseState>(json!, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
    }
    private async Task<List<Guid>> Cases(Guid conversationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.TechnicalSupportCases.AsNoTracking().Where(x => x.ConversationId == conversationId).Select(x => x.Id).ToListAsync();
    }
    private static string Code() => $"{Random.Shared.Next(100000, 999999)}S";
    private static string Id() => $"pf5-{Guid.NewGuid():N}";
}
