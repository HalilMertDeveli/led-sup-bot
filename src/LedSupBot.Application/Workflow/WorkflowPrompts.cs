using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public static class WorkflowPrompts
{
    public const string Greeting =
        "Merhaba, kolay gelsin. Size yardımcı olabilmem için birkaç bilgi alacağım.";

    public const string NaturalGreetingNoIssue =
        "Merhaba, kolay gelsin. Size nasıl yardımcı olabilirim?";

    public const string NaturalGreetingWithIssue =
        "Merhaba, kaydınızdaki sorunla devam edebiliriz.";

    public const string AskScreenSize =
        "Ekran ölçüleriniz nedir? Yatayda ve dikeyde kaç cm?";

    public const string AskScreenSizeAttempt2 =
        "Ölçüyü şu an net bilmiyorsanız sorun değil. Yaklaşık genişlik ve yüksekliği santimetre (cm) cinsinden söyleyebilir misiniz?";

    public const string AskMissingWidth =
        "Yatay ölçüyü (cm) paylaşabilir misiniz?";

    public const string AskMissingHeight =
        "Dikey ölçüyü (cm) paylaşabilir misiniz?";

    public const string AskMeterConfirmation =
        "Ölçüyü cm olarak teyit edebilir misiniz?";

    public const string AskScreenClarification =
        "Ekran ölçüsünü cm cinsinden yatay ve dikey olarak paylaşabilir misiniz? Örnek: 500x300";

    public const string AskPanelLabelPhotos =
        "Panelin arkasındaki etiketlerin fotoğraflarını bana atabilir misiniz? Panel arkasında bulunan bütün etiketleri göndermeniz önemli.";

    public const string AskPanelLabelPhotosAttempt2 =
        "Uygunsa panelin arkasındaki ürün/etiket kısmının tek bir fotoğrafı yeterli.";

    public const string RemindPanelLabelPhotos =
        "Panel etiketlerinin fotoğraflarını göndermeniz gerekiyor. Lütfen panel arkasındaki etiket fotoğraflarını paylaşın.";

    public const string AskReceiverCount =
        "Kaç adet receiver kullandınız? Panel arkasında bulunan yeşil kartlardan bahsediyorum.";

    public const string AskReceiverCountAttempt2 =
        "Panel arkasında kaç adet yeşil receiver kartı bulunduğunu rakam olarak paylaşabilir misiniz? Örnek: 8";

    public const string AskReceiverModel =
        "Receiver modeli hangisi: E120, E80, 5A75E veya E320 Pro?";

    public const string AskReceiverModelAttempt2 =
        "Panel arkasındaki yeşil kartın üzerinde E120, E80 veya 5A75E gibi bir model yazısı görebiliyor musunuz?";

    public const string AskReceiverClarification =
        "Receiver sayısını rakam olarak paylaşabilir misiniz? Örnek: 8";

    public static string AskReceiverCountForModel(string model) =>
        $"Tamam, {model} kullanıyorsunuz. Toplam kaç adet receiver var?";

    public static string AskReceiverModelForCount(int count) =>
        $"Tamam, {count} adet. Receiver modeli hangisi: E120, E80, 5A75E veya E320 Pro?";

    public const string AskPlayerType =
        "Oynatıcı olarak Playbox mı kullanıyorsunuz, yoksa processor mü?";

    public const string AskPlayerTypeAttempt2 =
        "Ekranı kontrol eden cihaz Playbox (küçük medya kutusu) mu yoksa üzerinde düğmeleri/ekranı olan bir processor mü?";

    public const string AskProcessorHdmi =
        "Processor kullanıyorsanız bilgisayarınızın HDMI çıkışından bir HDMI kablosunu processorün HDMI girişine bağlamanız gerekiyor. HDMI kablosunu bilgisayardan processorün HDMI girişine bağlayabildiniz mi?";

    public const string AskProcessorHdmiAttempt2 =
        "Bilgisayarınızın HDMI çıkışından çıkan HDMI kablosunu processorün HDMI girişine takmanız gerekiyor. Bağlayabildiniz mi?";

    public const string ExplainProcessorHdmi =
        "Bilgisayarınızın HDMI çıkışından çıkan HDMI kablosunu processorün HDMI girişine takmanız gerekiyor. Processor görüntüyü bilgisayardan bu bağlantıyla alacak. Bağlayabildiğinizde haber verin, devam edelim.";

    public const string AskConfigConnection =
        "Bilgisayar ile cihazı yazıcı tipi USB config kablosuyla bağlamamız gerekiyor. Kablonun kareye yakın USB-B ucunu cihaza, standart USB-A ucunu bilgisayara takın. Görselini de gönderiyorum. Bağladıktan sonra haber verirseniz devam edelim.";

    public const string AskConfigConnectionFollowUp =
        "Bağladıktan sonra haber verirseniz devam edelim.";

    public const string AskConfigConnectionAttempt2 =
        "Cihaz ile bilgisayar arasındaki yazıcı tipi USB config kablosunu bağlayabildiniz mi? Bağladıktan sonra 'evet' yazabilirsiniz.";

    public const string ExplainConfigConnection =
        "Kutunun içinden çıkan yazıcı tipi USB kablosunu kullanıyoruz. Kablonun kareye yakın USB-B ucunu cihaza, standart USB-A ucunu bilgisayarınıza takmanız gerekiyor.";

    public const string ConfigExampleSent =
        "Örnek bağlantı görselini paylaşıyorum.";

    public const string ConfigExampleUnavailable =
        "Kutunun içinden çıkan yazıcı tipi USB kablosunu kullanabilirsiniz. Kablonun kareye yakın USB-B ucunu cihaza, standart USB-A ucunu bilgisayarınıza takın. Bağlantıyı yaptıysanız devam edebiliriz.";

    public const string AskAnyDesk =
        "Şimdi AnyDesk'i açıp adresinizi benimle paylaşabilir misiniz?";

    public const string AskAnyDeskAttempt2 =
        "AnyDesk programını açtığınızda ekranda görünen 9 veya 10 haneli adresi rakamlar halinde gönderebilir misiniz?";

    public const string AskAnyDeskClarification =
        "AnyDesk adresinizi rakamlar halinde paylaşabilir misiniz?";

    public static string BuildSummary(
        decimal width,
        decimal height,
        int receiverCount,
        bool configConfirmed,
        bool photosReceived) =>
        BuildSummary((decimal?)width, (decimal?)height, (int?)receiverCount, null, null, null, configConfirmed, photosReceived);

    public static string BuildSummary(
        decimal? width,
        decimal? height,
        int? receiverCount,
        string? receiverModel,
        PlayerType? playerType,
        bool? processorHdmiConnected,
        bool configConfirmed,
        bool photosReceived)
    {
        var screenLine = width is > 0 && height is > 0
            ? $"- Ekran ölçüsü: {width:0} x {height:0} cm"
            : "- Ekran ölçüsü: Bilgi alınamadı";

        var receiverLine = receiverCount is > 0
            ? (!string.IsNullOrWhiteSpace(receiverModel) ? $"- Receiver: {receiverCount} adet {receiverModel}" : $"- Receiver: {receiverCount} adet")
            : (!string.IsNullOrWhiteSpace(receiverModel) ? $"- Receiver modeli: {receiverModel}" : "- Receiver: Bilgi alınamadı");

        var playerLine = playerType switch
        {
            PlayerType.Playbox => "- Oynatıcı: Playbox\n",
            PlayerType.Processor => $"- Oynatıcı: Processor (HDMI: {(processorHdmiConnected == true ? "Bağlı" : "Bilgi alınamadı")})\n",
            _ => ""
        };

        var photoLine = $"- Panel etiket fotoğrafları: {(photosReceived ? "Alındı" : "Alınamadı")}";
        var configLine = $"- Config bağlantısı: {(configConfirmed ? "Yapıldı" : "Alınamadı")}";
        var anyDeskLine = "- AnyDesk: Alındı";

        var allCollected = width is > 0 && height is > 0 && receiverCount is > 0 && configConfirmed && photosReceived;
        var closing = allCollected
            ? "Gerekli teknik bilgileri aldım, teşekkürler."
            : "Paylaştığınız bilgileri kaydettim, teşekkürler.";

        return $"""
         Teknik bilgileriniz:
         {screenLine}
         {receiverLine}
         {playerLine}{photoLine}
         {configLine}
         {anyDeskLine}

         {closing}
         """;
    }
}
