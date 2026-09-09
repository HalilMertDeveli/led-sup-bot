using LedSupBot.Application.Workflow.Knowledge;

namespace LedSupBot.UnitTests;

public sealed class TechnicalRetrievalPolicyTests
{
    [Theory]
    [InlineData("tamam")]
    [InlineData("evet")]
    [InlineData("4")]
    [InlineData("devam")]
    [InlineData("teşekkürler")]
    [InlineData("4 tane E120")]
    public void Routine_Messages_Do_Not_Retrieve(string message) =>
        Assert.False(TechnicalRetrievalPolicy.ShouldRetrieve(message));

    [Theory]
    [InlineData("X20 maksimum kaç piksel destekliyor?")]
    [InlineData("X4e'de görüntü neden gidip geliyor?")]
    [InlineData("Ekranın yarısı neden gelmiyor?")]
    public void Technical_Questions_And_Symptoms_Retrieve(string message) =>
        Assert.True(TechnicalRetrievalPolicy.ShouldRetrieve(message));

    [Fact]
    public void Comparison_Is_Explicit_Not_Just_Multiple_Model_Tokens()
    {
        Assert.True(TechnicalRetrievalPolicy.IsComparison("X20 ile X20m arasındaki fark ne?"));
        Assert.False(TechnicalRetrievalPolicy.IsComparison("4 tane E120 var, X4e'de görüntü neden gidip geliyor?"));
    }

    [Fact]
    public void Unknown_Model_Exact_Claim_Requires_Grounding() =>
        Assert.True(TechnicalRetrievalPolicy.RequiresExactProductGrounding("Bu kart maksimum kaç piksel destekliyor?"));
}
