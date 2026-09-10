using System.Net;
using System.Text;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class GeminiCapacityPolicyTests
{
    [Fact]
    public async Task DailyQuota_IsClassified_AndFurtherCallsAreSuppressed()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"message\":\"Quota exceeded for GenerateRequestsPerDayPerProjectPerModel-FreeTier\"}}"));
        var (client, state) = Create(handler, maxRetries: 3);

        Assert.Null(await Generate(client));
        Assert.Null(await Generate(client));

        Assert.Equal(1, handler.Count);
        Assert.Equal(GeminiCapacityStatus.DailyQuotaExhausted, state.Current.Status);
        Assert.Equal(GeminiFailureKind.DailyQuota, state.Current.LastFailure);
        Assert.NotNull(state.Current.SuppressedUntil);
    }

    [Fact]
    public async Task Temporary429_HonorsBoundedRetry_ThenRecovers()
    {
        var limited = Response(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"rate limit\"}}");
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new SequenceHandler(limited, Success());
        var (client, state) = Create(handler, maxRetries: 1);

        Assert.NotNull(await Generate(client));

        Assert.Equal(2, handler.Count);
        Assert.Equal(GeminiCapacityStatus.Available, state.Current.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentFailures_AreNotRetried(HttpStatusCode statusCode)
    {
        var handler = new SequenceHandler(Response(statusCode, "{}"));
        var (client, state) = Create(handler, maxRetries: 3);

        Assert.Null(await Generate(client));

        Assert.Equal(1, handler.Count);
        Assert.Equal(GeminiCapacityStatus.Misconfigured, state.Current.Status);
    }

    [Fact]
    public async Task Provider5xx_IsRetriedWithinConfiguredBound()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.ServiceUnavailable, "{}"), Success());
        var (client, state) = Create(handler, maxRetries: 1);

        Assert.NotNull(await Generate(client));

        Assert.Equal(2, handler.Count);
        Assert.Equal(GeminiCapacityStatus.Available, state.Current.Status);
    }

    [Fact]
    public async Task Timeout_IsRetriedWithinConfiguredBound()
    {
        var handler = new SequenceHandler(
            _ => throw new TaskCanceledException("simulated timeout"),
            _ => Success());
        var (client, state) = Create(handler, maxRetries: 1);

        Assert.NotNull(await Generate(client));

        Assert.Equal(2, handler.Count);
        Assert.Equal(GeminiCapacityStatus.Available, state.Current.Status);
    }

    [Fact]
    public async Task NormalCapacity_UsesOneRequestWithoutRetries()
    {
        var handler = new SequenceHandler(Success());
        var (client, state) = Create(handler, maxRetries: 3, capacityMode: "Normal");

        Assert.NotNull(await Generate(client));

        Assert.Equal(1, handler.Count);
        Assert.Equal(1, state.Current.RequestsAttempted);
        Assert.Equal("Normal", state.Current.CapacityMode);
    }

    [Fact]
    public async Task FinalPlannerBudget_AllowsOnlyOneGeminiOperationPerTurn()
    {
        var handler = new SequenceHandler(Success());
        var (client, _) = Create(handler, maxRetries: 0);
        var planner = new GeminiConversationPlannerService(client,
            NullLogger<GeminiConversationPlannerService>.Instance);
        var context = new ConversationContext
        {
            CurrentStage = ConversationStage.Start,
            StageLabel = "Start",
            BotMode = BotMode.Bot
        };

        await planner.TryPlanAsync(context, "question");
        Assert.Null(await planner.TryPlanAsync(context, "question repeated"));

        Assert.Equal(1, handler.Count);
        Assert.Equal(1, context.RequestBudget.GeminiCalls);
    }

    private static Task<string?> Generate(GeminiStructuredClient client) => client.TryGenerateJsonAsync(
        "capacity_test", "system", "question", new { type = "OBJECT" }, 0.1, CancellationToken.None);

    private static (GeminiStructuredClient Client, GeminiProviderState State) Create(
        HttpMessageHandler handler, int maxRetries, string capacityMode = "Limited")
    {
        var configured = Options.Create(new GeminiOptions
        {
            ApiKey = "test-key",
            Model = "test-model",
            BaseUrl = "https://example.invalid",
            MaxRetries = maxRetries,
            MaxRetryDelaySeconds = 0,
            CapacityMode = capacityMode,
            DailyQuotaAware = true
        });
        var state = new GeminiProviderState(configured);
        var client = new GeminiStructuredClient(new HttpClient(handler), configured,
            NullLogger<GeminiStructuredClient>.Instance, state);
        return (client, state);
    }

    private static HttpResponseMessage Success() => Response(HttpStatusCode.OK,
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"intent\\\":\\\"unclear\\\"}\"}]}}]}");

    private static HttpResponseMessage Response(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;
        public int Count { get; private set; }

        public SequenceHandler(params HttpResponseMessage[] responses)
            : this(responses.Select<HttpResponseMessage, Func<HttpRequestMessage, HttpResponseMessage>>(response => _ => response).ToArray())
        {
        }

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) =>
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }
}
