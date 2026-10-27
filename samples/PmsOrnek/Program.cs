// Otopark yazılımına (PMS) DOĞRUDAN GÖMÜLÜM referans örneği: Anpr.Sdk + AnprRuntime.
// PMS projeniz `Anpr.Sdk` paketini referans alır, AnprRuntime'ı KENDİ süreci içinde çalıştırır ve
// PlateDetected olayını dinler. Kamera RTSP adresleri ortam değişkeninden (kimlik bilgisi repoya girmez):
//   ANPR_RTSP_GIRIS="rtsp://kullanici:sifre@ip:554/Streaming/Channels/101"
//   ANPR_RTSP_CIKIS="..." (isteğe bağlı ikinci kamera)
// Çalıştırma: dotnet run  (60 sn okur, gelen olayları yazar, kapanır).
//
// ⚠️ BU DOSYADAKİ SAYILAR NOKTASAL ÖLÇÜMLERDİR ve BAYATLIK DENETİMİ YOKTUR.
//    `scripts/check_readme_claims.py` yalnız README.md ve Anpr.Sdk/README.md'yi tutar; bu örnek
//    kapsam DIŞIDIR. Buradaki oranlar yazıldıkları gündeki ölçümlerdir — bir KARAR verirken
//    README'deki (denetlenen) güncel değerlere bakın. Yeni yorum yazarken de buraya bayatlayacak
//    bir sayı EKLEMEYİN; ölçümün YÖNÜNÜ yazın, değerini denetlenen yüzeye bırakın.
//    (2026-08-08: bu kural, buraya "34 bayrağın 32'si" diye yazıp aynı gün 40'a çıktığını görünce
//     kondu — sayı yanlış değildi, YERİ yanlıştı.)

using Anpr.Sdk;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Logging;

string? giris = Environment.GetEnvironmentVariable("ANPR_RTSP_GIRIS");
string? cikis = Environment.GetEnvironmentVariable("ANPR_RTSP_CIKIS");
if (giris is null && cikis is null)
{
    Console.Error.WriteLine("ANPR_RTSP_GIRIS (ve isteğe bağlı ANPR_RTSP_CIKIS) ortam değişkenini ayarlayın.");
    return 1;
}

var options = new AnprServiceOptions();   // üretimde doğrulanmış varsayılanlar (çoğu senaryoda yeterli)

// Doğruluk: sitenin bilinen araçlarını verin → sistematik yanlış-okumalar bilinen plakaya lev≤1
// düzeltilir (ör. bir karede 25 ADM 032 -> 25 AUM 032 okunursa geri düzelir). Ölçüldü: +9.3pp,
// %0.16 yanlış-snap. Listeyi veriden üretin: scripts/harvest_knownplates.py (kamera+kendi okumalar).
// options.KnownPlates.AddRange(new[] { "25 ADM 032", "25 TGG 001" });
// options.SnapMaxLev = 1;                 // lev≤1 (lev≤2 bariyer için fazla yanlış-snap ölçüldü)
// options.SnapMaxOcrConfidence = 0.99f;   // varsayılan; model ≥0.99 okumaları korunur

if (giris != null) options.Cameras.Add(new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = giris });
if (cikis != null) options.Cameras.Add(new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = cikis });

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                                     .SetMinimumLevel(LogLevel.Information));

await using var runtime = new AnprRuntime(options, loggerFactory);

int eventCount = 0;
// KONTRAT (M2): işleyici TEK dispatcher thread'inde SERİLEŞMİŞ çağrılır (eşzamanlı yok) ve worker boru
// hattını BLOKLAMAZ. Yine de makul hızlı dönün — çok yavaşsa kuyruk (256) dolar, olaylar DÜŞER
// (runtime.DroppedEvents sayar). Uzun işi kendi kuyruğunuza atın. Burada Interlocked+Console (hızlı).
// Geriye dönük uzlaştırma için son 15 sn penceresi (aşağıdaki UzlastirGecmisi kullanır).
var uzlastirSon = new List<Anpr.Sdk.Events.PlateEvent>();
var uzlastirKilit = new object();

