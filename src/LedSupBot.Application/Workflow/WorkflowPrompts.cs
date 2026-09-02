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

    public const string RemindPanelLabelPhotos =
        "Panel etiketlerinin fotoğraflarını göndermeniz gerekiyor. Lütfen panel arkasındaki etiket fotoğraflarını paylaşın.";

    public const string AskReceiverCount =
        "Kaç adet receiver kullandınız? Panel arkasında bulunan yeşil kartlardan bahsediyorum.";

    public const string AskReceiverClarification =
        "Receiver sayısını rakam olarak paylaşabilir misiniz? Örnek: 8";

    public const string AskConfigConnection =
        "Cihaz ile bilgisayarı yazıcı kablosu, yani config kablosu ile bağlayabilir misiniz?";

    public const string ExplainConfigConnection =
        "Devam edebilmemiz için cihazı bilgisayara config/yazıcı kablosu ile bağlamanız gerekiyor. Bağladıktan sonra 'evet' yazabilirsiniz.";

    public const string ConfigExampleSent =
        "Config kablosu bağlantısı için örnek görseli paylaşıyorum.";

    public const string ConfigExampleUnavailable =
        "Config kablosu bağlantısı için örnek görsel hazırlanıyor. Bağlantıyı yaptıysanız devam edebiliriz.";

    public const string AskAnyDesk =
        "Şimdi AnyDesk'i açıp adresinizi benimle paylaşabilir misiniz?";

    public const string AskAnyDeskClarification =
        "AnyDesk adresinizi rakamlar halinde paylaşabilir misiniz?";

    public static string BuildSummary(decimal width, decimal height, int receiverCount, bool configConfirmed, bool photosReceived) =>
        $"""
         Teknik bilgileriniz:
         - Ekran ölçüsü: {width:0} x {height:0} cm
         - Receiver: {receiverCount} adet
         - Panel etiket fotoğrafları: {(photosReceived ? "Alındı" : "Bekleniyor")}
         - Config bağlantısı: {(configConfirmed ? "Yapıldı" : "Bekleniyor")}
         - AnyDesk: Alındı

         Gerekli teknik bilgileri aldım, teşekkürler.
         """;
}
