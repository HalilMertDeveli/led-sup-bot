using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

[Collection("whatsapp-webhook")]
public sealed class TakeoverWebhookTests
{
    private readonly WhatsAppWebhookFixture _fixture;

    public TakeoverWebhookTests(WhatsAppWebhookFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Internal_Takeover_Endpoint_Requires_Auth()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsync($"/internal/conversations/{Guid.NewGuid()}/takeover", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Internal_Takeover_Pause_Resume_Endpoints_Work()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = "905551000099" };
        db.Customers.Add(customer);
        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            Status = ConversationStatus.Active,
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskScreenSize,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var takeover = await client.PostAsync($"/internal/conversations/{conversation.Id}/takeover", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, takeover.StatusCode);

        var pause = await client.PostAsync($"/internal/conversations/{conversation.Id}/pause", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, pause.StatusCode);

        var resume = await client.PostAsync($"/internal/conversations/{conversation.Id}/resume-bot", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, resume.StatusCode);

        var updated = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(BotMode.Bot, updated.BotMode);
    }
}