// Uzlaştırmanın ASGARİ GÜVEN FARKI eşiği. Neredeyse eşit güvenli iki okuma arasında sıralama
// bilgi taşımaz; ölçüldü (2026-08-02) — iki zararlı düzeltmenin ikisi de Δ < 0,01 idi.
// 0,02 muhafazakâr ve YUVARLAK bir seçimdir: 0,02–0,15 aralığının tamamında isabet %100 çıkıyor,
// yani eşik bıçak sırtında değil. ⚠️ Veri büyüdükçe yeniden ölçün.
const float MinGuvenFarki = 0.02f;

runtime.PlateDetected += e =>
{
    if (e.Scope != "roi") return;                       // "gozlem" (ROI dışı) okumalar bariyer kararına girmez
    Interlocked.Increment(ref eventCount);

    // 🔴 ŞEMA SÜRÜMÜ: **EŞİTLİK KONTROLÜ YAPMAYIN.** Şema ileriye dönük genişler (bugün v5); yeni
    // alanlar EKLENİR, var olanların anlamı DEĞİŞMEZ, alan KALDIRILMAZ. `== 5` yazan bir PMS bir
    // sonraki sürümde SESSİZCE olayları reddeder. Doğru desen `>=` ile TABAN kontrolüdür:
    if (e.SchemaVersion < 2)
    {
        // Bu SDK'nın üreteceği bir durum değil; savunma amaçlı — beklenmedik bir yükü sessizce
        // işlemek yerine gürültülü reddedin.
        Console.Error.WriteLine($"    [uyarı] beklenmeyen şema v{e.SchemaVersion}, olay atlandı");
        return;
    }
    // Tanımadığınız YENİ alanları yok saymak güvenlidir; eksik alanları VARSAYMAK değildir:
    //   v4'ten önce  → e.LowConfidenceReasons BOŞ olabilir ama e.LowConfidence yine de true olabilir
    //                  (ölçüldü: diskte 270 böyle kayıt) → "sebep yok, demek ki güvenli" SONUCUNA VARMAYIN.
    //   v5'ten önce  → e.EarlyEmitHeldAtOcr null = "tutulmadı" DEĞİL, "BİLİNMİYOR".

    // PMS iş kuralı örneği:
    //  - e.EventId       : idempotency anahtarı (retry teslimi mükerrer olabilir; işlenmişse yoksay)
    //  - e.LowConfidence : true ise bariyeri otomatik açma, operatör onayına düşür (kanıt görseliyle)
    //  - e.EvidencePath  : plaka kırpıntısı JPEG ; e.FramePath : aracın da göründüğü tam kare
    //  - e.Candidates    : whitelist/abone eşleşmesi için top-K alternatif
    // Plaka whitelist'e göre DÜZELTİLDİYSE (şema v2) denetim izine yaz: snap sistematik yanlış
    // okumaları onarır ama %100 değildir; e.SnappedFrom snap ÖNCESİ ham okumadır.
    if (e.SnappedFrom != null)
        Console.WriteLine($"    [denetim] snap: {e.SnappedFrom} -> {e.PlateCompact}");

    // NOT: kendi eşiğinizi kuracaksanız e.OcrConfidence kullanın — e.Confidence bir ÇARPIMDIR
    // (dedektör × ocr) ve ManualReviewMinOcr ile aynı ölçekte DEĞİLDİR.
    if (e.LowConfidence)
    {
        // e.LowConfidenceReasons (şema v4+) NEDEN düşük olduğunu söyler ve operatör TRİYAJINI
        // hızlandırır — sebepler aynı ağırlıkta DEĞİLDİR (canlı ölçüm, bkz. Anpr.Sdk/README.md):
        //   "cross-camera-substitution" / "cross-camera-truncation"
        //        → karşı kamera ÇELİŞİYOR. Canlı ölçümde bu sınıfın kesinliği **%95'in üstünde**
        //          ve bayrakların çoğu OTOMATİK AÇMA EŞİĞİNİN ÜSTÜNDE — yani kapı olmasa bariyer
        //          yanlış plakayla açılacaktı. ÖNCE bunlara bakın.
        //          ⚠️ Güncel sayılar için `README.md` §doğrulama'ya bakın; buraya bilinçli olarak
        //          BAYATLAYACAK bir sayı yazılmadı (bu dosyanın bayatlık denetimi YOKTUR;
        //          `scripts/check_readme_claims.py` yalnız README'leri tutar).
        //   "weak-reading" / "last-char-ambiguity"
        //        → yalnız güven düşük; çoğu zaten eşiğin altında ve bariyeri zaten açmıyor.
        // ⚠️ Liste BOŞ olabilir (v4 öncesi olaylar) — boşluğu "sorun yok" diye okumayın.
        bool karsiKameraCelisiyor = e.LowConfidenceReasons.Any(r => r.StartsWith("cross-camera"));
        OperatorOnayinaDusur(e, oncelikli: karsiKameraCelisiyor);
    }
    else
        BariyerVeKayit(e);

    // GERİYE DÖNÜK UZLAŞTIRMA — bu boşluğu YALNIZ PMS kapatabilir (ölçüldü, bkz. Anpr.Sdk/README.md).
    // SDK'nın çapraz-kamera kapıları GERİYE DÖNÜKTÜR: bir okumayı yalnız DAHA ÖNCE yayınlanmış daha
    // güvenli bir okumayla karşılaştırabilir. Zayıf okuma ÖNCE gelirse kapı onu göremez ve olay
    // bariyeri açmış olur. Canlı ölçüm: 38 çapraz-kamera lev-1 çiftinin 17'sinde zayıf taraf önce
    // geldi ve 16'sı otomatik açtı. Eşik bandındaki (0,85–0,95) hataların %66'sı bu sınıftadır.
    //
    // Bariyer geri alınamaz — ama KAYIT ve ÜCRETLENDİRME düzeltilebilir. Asıl zarar zaten orada:
    // giriş yanlış araca (bazen BAŞKA BİR ABONEYE) yazılıyor.
    UzlastirGecmisi(e);
};

