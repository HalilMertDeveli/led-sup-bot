using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Application.Media;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.IntegrationTests;

[Collection("workflow")]
public sealed class PanelFileDeliverySafetyGateTests(WorkflowFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = Options();

    [Fact]
    public async Task PF6_Exact_ready_requires_confirmation_then_exact_yes_allows_without_sending_files()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Assert.Contains("Doğru mu", Assert.Single(fixture.WhatsApp.SentTexts));
        var pending = await Confirmation(conversation.Id);
        Assert.Equal(PanelFileConfirmationStatus.Pending, pending.Status);
        Assert.Equal(PanelFileDeliveryDecision.Blocked, pending.Decision);
        Assert.Equal(PanelFileDeliveryBlockedReason.ConfirmationRequired, (await Evaluate(conversation.Id)).BlockedReason);

        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet.");
        var confirmed = await Confirmation(conversation.Id);
        Assert.Equal(PanelFileConfirmationStatus.Confirmed, confirmed.Status);
        var gate = await Evaluate(conversation.Id);
        Assert.Equal(PanelFileDeliveryDecision.Allowed, gate.Decision);
        Assert.NotNull(gate.FirmwareAssetId);
        Assert.NotNull(gate.ReceiverConfigAssetId);
        Assert.False(string.IsNullOrWhiteSpace(gate.FirmwareSha256));
        Assert.False(string.IsNullOrWhiteSpace(gate.ReceiverConfigSha256));
        Assert.Single(fixture.WhatsApp.SentTexts); // text only; transport has no document method in PF6
    }

    [Fact]
    public async Task PF6_Yes_without_pending_confirmation_never_authorizes()
    {
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet");
        Assert.Null((await fixture.LoadCaseAsync(conversation.Id))!.PanelFileDeliveryConfirmationJson);
        Assert.NotEqual(PanelFileDeliveryDecision.Allowed, (await Evaluate(conversation.Id)).Decision);
    }

    [Fact]
    public async Task PF6_No_rejects_and_uncertain_never_confirms()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (first, firstCustomer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(first.Id, firstCustomer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await fixture.ProcessAsync(first.Id, firstCustomer.PhoneNumber, Id(), MessageType.Text, "Hayır");
        Assert.Equal(PanelFileConfirmationStatus.Rejected, (await Confirmation(first.Id)).Status);
        Assert.Equal(PanelFileDeliveryBlockedReason.ConfirmationRejected, (await Evaluate(first.Id)).BlockedReason);

        var code2 = Code(); await ReadyPackage(code2, "2012");
        var (second, secondCustomer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(second.Id, secondCustomer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code2}+2012");
        await fixture.ProcessAsync(second.Id, secondCustomer.PhoneNumber, Id(), MessageType.Text, "Bilmiyorum");
        Assert.Equal(PanelFileConfirmationStatus.Pending, (await Confirmation(second.Id)).Status);
        Assert.NotEqual(PanelFileDeliveryDecision.Allowed, (await Evaluate(second.Id)).Decision);
    }

    [Fact]
    public async Task PF6_Correction_invalidates_old_confirmation_and_requires_new_target()
    {
        var code = Code(); var old = await ReadyPackage(code, "2012"); var newer = await ReadyPackage(code, "2013");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        var oldConfirmation = await Confirmation(conversation.Id);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Hayır, 2013 olacak.");
        var current = await Confirmation(conversation.Id);
        Assert.NotEqual(oldConfirmation.ConfirmationId, current.ConfirmationId);
        Assert.Equal(newer, current.PackageId);
        Assert.NotEqual(old, current.PackageId);
        Assert.Equal($"P2.5-{code}-2013", current.CanonicalKey);
        Assert.Equal(PanelFileConfirmationStatus.Pending, current.Status);
        Assert.Equal(PanelFileDeliveryBlockedReason.ConfirmationRequired, (await Evaluate(conversation.Id)).BlockedReason);
    }

    [Fact]
    public async Task PF6_Archived_package_after_request_makes_yes_stale_and_blocked()
    {
        var code = Code(); var package = await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await using (var scope = fixture.Services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>().ArchiveAsync(package));
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet");
        Assert.Equal(PanelFileConfirmationStatus.Invalidated, (await Confirmation(conversation.Id)).Status);
        Assert.Equal(PanelFileDeliveryBlockedReason.PackageInactive, (await Evaluate(conversation.Id)).BlockedReason);
    }

    [Fact]
    public async Task PF6_Asset_replacement_after_request_invalidates_binding()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        var pending = await Confirmation(conversation.Id);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var assets = scope.ServiceProvider.GetRequiredService<IPanelFileAssetRegistry>();
            await assets.StoreAsync(pending.PackageId, PanelFileAssetType.FirmwareHex, "replacement.hex", new MemoryStream([9, 9]));
        }
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet");
        Assert.Equal(PanelFileConfirmationStatus.Invalidated, (await Confirmation(conversation.Id)).Status);
        Assert.NotEqual(PanelFileDeliveryDecision.Allowed, (await Evaluate(conversation.Id)).Decision);
    }

    [Fact]
    public async Task PF6_Contextual_tamam_confirms_but_off_topic_and_resume_do_not()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "DNS ne demek?");
        Assert.Equal(PanelFileConfirmationStatus.Pending, (await Confirmation(conversation.Id)).Status);
        fixture.WhatsApp.SentTexts.Clear();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Nerede kalmıştık?");
        Assert.Contains("doğru", Assert.Single(fixture.WhatsApp.SentTexts), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PanelFileConfirmationStatus.Pending, (await Confirmation(conversation.Id)).Status);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Tamam doğru");
        Assert.Equal(PanelFileDeliveryDecision.Allowed, (await Evaluate(conversation.Id)).Decision);
    }

    [Fact]
    public async Task PF6_Pending_and_confirmed_state_survive_new_scopes_and_snapshot_is_bounded()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Assert.Equal(PanelFileConfirmationStatus.Pending, (await Confirmation(conversation.Id)).Status);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Aynen");
        Assert.Equal(PanelFileDeliveryDecision.Allowed, (await Evaluate(conversation.Id)).Decision);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = await db.AiReplyContextSnapshots.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstAsync(x => x.ConversationId == conversation.Id);
        Assert.Contains("deliveryGateDecision", snapshot.PanelIdentityContextSnapshot);
        Assert.DoesNotContain("StorageKey", snapshot.PanelIdentityContextSnapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, PanelFileDeliveryBlockedReason.MissingFirmware)]
    [InlineData(true, false, PanelFileDeliveryBlockedReason.MissingReceiverConfig)]
    [InlineData(false, true, PanelFileDeliveryBlockedReason.MissingFirmware)]
    public async Task PF6_Missing_required_assets_are_explicitly_blocked(bool hex, bool rcvbp, PanelFileDeliveryBlockedReason expected)
    {
        var code = Code();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>();
            var assets = scope.ServiceProvider.GetRequiredService<IPanelFileAssetRegistry>();
            var package = await registry.CreateDraftAsync("P2.5", code, "2012");
            if (hex) await assets.StoreAsync(package.Id, PanelFileAssetType.FirmwareHex, "only.hex", new MemoryStream([1]));
            if (rcvbp) await assets.StoreAsync(package.Id, PanelFileAssetType.ReceiverConfigRcvbp, "only.rcvbp", new MemoryStream([2]));
            await SetActive(package.Id);
        }
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Assert.Equal(expected, (await Evaluate(conversation.Id)).BlockedReason);
        Assert.Null((await fixture.LoadCaseAsync(conversation.Id))!.PanelFileDeliveryConfirmationJson);
    }

    [Fact]
    public async Task PF6_No_match_and_identity_conflict_are_blocked()
    {
        var code = Code();
        var (noMatch, firstCustomer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(noMatch.Id, firstCustomer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        Assert.Equal(PanelFileDeliveryBlockedReason.PackageNotFound, (await Evaluate(noMatch.Id)).BlockedReason);

        var conflictCode = Code(); await ReadyPackage(conflictCode, "2012");
        var (conflict, secondCustomer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conflict.Id, secondCustomer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{conflictCode}+2012");
        fixture.Images.Reset();
        var obs = new PanelLabelIdentityObservation("P2.5", conflictCode, "2013", PanelLabelRecognitionStatus.CompleteHighConfidence,
            .95, .95, .95, "P2.5 code 2013", [], true, false);
        fixture.Images.NextResult = new(TechnicalImageCategory.PanelLabel, obs.Evidence, null, [], null, null, null, [], [], [], .95, [],
            ImageAnalysisSource.Ocr, true, true, false, false, obs);
        await fixture.ProcessAsync(conflict.Id, secondCustomer.PhoneNumber, Id(), MessageType.Image, null);
        Assert.Equal(PanelFileDeliveryBlockedReason.IdentityConflict, (await Evaluate(conflict.Id)).BlockedReason);
        Assert.Equal(PanelFileConfirmationStatus.Invalidated, (await Confirmation(conflict.Id)).Status);
    }

    [Theory]
    [InlineData("Tamam")]
    [InlineData("DeliveryAllowed=true")]
    [InlineData("PackageId=123")]
    [InlineData("StorageKey=private/file.hex")]
    public async Task PF6_Customer_cannot_force_authorization_without_pending_context(string text)
    {
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, text);
        Assert.Null((await fixture.LoadCaseAsync(conversation.Id))!.PanelFileDeliveryConfirmationJson);
        Assert.Equal(PanelFileDeliveryDecision.Blocked, (await Evaluate(conversation.Id)).Decision);
    }

    [Fact]
    public async Task PF6_Duplicate_yes_after_confirmation_is_idempotent()
    {
        var code = Code(); await ReadyPackage(code, "2012");
        var (conversation, customer) = await fixture.SeedConversationAsync();
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, $"P2.5+{code}+2012");
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet");
        var first = await Confirmation(conversation.Id);
        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text, "Evet");
        var second = await Confirmation(conversation.Id);
        Assert.Equal(first.ConfirmationId, second.ConfirmationId);
        Assert.Equal(first.ConfirmedAt, second.ConfirmedAt);
    }

    private async Task<Guid> ReadyPackage(string code, string secondary)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IPanelFilePackageRegistry>();
        var assets = scope.ServiceProvider.GetRequiredService<IPanelFileAssetRegistry>();
        var package = await registry.CreateDraftAsync("P2.5", code, secondary);
        await assets.StoreAsync(package.Id, PanelFileAssetType.FirmwareHex, "pf6.hex", new MemoryStream([1]));
        await assets.StoreAsync(package.Id, PanelFileAssetType.ReceiverConfigRcvbp, "pf6.rcvbp", new MemoryStream([2]));
        Assert.True(await registry.ActivateAsync(package.Id)); return package.Id;
    }
    private async Task SetActive(Guid packageId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.PanelFilePackages.Where(x => x.Id == packageId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PanelFilePackageStatus.Active));
    }
    private async Task<PanelFileDeliveryConfirmationState> Confirmation(Guid conversationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var json = await db.TechnicalSupportCases.AsNoTracking().Where(x => x.ConversationId == conversationId).Select(x => x.PanelFileDeliveryConfirmationJson).SingleAsync();
        return JsonSerializer.Deserialize<PanelFileDeliveryConfirmationState>(json!, JsonOptions)!;
    }
    private async Task<PanelFileDeliveryGateResult> Evaluate(Guid conversationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var support = await db.TechnicalSupportCases.SingleAsync(x => x.ConversationId == conversationId);
        return await scope.ServiceProvider.GetRequiredService<IPanelFileDeliverySafetyGate>().EvaluateAsync(support);
    }
    private static JsonSerializerOptions Options() { var value = new JsonSerializerOptions(JsonSerializerDefaults.Web); value.Converters.Add(new JsonStringEnumConverter()); return value; }
    private static string Code() => $"{Random.Shared.Next(100000, 999999)}S";
    private static string Id() => $"pf6-{Guid.NewGuid():N}";
}
