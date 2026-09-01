namespace LedSupBot.Application.Media;

public interface IMediaImageValidator
{
    MediaImageValidationResult Validate(ReadOnlySpan<byte> content, string? declaredMimeType);
}

public sealed record MediaImageValidationResult(
    bool IsValid,
    string? NormalizedContentType,
    int? WidthPixels,
    int? HeightPixels,
    string? FailureReason);
