using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class RealtimeSignalRSyncTests
{
    [Fact]
    public void EventPayloads_SerializeAndDeserialize_Correctly()
    {
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();

        var convPayload = new ConversationEventPayload { ConversationId = convId };
        var convJson = JsonSerializer.Serialize(convPayload);
        var convDeserialized = JsonSerializer.Deserialize<ConversationEventPayload>(convJson);
        Assert.NotNull(convDeserialized);
        Assert.Equal(convId, convDeserialized!.ConversationId);

        var msgPayload = new MessageEventPayload { ConversationId = convId, MessageId = msgId };
        var msgJson = JsonSerializer.Serialize(msgPayload);
        var msgDeserialized = JsonSerializer.Deserialize<MessageEventPayload>(msgJson);
        Assert.NotNull(msgDeserialized);
        Assert.Equal(convId, msgDeserialized!.ConversationId);
        Assert.Equal(msgId, msgDeserialized.MessageId);

        var waPayload = new WhatsAppStatusEventPayload { Status = "Connected" };
        var waJson = JsonSerializer.Serialize(waPayload);
        var waDeserialized = JsonSerializer.Deserialize<WhatsAppStatusEventPayload>(waJson);
        Assert.NotNull(waDeserialized);
        Assert.Equal("Connected", waDeserialized!.Status);

        var opPayload = new OperatorStateEventPayload { ConversationId = convId, MessageId = msgId, Status = "Connected" };
        var opJson = JsonSerializer.Serialize(opPayload);
        var opDeserialized = JsonSerializer.Deserialize<OperatorStateEventPayload>(opJson);
        Assert.NotNull(opDeserialized);
        Assert.Equal(convId, opDeserialized!.ConversationId);
        Assert.Equal(msgId, opDeserialized.MessageId);
        Assert.Equal("Connected", opDeserialized.Status);

        var packageId = Guid.NewGuid();
        var packagePayload = new PanelPackagesChangedEventPayload { PackageId = packageId, Change = "deleted" };
        var packageJson = JsonSerializer.Serialize(packagePayload);
        var packageDeserialized = JsonSerializer.Deserialize<PanelPackagesChangedEventPayload>(packageJson);
        Assert.NotNull(packageDeserialized);
        Assert.Equal(packageId, packageDeserialized!.PackageId);
        Assert.Equal("deleted", packageDeserialized.Change);

        var anyDeskPayload = new AnyDeskAlertEvent
        {
            ConversationId = convId,
            CustomerDisplayName = "Halil",
            IssueSummary = "Modül arızası",
            ScreenSummary = "160×192 cm",
            ReceiverSummary = "2 Adet",
            PanelStatus = "✓ Alındı",
            ConfigStatus = "✓ Bağlandı",
            AnyDeskStatus = "✓ Alındı",
        };
        var anyDeskJson = JsonSerializer.Serialize(anyDeskPayload);
        var anyDeskDeserialized = JsonSerializer.Deserialize<AnyDeskAlertEvent>(anyDeskJson);
        Assert.NotNull(anyDeskDeserialized);
        Assert.Equal(convId, anyDeskDeserialized!.ConversationId);
        Assert.Equal("Halil", anyDeskDeserialized.CustomerDisplayName);
        Assert.Equal("160×192 cm", anyDeskDeserialized.ScreenSummary);
    }

    [Fact]
    public async Task NullAdminEventNotifier_ExecutesSafely()
    {
        IAdminEventNotifier notifier = new NullAdminEventNotifier();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();

        await notifier.NotifyConversationUpdatedAsync(convId);
        await notifier.NotifyMessageReceivedAsync(convId, msgId);
        await notifier.NotifyWhatsAppStatusChangedAsync("Connected");
        await notifier.NotifyOperatorStateChangedAsync(convId);
        await notifier.NotifyPanelPackagesChangedAsync(Guid.NewGuid(), "imported");
        await notifier.NotifyAnyDeskReadyForHandoffAsync(new AnyDeskReadyNotification(
            convId, "Test", "Summary", "Screen", "Receiver", "Panel", "Config", "AnyDesk"));

        Assert.True(true);
    }

    [Fact]
    public async Task InboundHandler_Notifies_On_ConnectionStateChanged()
    {
        var testNotifier = new RecordingAdminEventNotifier();
        var services = new ServiceCollection();
        services.AddSingleton<IAdminEventNotifier>(testNotifier);
        var sp = services.BuildServiceProvider();

        var stateStore = new TestConnectionStateStore();
        var selfIdentity = new WhatsAppSelfIdentityStore();
        var testModeOptions = Options.Create(new WhatsAppTestModeOptions());

        var handler = new WhatsAppInboundEventHandler(
            sp,
            stateStore,
            selfIdentity,
            testModeOptions,
            NullLogger<WhatsAppInboundEventHandler>.Instance);

        await handler.HandleAsync(new WhatsAppInboundEvent
        {
            EventType = "ConnectionStateChanged",
            Status = "CONNECTED",
        });

        Assert.Equal(WhatsAppConnectionStatus.Connected, stateStore.Current);
        Assert.Contains("Connected", testNotifier.WhatsAppStatuses);
    }

    private sealed class TestConnectionStateStore : IWhatsAppConnectionStateStore
    {
        public WhatsAppConnectionStatus Current { get; private set; } = WhatsAppConnectionStatus.Disconnected;
        public void Set(WhatsAppConnectionStatus status) => Current = status;
    }

    private sealed class RecordingAdminEventNotifier : IAdminEventNotifier
    {
        public List<Guid> UpdatedConversations { get; } = [];
        public List<(Guid ConversationId, Guid MessageId)> ReceivedMessages { get; } = [];
        public List<string> WhatsAppStatuses { get; } = [];
        public List<Guid?> OperatorStateChanges { get; } = [];

        public Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            UpdatedConversations.Add(conversationId);
            return Task.CompletedTask;
        }

        public Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Add((conversationId, messageId));
            return Task.CompletedTask;
        }

        public Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task NotifyWhatsAppStatusChangedAsync(string status, CancellationToken cancellationToken = default)
        {
            WhatsAppStatuses.Add(status);
            return Task.CompletedTask;
        }

        public Task NotifyOperatorStateChangedAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
        {
            OperatorStateChanges.Add(conversationId);
            return Task.CompletedTask;
        }

        public Task NotifyPanelPackagesChangedAsync(Guid packageId, string change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
