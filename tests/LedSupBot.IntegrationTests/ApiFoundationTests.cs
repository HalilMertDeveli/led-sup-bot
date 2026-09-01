using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LedSupBot.IntegrationTests;

public class ApiFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiFoundationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Returns_Healthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload!.Status);
    }

    [Fact]
    public async Task Root_Declares_Integrations_Not_Implemented()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RootPayload>();
        Assert.NotNull(payload);
        Assert.Equal("baileys_connector_unofficial", payload!.WhatsAppIntegration);
        Assert.Equal("not_configured", payload.OpenAiIntegration);
        Assert.Equal("ef_core_postgresql", payload.DatabaseSchema);
    }

    private sealed class HealthPayload
    {
        public string Status { get; set; } = string.Empty;
    }

    private sealed class RootPayload
    {
        public string WhatsAppIntegration { get; set; } = string.Empty;
        public string OpenAiIntegration { get; set; } = string.Empty;
        public string DatabaseSchema { get; set; } = string.Empty;
    }
}
