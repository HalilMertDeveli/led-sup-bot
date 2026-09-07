using LedSupBot.Application.Workflow;

namespace LedSupBot.UnitTests;

public sealed class CustomerResponseStylePolicyTests
{
    [Fact]
    public void Shared_policy_requires_answer_first_bounded_replies()
    {
        var policy = CustomerResponseStylePolicy.GenerationInstruction;
        Assert.Contains("Answer first", policy);
        Assert.Contains("1-2 short sentences", policy);
        Assert.Contains("one action/check at a time", policy);
        Assert.Contains("Never claim an action", policy);
    }

    [Theory]
    [InlineData("detaylı anlat")]
    [InlineData("tüm özellikleri yaz")]
    [InlineData("madde madde anlat")]
    public void Explicit_detail_requests_are_detected(string text) =>
        Assert.True(CustomerResponseStylePolicy.RequestsDetail(text));

    [Theory]
    [InlineData("Foto atsam olur mu?")]
    [InlineData("Bekle bakıyorum")]
    [InlineData("DNS ne?")]
    public void Ordinary_messages_do_not_enable_long_form(string text) =>
        Assert.False(CustomerResponseStylePolicy.RequestsDetail(text));

    [Theory]
    [InlineData("Elbette")]
    [InlineData("Tabii ki")]
    [InlineData("Harika bir soru")]
    [InlineData("Memnuniyetle")]
    public void Corporate_openings_are_explicitly_forbidden(string opening) =>
        Assert.Contains(opening, CustomerResponseStylePolicy.GenerationInstruction);
}
