using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Admin;

public static class AdminStageLabels
{
    public static string ToLabel(ConversationStage stage) => stage switch
    {
        ConversationStage.Start => "Başlangıç",
        ConversationStage.Greeting => "Karşılama",
        ConversationStage.AskScreenSize => "Ekran ölçüsü",
        ConversationStage.AskPanelLabelPhotos => "Panel fotoğrafları",
        ConversationStage.AskReceiverCount => "Receiver sayısı",
        ConversationStage.AskReceiverModel => "Receiver modeli",
        ConversationStage.AskPlayerType => "Oynatıcı türü",
        ConversationStage.AskProcessorHdmi => "Processor HDMI",
        ConversationStage.AskConfigConnection => "Config bağlantısı",
        ConversationStage.SendConfigExample => "Config örneği",
        ConversationStage.AskAnyDesk => "AnyDesk",
        ConversationStage.CollectingFinalInfo => "Son bilgiler",
        ConversationStage.HumanHandoff => "İnsan desteği",
        ConversationStage.Completed => "Tamamlandı",
        _ => stage.ToString(),
    };
}
