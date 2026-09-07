namespace LedSupBot.Application.Audio;

public sealed class VoiceOptions
{
    public const string SectionName = "Voice";
    public bool InboundEnabled { get; set; }
    public bool OutboundEnabled { get; set; }
    public long MaxAudioSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int ProviderTimeoutSeconds { get; set; } = 30;
    public string LanguageCode { get; set; } = "tr-TR";
}

public sealed record SpeechTranscriptionResult(bool Succeeded, string? Transcript, string Provider);
public interface ISpeechToTextService
{
    Task<SpeechTranscriptionResult> TranscribeAsync(Stream audio, string mimeType, string languageCode, CancellationToken ct = default);
}
public sealed record SpeechSynthesisResult(bool Succeeded, Stream? Audio, string MimeType, string Provider);
public interface ITextToSpeechService
{
    Task<SpeechSynthesisResult> SynthesizeAsync(string finalCustomerText, string languageCode, CancellationToken ct = default);
}
public sealed class UnavailableSpeechToTextService : ISpeechToTextService
{
    public Task<SpeechTranscriptionResult> TranscribeAsync(Stream audio, string mimeType, string languageCode, CancellationToken ct = default) =>
        Task.FromResult(new SpeechTranscriptionResult(false, null, "unconfigured"));
}
public sealed class UnavailableTextToSpeechService : ITextToSpeechService
{
    public Task<SpeechSynthesisResult> SynthesizeAsync(string finalCustomerText, string languageCode, CancellationToken ct = default) =>
        Task.FromResult(new SpeechSynthesisResult(false, null, "audio/ogg; codecs=opus", "unconfigured"));
}
