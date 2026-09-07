using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.PanelFiles;

namespace LedSupBot.UnitTests;

public sealed class PanelFileIdentityTests
{
    private readonly PanelIdentityNormalizer _normalizer = new();

    [Theory]
    [InlineData("P2.5+1065S+2012")]
    [InlineData("p2.5 + 1065s + 2012")]
    [InlineData("P2.5-1065S-2012")]
    [InlineData("P2.5_1065S_2012")]
    [InlineData("P2.5 / 1065S / 2012")]
    [InlineData("P2.5 1065S 2012")]
    public void Safe_variations_have_one_canonical_identity(string input)
    {
        var result = _normalizer.NormalizeCanonicalInput(input);
        Assert.Equal(PanelIdentityNormalizationStatus.Valid, result.Status);
        Assert.Equal("P2.5-1065S-2012", result.Identity!.CanonicalKey);
    }

    [Fact]
    public void Structured_identity_generates_expected_key()
    {
        var result = _normalizer.Normalize("P2.5", "1065S", "2012");
        Assert.Equal(new PanelIdentity("P2.5", "1065S", "2012"), result.Identity);
        Assert.Equal("P2.5-1065S-2012", result.Identity!.CanonicalKey);
    }

    [Theory]
    [InlineData(null, "1065S", "2012", PanelIdentityNormalizationStatus.MissingInformation)]
    [InlineData("P2.x", "1065S", "2012", PanelIdentityNormalizationStatus.Malformed)]
    [InlineData("P2.5", "1065-", "2012", PanelIdentityNormalizationStatus.Malformed)]
    public void Missing_or_malformed_values_are_rejected(string? pitch, string code, string secondary,
        PanelIdentityNormalizationStatus expected) =>
        Assert.Equal(expected, _normalizer.Normalize(pitch, code, secondary).Status);

    [Theory]
    [InlineData("firmware.hex", true, PanelFileAssetType.FirmwareHex)]
    [InlineData("FIRMWARE.HEX", true, PanelFileAssetType.FirmwareHex)]
    [InlineData("receiver.rcvbp", true, PanelFileAssetType.ReceiverConfigRcvbp)]
    [InlineData("wrong.exe", false, PanelFileAssetType.FirmwareHex)]
    [InlineData("scan.txt", false, PanelFileAssetType.FirmwareHex)]
    [InlineData("payload.exe.hex", false, PanelFileAssetType.FirmwareHex)]
    [InlineData("config.rcvbp.hex", false, PanelFileAssetType.FirmwareHex)]
    [InlineData("../firmware.hex", false, PanelFileAssetType.FirmwareHex)]
    [InlineData("..\\firmware.hex", false, PanelFileAssetType.FirmwareHex)]
    public void Asset_extensions_are_strict(string file, bool valid, PanelFileAssetType expected)
    {
        Assert.Equal(valid, PanelFileAssetPolicy.TryClassify(file, out var actual));
        if (valid) Assert.Equal(expected, actual);
    }
}