await runtime.StartAsync();   // modeller yoksa/bozuksa fırlatır (sessiz yanlış çalışma yok)
Console.WriteLine($"ANPR çalışıyor ({options.Cameras.Count} kamera, CPU: {runtime.CpuFeatures}). 60 sn dinleniyor...");

// Üretimde: uygulama ömrü boyunca çalışır; kapanışta StopAsync (aşağıdaki 'await using' halleder).
await Task.Delay(TimeSpan.FromSeconds(60));

foreach (var cam in runtime.CameraStatus)   // salt-okunur durum görünümü (M1)
    Console.WriteLine($"[{cam.CameraId}] bağlı={cam.IsConnected} kare={cam.FramesReceived} " +
                      $"işlenen={cam.FramesProcessed} p50={cam.FrameMsP50}ms p95={cam.FrameMsP95}ms olay={cam.EventsEmitted}");
Console.WriteLine($"Toplam roi olayı: {eventCount}");

await runtime.StopAsync();
return 0;

// --- PMS tarafı iş kuralı iskeleti (örnek) ---
static void BariyerVeKayit(Anpr.Sdk.Events.PlateEvent e) =>
    Console.WriteLine($">>> PLAKA: {e.Plate} (güven {e.OcrConfidence:P0}, {e.CameraId})");

static void OperatorOnayinaDusur(Anpr.Sdk.Events.PlateEvent e, bool oncelikli = false)
{
    // `oncelikli` = karşı kamera ÇELİŞİYOR → ölçülen kesinlik yüksek, kuyruğun BAŞINA alın.
    string sebepler = e.LowConfidenceReasons.Count > 0
        ? string.Join(",", e.LowConfidenceReasons)
        : "(sebep yok — v4 öncesi olay olabilir, 'güvenli' DEMEK DEĞİL)";
    Console.WriteLine($">>> OPERATÖR ONAYI{(oncelikli ? " [ÖNCELİKLİ]" : "")}: {e.Plate} " +
                      $"(güven {e.OcrConfidence:P0}, {e.CameraId}, sebep: {sebepler}, kanıt: {e.EvidencePath})");
}

