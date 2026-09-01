using LedSupBot.Application.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace LedSupBot.Infrastructure.Media;

public sealed class MediaImageValidator : IMediaImageValidator
{
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    public MediaImageValidationResult Validate(ReadOnlySpan<byte> content, string? declaredMimeType)
    {
        if (content.IsEmpty)
        {
            return new MediaImageValidationResult(false, null, null, null, "empty_content");
        }

        try
        {
            using var image = Image.Load(new MemoryStream(content.ToArray()));
            var format = image.Metadata.DecodedImageFormat;
            var detected = format?.DefaultMimeType;
            if (string.IsNullOrWhiteSpace(detected) || !SupportedMimeTypes.Contains(detected))
            {
                return new MediaImageValidationResult(false, detected, null, null, "unsupported_mime_type");
            }

            if (!string.IsNullOrWhiteSpace(declaredMimeType) &&
                !string.Equals(NormalizeMime(declaredMimeType), detected, StringComparison.OrdinalIgnoreCase) &&
                !IsCompatibleMime(declaredMimeType, detected))
            {
                return new MediaImageValidationResult(false, detected, null, null, "mime_type_mismatch");
            }

            if (image.Width <= 0 || image.Height <= 0)
            {
                return new MediaImageValidationResult(false, detected, null, null, "invalid_image_data");
            }

            return new MediaImageValidationResult(true, detected, image.Width, image.Height, null);
        }
        catch (UnknownImageFormatException)
        {
            return new MediaImageValidationResult(false, null, null, null, "unrecognized_image_format");
        }
        catch (InvalidImageContentException)
        {
            return new MediaImageValidationResult(false, null, null, null, "invalid_image_data");
        }
        catch
        {
            return new MediaImageValidationResult(false, null, null, null, "invalid_image_data");
        }
    }

    private static bool IsCompatibleMime(string declared, string detected)
    {
        var normalizedDeclared = NormalizeMime(declared);
        return string.Equals(normalizedDeclared, detected, StringComparison.OrdinalIgnoreCase) ||
               (normalizedDeclared == "image/jpg" && detected == "image/jpeg");
    }

    private static string NormalizeMime(string mime) =>
        mime.Split(';', 2)[0].Trim().ToLowerInvariant();
}
