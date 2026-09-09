using LedSupBot.Infrastructure.Media;

namespace LedSupBot.UnitTests.Media;

public sealed class MediaImageValidatorTests
{
    private readonly MediaImageValidator _validator = new();

    [Fact]
    public void Validates_Jpeg()
    {
        var result = _validator.Validate(TestImageBytes.CreateJpeg(), "image/jpeg");
        Assert.True(result.IsValid);
        Assert.Equal("image/jpeg", result.NormalizedContentType);
    }

    [Fact]
    public void Validates_Png()
    {
        var result = _validator.Validate(TestImageBytes.CreatePng(), "image/png");
        Assert.True(result.IsValid);
        Assert.Equal("image/png", result.NormalizedContentType);
    }

    [Fact]
    public void Validates_Webp()
    {
        var result = _validator.Validate(TestImageBytes.CreateWebp(), "image/webp");
        Assert.True(result.IsValid);
        Assert.Equal("image/webp", result.NormalizedContentType);
    }

    [Fact]
    public void Rejects_Unsupported_Mime()
    {
        var result = _validator.Validate(TestImageBytes.CreatePng(), "image/gif");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_Corrupt_Image()
    {
        var result = _validator.Validate(TestImageBytes.CorruptJpegHeader(), "image/jpeg");
        Assert.False(result.IsValid);
        Assert.True(result.FailureReason is "invalid_image_data" or "unrecognized_image_format");
    }

    [Fact]
    public void Rejects_Empty_Content()
    {
        var result = _validator.Validate([], "image/jpeg");
        Assert.False(result.IsValid);
        Assert.Equal("empty_content", result.FailureReason);
    }

    [Fact]
    public void Rejects_Fake_Jpeg_Bytes()
    {
        var fake = "not-an-image"u8.ToArray();
        var result = _validator.Validate(fake, "image/jpeg");
        Assert.False(result.IsValid);
    }
}
