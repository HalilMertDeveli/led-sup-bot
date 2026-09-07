using System.Net;
using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.IntegrationTests;

[Collection("whatsapp-webhook")]
public sealed class AdminApiTests
{
    private readonly WhatsAppWebhookFixture _fixture;
    private const string AdminPassword = "test-admin-password";

    public AdminApiTests(WhatsAppWebhookFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Admin_Conversations_Requires_Auth()
    {
        var client = _fixture.CreateAdminClient();
        var response = await client.GetAsync("/api/admin/conversations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_SignalR_Negotiate_Requires_Valid_Admin_Session()
    {
        using var anonymous = _fixture.CreateAdminClient();
        var denied = await anonymous.PostAsync("/hubs/admin/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var authenticated = await CreateAuthenticatedClientAsync();
        var allowed = await authenticated.PostAsync("/hubs/admin/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Admin_Login_And_List_Conversations()
    {
        var client = await CreateAuthenticatedClientAsync();

        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = "905551234567", DisplayName = "Test Müşteri" };
        db.Customers.Add(customer);
        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            Status = ConversationStatus.Active,
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskScreenSize,
        };
        db.Conversations.Add(conversation);
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "Merhaba",
            SentBy = MessageSender.Customer,
            DeliveryStatus = DeliveryStatus.Delivered,
        });
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/admin/conversations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AdminListResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Items);
        Assert.Contains(body.Items, i => i.Id == conversation.Id);
    }

    [Fact]
    public async Task Admin_Messages_Show_SentBy_Without_Connector_Secret()
    {
        var client = await CreateAuthenticatedClientAsync();

        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = "905559876543" };
        db.Customers.Add(customer);
        var conversation = new Conversation { CustomerId = customer.Id, BotMode = BotMode.Human, Status = ConversationStatus.InHumanSupport };
        db.Conversations.Add(conversation);
        db.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Inbound,
                MessageType = MessageType.Text,
                Content = "Müşteri mesajı",
                SentBy = MessageSender.Customer,
            },
            new Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Text,
                Content = "Bot cevabı",
                SentBy = MessageSender.Bot,
            },
            new Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Text,
                Content = "Teknisyen cevabı",
                SentBy = MessageSender.Technician,
            });
        await db.SaveChangesAsync();

        var response = await client.GetAsync($"/api/admin/conversations/{conversation.Id}/messages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(client.DefaultRequestHeaders.Contains("X-Connector-Secret"));
    }

    [Fact]
    public async Task Admin_Takeover_Pause_Resume_Work()
    {
        var client = await CreateAuthenticatedClientAsync();

        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = "905551112233" };
        db.Customers.Add(customer);
        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            BotMode = BotMode.Bot,
            Status = ConversationStatus.Active,
            CurrentStage = ConversationStage.AskScreenSize,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var takeover = await client.PostAsync($"/api/admin/conversations/{conversation.Id}/takeover", null);
        Assert.Equal(HttpStatusCode.OK, takeover.StatusCode);

        var pause = await client.PostAsync($"/api/admin/conversations/{conversation.Id}/pause", null);
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

        var resume = await client.PostAsync($"/api/admin/conversations/{conversation.Id}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);

        var updated = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(BotMode.Bot, updated.BotMode);
    }

    [Fact]
    public async Task Admin_Send_Message_Persists_Technician_And_Sets_Human()
    {
        var client = await CreateAuthenticatedClientAsync();

        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = "905553334455" };
        db.Customers.Add(customer);
        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            BotMode = BotMode.Bot,
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/admin/conversations/{conversation.Id}/messages",
            new { text = "Teknisyen mesajı" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyDb = _fixture.CreateDb();
        var updatedConversation = await verifyDb.Conversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(BotMode.Human, updatedConversation.BotMode);
        Assert.Equal(ConversationStatus.InHumanSupport, updatedConversation.Status);
        Assert.Equal(ConversationStage.HumanHandoff, updatedConversation.CurrentStage);

        var message = await verifyDb.Messages
            .AsNoTracking()
            .SingleAsync(m => m.ConversationId == conversation.Id);
        Assert.Equal(MessageSender.Technician, message.SentBy);
        Assert.Equal(MessageDirection.Outbound, message.Direction);
        Assert.Equal("Teknisyen mesajı", message.Content);
    }

    [Fact]
    public async Task Admin_Login_Fails_With_Wrong_Password()
    {
        var client = _fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _fixture.CreateAdminClient();
        var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { password = AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private sealed record AdminListResponse(List<AdminListItem> Items, int Total);
    private sealed record AdminListItem(Guid Id, string CustomerIdentifier, string BotMode);
}
