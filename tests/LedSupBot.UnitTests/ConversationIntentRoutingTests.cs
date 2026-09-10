using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;

namespace LedSupBot.UnitTests;

public sealed class ConversationIntentRoutingTests
{
    [Theory]
    [InlineData("Yeni bir ekran kuracağız.", ConversationIntent.ScreenSetup)]
    [InlineData("Receiverları bağladık, devamında ne yapacağız?", ConversationIntent.ScreenSetup)]
    [InlineData("P2.5+1065S+2012 dosyalarını gönder.", ConversationIntent.FileRequest)]
    [InlineData("Ekranın yarısı çalışmıyor.", ConversationIntent.LedTechnicalSupport)]
    [InlineData("Receiver kırmızı yanıyor neden?", ConversationIntent.LedTechnicalSupport)]
    public void Routes_main_customer_intents(string message, ConversationIntent expected)
    {
        Assert.Equal(expected, ConversationSemantics.Route(message).PrimaryIntent);
    }

    [Fact]
    public void Preserves_both_intents_in_technical_file_request()
    {
        var route = ConversationSemantics.Route(
            "E120 neden kırmızı yanıyor bir de P2.5+1065S+2012 dosyasını gönder.");

        Assert.Equal(ConversationIntent.LedTechnicalSupport, route.PrimaryIntent);
        Assert.True(route.HasTechnicalSupport);
        Assert.True(route.HasFileRequest);
    }

    [Fact]
    public void Generic_file_request_asks_only_for_folder_name()
    {
        var turn = PanelFileConversationGuidance.Handle(
            new TechnicalSupportCase(), "Config dosyasını at.");

        Assert.True(turn.Attempted);
        Assert.Contains("klasör/model adını", turn.Reply);
        Assert.Contains("P2.5+1065S+2012", turn.Reply);
    }

    [Fact]
    public void Technical_route_holds_workflow_question()
    {
        Assert.Equal(
            ConversationResponseAction.AnswerAndHoldWorkflow,
            ConversationSemantics.ResolveAction(ConversationIntent.LedTechnicalSupport));
    }
}
