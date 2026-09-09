using LedSupBot.Application.Audio;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class IntelligenceHardeningTests
{
    [Theory]
    [InlineData("10 adet E80 var")]
    [InlineData("10 tane e80")]
    [InlineData("E80 10 tane")]
    [InlineData("e80'den 10 tane var")]
    [InlineData("10 E80 kullanıyoruz")]
    [InlineData("toplam 10 tane E80 receiver var")]
    [InlineData("receiver 10 tane, model E80")]
    [InlineData("10 tane kart var hepsi E80")]
    public void Receiver_count_and_model_are_independently_extracted(string text)
    {
        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(10, count);
        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E80, model);
    }

    [Theory]
    [InlineData("320x160")]
    [InlineData("HDMI 2")]
    [InlineData("AnyDesk 123456789")]
    [InlineData("P2.5+1065S+2012")]
    public void Unrelated_numbers_are_not_receiver_counts(string text) =>
        Assert.False(ReceiverCountParser.TryParse(text, out _));

    [Fact]
    public void Turkey_cutoff_is_exact()
    {
        var policy = new TurkeyRemoteSupportAvailabilityPolicy();
        Assert.True(policy.IsAvailable(new DateTimeOffset(2026, 9, 7, 15, 29, 59, TimeSpan.Zero)));
        Assert.False(policy.IsAvailable(new DateTimeOffset(2026, 9, 7, 15, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Voice_is_disabled_and_network_free_by_default()
    {
        var options = new VoiceOptions();
        Assert.False(options.InboundEnabled);
        Assert.False(options.OutboundEnabled);
        Assert.Equal("tr-TR", options.LanguageCode);
        var tts = await new UnavailableTextToSpeechService().SynthesizeAsync("Son müşteri metni", "tr-TR");
        Assert.False(tts.Succeeded);
        Assert.Null(tts.Audio);
    }
}
