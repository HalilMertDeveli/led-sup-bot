namespace LedSupBot.Application.Workflow.Knowledge;

public sealed class DefaultTechnicalKnowledgeProvider : ITechnicalKnowledgeProvider
{
    public Task<string?> GetRelevantKnowledgeAsync(
        string customerUtterance,
        IReadOnlyList<string> knownFacts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerUtterance))
        {
            return Task.FromResult<string?>(null);
        }

        var lower = customerUtterance.ToLowerInvariant();
        var factsText = string.Join(" ", knownFacts).ToLowerInvariant();
        var guidance = new List<string>();

        // 1. Color shift / Pink / Green / Discoloration issues
        if (lower.Contains("pembe") || lower.Contains("renk") || lower.Contains("yeşil") || lower.Contains("kırmızı") || lower.Contains("ton") || lower.Contains("fark"))
        {
            guidance.Add(
                "Renk bozulması veya tek bir rengin baskın çıkması (örneğin pembe/yeşil görüntü): Genellikle ilk problemli modüle giren yassı data kablosundaki (flat kablo) pin temassızlığından veya receiver çıkış portundaki data hattından kaynaklanır. İlk adım: Bozulmanın başladığı ilk modülün data kablosunu çıkartıp tekrar takmak veya değiştirmektir.");
        }

        // 2. Missing half / Half screen off / Signal chain drop
        if (lower.Contains("yarısı") || lower.Contains("yarisi") || lower.Contains("gelmiyor") || lower.Contains("kesildi") || lower.Contains("çalışmıyor") || lower.Contains("calismiyor") || lower.Contains("görüntü yok") || lower.Contains("goruntu yok"))
        {
            guidance.Add(
                "Ekranın bir bölümünün veya yarısının gelmemesi: Receiverlar arasındaki RJ45 data kablosu kopukluğu/gevşekliği, o bölgedeki receiverın 5V beslemesinin kesilmesi veya port data kapasitesinin aşılmasından kaynaklanır. İlk adım: Görüntünün kesildiği ilk kabini/receiverı tespit etmek ve oraya gelen ethernet data kablosu ile receiverın üzerindeki durum LED'lerini (güç ve sinyal) kontrol etmektir.");
        }

        // 3. Receiver offline / Not recognized / Receiver functions
        if (lower.Contains("online") || lower.Contains("görünmüyor") || lower.Contains("gorunmuyor") || lower.Contains("kart ne işe") || lower.Contains("receiver nedir") || lower.Contains("kart nedir"))
        {
            guidance.Add(
                "Receiver (Alıcı) Kart: LED panellerin arkasında bulunur; gönderici karttan (sender/processor) gelen gigabit sinyali alıp modüllere dağıtır. Kartın yazılımda (NovaLCT/LEDSet) görünmemesi durumunda: 1) Kart üzerindeki 5V besleme LED'i yanıyor mu? 2) Bilgisayar ile kart arasındaki USB config kablosu ve gigabit ethernet kablosu takılı mı? 3) Ağ kartı 1Gbps hızında mı? kontrol edilmelidir.");
        }

        // 4. Processor & HDMI issues
        if (lower.Contains("processor") || lower.Contains("prosesör") || lower.Contains("hdmi") || lower.Contains("giriş") || lower.Contains("oynatıcı") || lower.Contains("işlemci"))
        {
            guidance.Add(
                "Video Processor: Bilgisayardan gelen HDMI/DVI görüntüsünü LED ekranın çözünürlüğüne ölçekler. HDMI görüntüsü gelmiyorsa: 1) Bilgisayarda ekranı 'Yinele/Genişlet' moduna alın (Win+P). 2) Processor ön panelindeki HDMI giriş butonuna basarak kaynağın aktif olduğunu doğrulayın. 3) Bilgisayar çözünürlüğünü 1080p@60Hz olarak ayarlayın.");
        }

        // 5. Config file loading & USB cable
        if (lower.Contains("config") || lower.Contains("rcfg") || lower.Contains("yazıcı kablo") || lower.Contains("nasıl yüklenir") || lower.Contains("nasil yuklenir") || lower.Contains("ayar"))
        {
            guidance.Add(
                "Config Yükleme: Kontrol yazılımında (NovaLCT/LEDSet) Receiving Card (Alıcı Kart) sekmesinden 'Load from File' ile .rcfgx/.rcfg dosyası seçilir, 'Send to Receiving Card' denir ve ardından 'Save to HW' (Donanıma Kaydet) yapılır. Bu işlem için cihazın bilgisayara kare uçlu USB-B (yazıcı) kablosu ile bağlı olması şarttır.");
        }

        // 6. Flickering / Shaking display
        if (lower.Contains("titriyor") || lower.Contains("dalgalan") || lower.Contains("titreme") || lower.Contains("parazit"))
        {
            guidance.Add(
                "Ekran titremesi / dalgalanma: Yenileme hızı (refresh rate), 5V güç kaynaklarındaki voltaj düşüşü/dalgalanması veya topraklama eksikliğinden kaynaklanabilir. İlk adım: Video kablosunu ve güç kaynaklarının 5V çıkışını kontrol etmektir.");
        }

        // 7. Power supply (5V) check
        if (lower.Contains("power") || lower.Contains("güç") || lower.Contains("guc") || lower.Contains("trafo") || lower.Contains("besleme") || lower.Contains("voltaj"))
        {
            guidance.Add(
                "LED Güç Kaynağı (Trafo) Kontrolü: LED modüller 5V DC ile çalışır. Bir multimetre (ölçü aleti) ile trafo çıkışının 4.8V - 5.2V aralığında olduğu teyit edilmelidir. Trafonun yeşil durum ledi yanmıyorsa 220V girişi ve sigortası kontrol edilmelidir.");
        }

        // 8. Specific receiver models context
        if (factsText.Contains("e120") || lower.Contains("e120"))
        {
            guidance.Add("Cihazda E120 receiver kullanılmaktadır. E120 kartları standart HUB75 pin çıkışına sahiptir.");
        }
        else if (factsText.Contains("5a75e") || lower.Contains("5a75e") || lower.Contains("colorlight"))
        {
            guidance.Add("Cihazda Colorlight 5A75E receiver kartları kullanılmaktadır.");
        }
        else if (factsText.Contains("e80") || lower.Contains("e80"))
        {
            guidance.Add("Cihazda E80 receiver kartları kullanılmaktadır.");
        }
        else if (factsText.Contains("e320") || lower.Contains("e320"))
        {
            guidance.Add("Cihazda E320 Pro serisi receiver kartları kullanılmaktadır.");
        }

        if (guidance.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(string.Join("\n- ", guidance.Prepend("İlgili Teknik Bilgi Rehberi:")));
    }
}