// --- GERİYE DÖNÜK UZLAŞTIRMA (referans uygulama) ---
//
// Son 15 saniyenin kayıtlarını tutar; yeni olay geldiğinde DAHA DÜŞÜK güvenli ve 1 karakter
// uzaklıktaki önceki kaydı bulur ve DÜZELTİLMESİ gerektiğini bildirir.
//
// İki bozulma biçimi ölçülmüştür ve ikisi de burada kapsanır:
//   · İKAME  : aynı uzunluk, tam 1 karakter farklı   (ör. 25ADM034 ← 25AUH034)
//   · KESİLME: tam 1 karakter kısa ve ön-ek           (ör. 25AFG450 ← 25AFG45)
//
// ⚠️ YÖN ŞARTI ATLANMAZ: yalnız YENİ okuma DAHA GÜVENLİ ise düzeltme yapılır. Ölçülmüş karşı-örnek
// olmasaydı bu kural doğru okumaları da bozardı (polis plakası `25 A 4855`, kısa taraf doğruydu).
//
// DOĞRULANDI (2026-08-01, 10 canlı vaka): pozitifler `25ES048←25LS048` · `25AFJ097←25AFJ897` ·
// `08LE481←06LE481` · `25AFG45←25AFG450` · `34ZH764←34ZH7641`; negatifler `25AUH034←25ADM034` ve
// `25ADN039←25ADM034` (lev-2, kapsam dışı) · `25A48550←25A4855` (ön-ek TERS yönde) ·
// aynı dizgi (dedup'ın işi) · `25EJ176←25EJ188` (iki GERÇEK araç). 10/10 beklenen davranış.
//
// ⚠️ NEDEN lev-2'YE AÇILMADI (2026-08-02'de ÖLÇÜLDÜ ve REDDEDİLDİ): lev-2 sınıfı gerçektir ve
// yakalanmayan hataların çoğu oradadır — ama uzlaştırmayı lev≤2 yapmak **artımlı** olarak
// 20 yeni tetikleme getirip yalnız **4'ünde** gerçek bir hatayı düzeltiyor ve **2 yeni ZARARLI**
// düzeltme üretiyor (artımlı isabet ~%20; lev-1'in isabeti %90). Yani lev-2 bu kuralın DEĞİL,
// insan incelemesinin işidir. (Aynı sonuç SDK'nın çapraz-kamera kapısı için de ölçülmüştü —
// maliyet yapısı farklı olmasına rağmen cevap aynı çıktı.)
void UzlastirGecmisi(Anpr.Sdk.Events.PlateEvent yeni)
{
    lock (uzlastirKilit)
    {
        uzlastirSon.RemoveAll(x => yeni.Timestamp - x.Timestamp > TimeSpan.FromSeconds(15));
        foreach (var eski in uzlastirSon)
        {
            if (eski.EventId == yeni.EventId) continue;
            // YÖN ŞARTI + ASGARİ GÜVEN FARKI. Yalnız ">" yetmiyor: ölçüldü (2026-08-02, 23 tetikleme)
            // — iki okuma neredeyse eşit güvendeyse sıralama BİLGİ TAŞIMIYOR ve düzeltme YANLIŞ yöne
            // gidebiliyor. İki zararlı vaka da Δ < 0,01 idi (`34SB7298`→34SBT298 Δ0,008 ve
            // `25EB045`→25EB041 Δ0,001; ikisi de DOĞRU kaydı bozardı). Δ ≥ 0,02 şartıyla:
            // 23 → 17 tetikleme, doğru 18 → 16, ZARARLI 2 → 0. İki doğru düzeltme kaybedilir ama
            // DOĞRU bir kaydı bozmak, eksik düzeltmeden daha kötüdür — takas bilinçlidir.
            if (yeni.OcrConfidence - eski.OcrConfidence < MinGuvenFarki) continue;
            if (!BozulmusMu(eski.PlateCompact, yeni.PlateCompact)) continue;

            // ÜRETİMDE: eski kaydın plakasını `yeni.PlateCompact` ile güncelleyin ya da o geçişi
            // operatör kuyruğuna alın. Ücretlendirme yapıldıysa düzeltme kaydı üretin.
            Console.WriteLine($">>> UZLAŞTIR: {eski.PlateCompact} ({eski.OcrConfidence:P0}) kaydı " +
                              $"muhtemelen {yeni.PlateCompact} ({yeni.OcrConfidence:P0}) — " +
                              $"olay {eski.EventId}, Δ{(yeni.Timestamp - eski.Timestamp).TotalSeconds:F1} sn");
        }
        uzlastirSon.Add(yeni);
    }
}

static bool BozulmusMu(string zayif, string guclu)
{
    if (zayif.Length == guclu.Length)                       // İKAME: tam 1 karakter farklı
    {
        int fark = 0;
        for (int i = 0; i < zayif.Length && fark < 2; i++)
            if (zayif[i] != guclu[i]) fark++;
        return fark == 1;
    }
    // KESİLME: zayıf okuma, güçlü okumanın tam 1 karakter kısa ön-eki
    return guclu.Length == zayif.Length + 1 && guclu.StartsWith(zayif, StringComparison.Ordinal);
}
