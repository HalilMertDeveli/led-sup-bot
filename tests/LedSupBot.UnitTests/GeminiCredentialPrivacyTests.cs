using System.Net;
using System.Text;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Infrastructure.OpenAi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class GeminiCredentialPrivacyTests
{
    private const string ApiKey = "test-api-key-must-not-appear-in-uri";

    [Fact]
    public async Task Structured_request_sends_api_key_in_header_only()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"{\"intent\":\"unclear\"}"}]}}]}""");
        var client = CreateStructuredClient(handler);

        var result = await client.TryGenerateJsonAsync(
            "credential_transport",
            "system",
            "user",
            new { type = "object" },
            0,
            CancellationToken.None);

        Assert.NotNull(result);
        AssertCredentialIsHeaderOnly(handler);
    }

    [Fact]
    public async Task Web_grounding_request_sends_api_key_in_header_only()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"answer"}]},"groundingMetadata":{"groundingChunks":[{"web":{"uri":"https://example.test/source","title":"source"}}]}}]}""");
        var client = CreateStructuredClient(handler);

        var result = await client.TryGenerateWebGroundedAsync("public question", CancellationToken.None);

        Assert.True(result.Grounded);
        AssertCredentialIsHeaderOnly(handler);
    }

    [Fact]
    public async Task Vision_request_sends_api_key_in_header_only()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var analyzer = new GeminiTechnicalImageAnalyzer(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") },
            new InMemoryStorage([0x00]),
            Options.Create(CreateOptions()),
            NullLogger<GeminiTechnicalImageAnalyzer>.Instance);

        _ = await analyzer.AnalyzeAsync("cases/test/image.jpg", "image/jpeg", "kablo", CancellationToken.None);

        AssertCredentialIsHeaderOnly(handler);
    }

    [Fact]
    public async Task Provider_error_body_is_not_written_to_logs()
    {
        const string privateMarker = "PROVIDER_PRIVATE_RESPONSE_MARKER";
        var handler = new CapturingHandler(HttpStatusCode.BadRequest, privateMarker);
        var logger = new CapturingLogger<GeminiStructuredClient>();
        var client = new GeminiStructuredClient(
            new HttpClient(handler),
            Options.Create(CreateOptions()),
            logger);

        var result = await client.TryGenerateJsonAsync(
            "privacy",
            "system",
            "user",
            new { type = "object" },
            0,
            CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(privateMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Malformed_extraction_payloads_are_not_written_to_logs()
    {
        const string privateMarker = "CUSTOMER_PRIVATE_MODEL_OUTPUT_MARKER";
        var geminiHandler = new CapturingHandler(HttpStatusCode.OK,
            $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{privateMarker}\"}}]}}}}]}}");
        var geminiClient = CreateStructuredClient(geminiHandler);
        var geminiLogger = new CapturingLogger<GeminiExtractionService>();
        var gemini = new GeminiExtractionService(geminiClient, geminiLogger);

        _ = await gemini.ExtractAsync(
            new AiExtractionRequest("customer input", null, new Dictionary<string, string?>()),
            CancellationToken.None);

        var openAiHandler = new CapturingHandler(HttpStatusCode.OK,
            $"{{\"choices\":[{{\"message\":{{\"content\":\"{privateMarker}\"}}}}]}}");
        var openAiLogger = new CapturingLogger<OpenAiExtractionService>();
        var openAi = new OpenAiExtractionService(
            new HttpClient(openAiHandler) { BaseAddress = new Uri("https://example.invalid/") },
            Options.Create(new OpenAiOptions { ApiKey = "test-only", BaseUrl = "https://example.invalid/" }),
            openAiLogger);

        _ = await openAi.ExtractAsync(
            new AiExtractionRequest("customer input", null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.DoesNotContain(geminiLogger.Messages, message => message.Contains(privateMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(openAiLogger.Messages, message => message.Contains(privateMarker, StringComparison.Ordinal));
    }

    private static GeminiStructuredClient CreateStructuredClient(CapturingHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(CreateOptions()),
            NullLogger<GeminiStructuredClient>.Instance);

    private static GeminiOptions CreateOptions() => new()
    {
        ApiKey = ApiKey,
        Model = "test-model",
        BaseUrl = "https://example.invalid/",
        TimeoutSeconds = 5,
    };

    private static void AssertCredentialIsHeaderOnly(CapturingHandler handler)
    {
        Assert.NotNull(handler.RequestUri);
        Assert.DoesNotContain(ApiKey, handler.RequestUri!.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("key=", handler.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ApiKey, handler.ApiKeyHeader);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.SingleOrDefault()
                : null;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class InMemoryStorage(byte[] content) : IMediaStorage
    {
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(content, writable: false));

        public Task<StoredMediaReference> StoreAsync(
            Stream stream,
            string contentType,
            MediaStoreRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool Exists(string storageKey) => true;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
