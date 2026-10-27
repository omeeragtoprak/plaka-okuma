using Microsoft.ML.OnnxRuntime;

namespace Anpr.Core;

/// <summary>
/// Anpr.Core motorunun yapılandırması. Zayıf/CPU-only makineler için varsayılanlar konservatif tutuldu.
/// </summary>
public sealed class AnprOptions
{
    /// <summary>YOLOv9-t end2end ONNX dedektör model yolu.</summary>
    public string DetectorModelPath { get; set; } = "models/yolo-v9-t-384-license-plates-end2end.onnx";

    /// <summary>fast-plate-ocr CCT ONNX model yolu.</summary>
    public string OcrModelPath { get; set; } = "models/cct_xs_v1_global.onnx";

    /// <summary>OCR modeline ait yaml config yolu (alfabe, giriş boyutu vb.).</summary>
    public string OcrConfigPath { get; set; } = "models/cct_xs_v1_global_plate_config.yaml";

    /// <summary>
    /// ONNX Runtime IntraOp iş parçacığı sayısı. Varsayılan <b>1</b> (2.1.0; önce 2): çıkarım başına toplam işlemci
    /// zamanı en düşük olan ayar (2026-10-20, yolo-v9-t-384: 96,6 → 77,6 ms işlemci/çıkarım). Bedeli duvar saati
    /// gecikmesidir (55 → 76 ms ortanca, M4) — kare 200–500 ms arayla geldiği için zayıf PC'de doğru takas budur.
    /// </summary>
    public int OnnxIntraOpThreads { get; set; } = 1;

    /// <summary>
    /// ONNX thread'leri iş beklerken dönerek (spin) mi beklesin? Spin = kare-başı en düşük gecikme ama
    /// BOŞTA da CPU yakar. Bariyer kamerası günün çoğunda boştadır; hedef donanım zayıf PC olduğu için
    /// varsayılan <b>false</b> (2.0.0 — Faz 2). Kare-başı gecikme birkaç ms artabilir; boşta CPU düşer.
    /// Çok çekirdekli güçlü makinede ve sürekli yoğun trafikte true denenebilir — ölçerek.
    /// </summary>
    public bool OnnxAllowSpinning { get; set; } = false;

    /// <summary>
    /// ONNX Runtime CPU bellek ARENASI. Açıkken (ORT varsayılanı) her oturum, ara tensörler için büyük
    /// bloklar ayırır ve SÜREÇ BOYUNCA elinde tutar: çıkarım biraz hızlanır, ama oturum başına onlarca MB
    /// kalıcı bellek demektir (kamera başına 4 oturum × kamera sayısı). Kapalıyken her çıkarım belleği
    /// işletim sisteminden alır ve geri verir. Varsayılan <b>false</b> (2.1.0): ölçüm (2026-10-20,
    /// <c>Anpr.Bench membench</c>, iki motor, dört oturum) arena kapalıyken süreç ~28 MB daha az tutuyor ve kare başına
    /// işlemci zamanı değişmiyor (41,6 ↔ 41,0 ms).
    /// </summary>
    public bool OnnxCpuMemArena { get; set; } = false;

    /// <summary>
    /// ONNX Runtime bellek deseni: sabit giriş boyutunda ara tensörlerin tahsisini tek blokta önceden planlar ve o
    /// bloğu oturum boyunca TUTAR. Varsayılan <b>false</b> (2.1.0). Ölçüm (2026-10-20, membench, arena kapalı, iki
    /// motor): kapalıyken süreç 25–31 MB daha az tutuyor (dedektör ısınması 25 → 16 MB); kare başına işlemci farkı
    /// ölçüm gürültüsünde (42–45 ↔ 44–45 ms).
    /// </summary>
    public bool OnnxMemoryPattern { get; set; } = false;

    /// <summary>
    /// OCR'ın çalıştırılacağı minimum dedektör güven skoru. Bunun altındaki tespitlere OCR uygulanmaz.
    /// </summary>
    public float MinConfidence { get; set; } = 0.5f;

    /// <summary>Dedektörün ham skor eşiği (bunun altındaki tespitler hiç dikkate alınmaz).</summary>
    public float DetectorScoreThreshold { get; set; } = 0.25f;

    /// <summary>Türkiye plaka doğrulama/normalizasyonu uygulansın mı.</summary>
    public bool ApplyTurkishValidation { get; set; } = true;

    /// <summary>
    /// Plaka kırpılırken kutunun her kenarına eklenecek pay oranı (0.06 = %6).
    /// Sıkı kutuların plaka kenarlarını kesmesini önler, OCR doğruluğunu artırır.
    /// </summary>
    public float CropPaddingRatio { get; set; } = 0.06f;

    /// <summary>
    /// <b>HEDEFLİ İKİNCİ OKUMA (kuyruk belirsizliği).</b> 0 = KAPALI (varsayılan).
    /// &gt; <see cref="CropPaddingRatio"/> ise: OCR'ın SON karakterden sonraki slotunda "dolgu mu, bir
    /// karakter daha mı" kararı belirsizse (bkz. <see cref="AmbiguousTailMaxGap"/>) kırpıntı bu oranla
    /// YENİDEN alınır ve OCR bir kez daha çalıştırılır.
    /// <para><b>NEDEN VAR (2026-08-22, §P.4 (358)/(361)).</b> Ölçülen kusur sınıfı: yayınlanan plaka, tanığın
    /// plakasının tam öneki ve <b>tam 1 karakter</b> eksik (16/9728 = %0,16 [%0,10–%0,27]). 16/16'sı
    /// format olarak GEÇERLİ (`TurkishPlateNormalizer` 7–8 karakter kabul eder; 8'den 1 düşünce 7 kalır,
    /// 7 de geçerlidir) ve 16/16'sı emit eşiğinin üstündedir — yani ne doğrulama ne güven eşiği bu
    /// sınıfı ayırabilir.</para>
    /// <para>🔬 <b>MEKANİZMA.</b> OCR kırpıntısı dardır — bu <b>yeni bir bulgu değil</b>,
    /// <c>CameraWorker.SaveEvidence</c> yorumunda <b>2026-07-29'dan beri</b> yazılı: *sağ pay
    /// &lt; 0,20 karakter olan okuma oranı %21,4*. Yeni olan, bunun <b>uçtan uca bedeli</b>.</para>
    /// <para>🔑 <b>VE KESİLME PER-KARE OCR'DA DOĞMUYOR (2026-08-22 ölçüldü, §P.4 (361)):</b>
    /// tam kareden <c>FrameBox</c> ile kırpılıp tek kare okunduğunda model <b>hiç kesmiyor</b> —
    /// 4/4 vakada <b>uzunluğu doğru</b> buluyor (8 karakter), yalnız son <i>rakamı</i> yanılıyor
    /// (<c>25NUP524</c> · <c>25ELF834</c> · <c>25FTY949</c> · <c>25EUH776</c>, conf ~0,90).
    /// Kesilme <b>iz-düzeyi slot ortalamasında</b> doğar: kareler son rakamda anlaşamayınca o
    /// slotta olasılık dağılır ve <b>dolgu simgesi</b> kazanır; gramer çözücü geriye kalan
    /// <b>geçerli</b> 7 karakteri basar. Oylama karakteri düzeltir, <b>uzunluğu bozar</b>.</para>
    /// <para>⛔ <b>Bu yüzden iki ucuz çare ELENDİ:</b> (a) 2. adayı yükseltmek — son rakamı yalnız
    /// <b>3/12</b>'de doğru; (b) uzunluğu koruyan oylama — aynı vakaları 8 karaktere çıkarır, 9'u
    /// yanlış olur. İkisi de kesilmeyi <b>sessiz yanlış plakaya</b> çevirir: kesik plaka hiçbir
    /// aboneyle eşleşmez (bariyer kapalı, operatör görür), yanlış 8 karakter <b>başka bir aracı</b>
    /// tutturabilir. Bilgi gerçekten piksellerde eksik → çare daha çok piksel: dolgu 0,06 → 0,15'te
    /// <b>4/4 doğru 8 karakterli plakaya döndü</b>, güven 0,90 → <b>0,998</b>
    /// (`scripts/crop_padding_ab.py`; koşum takımı gerçek C# yolunda doğrulandı, kontrol 56/60).</para>
    /// <para>⛔ <b>KÜRESEL DOLGU ARTIŞI ÖLÇÜLDÜ ve REDDEDİLDİ.</b> Yansız örneklemde (n=600, tanıkla
    /// eşleşen TÜM olaylar — tabanda doğru olma şartı YOK, yoksa seçim yanlılığı olurdu) tek-kare uyum:
    /// 0,06 → %71,3 · 0,10 → %71,5 (eşleştirilmiş net +1, p=1,00) · 0,15 → %70,8 (net −3, p=0,70) ·
    /// 0,22 → %66,2 (net <b>−31</b>, <b>p&lt;0,0001</b>) · 0,30 → %58,7 (net −76, p&lt;0,0001) — zarar
    /// dolguyla <b>tekdüze</b> büyüyor, yani gürültü değil.
    /// Kesilme trafiğin %0,16'sı olduğundan küresel değişimin <b>azami kazancı +0,16 puan</b>, 0,15'te
    /// ölçülen zararı <b>−0,50 puan</b>: kazancın ~3 katı. Bu yüzden <see cref="CropPaddingRatio"/>
    /// DEĞİŞTİRİLMEDİ; çare hedefli olmalıdır — bedeli yalnız belirsiz kuyruklu karelerde öde.</para>
    /// <para>⚠️ Ölçüm <b>tek-kare vekilidir</b>; servis metni izdeki tüm karelerin oylamasından çıkar.
    /// %71,3 servis doğruluğu DEĞİLDİR, dolgu kollarını karşılaştırmak için ortak taban çizgisidir.</para>
    /// </summary>
    public float AmbiguousTailRecropPad { get; set; } = 0f;

    /// <summary>
    /// Kuyruk belirsizliği eşiği: son karakterden sonraki slotta P(dolgu) ile en iyi P(karakter) farkı
    /// bunun altındaysa "uzunluk belirsiz" sayılır ve <see cref="AmbiguousTailRecropPad"/> devreye girer.
    /// Yalnız o ayar &gt; 0 iken anlamlıdır.
    /// </summary>
    public float AmbiguousTailMaxGap { get; set; } = 0.25f;

    /// <summary>
    /// Tetik bölgesi: OCR yalnızca plaka kutusu genişliği, kare genişliğinin bu oranından büyükse
    /// çalıştırılır (plaka yakın/büyük = araç bariyerde). 0 = kapalı. ~0.03 uzak/küçük plakaları eler;
    /// hem CPU tasarrufu hem doğruluk (kötü uzak kareler işlenmez).
    /// </summary>
    public float MinPlateWidthRatio { get; set; } = 0.0f;

    /// <summary>
    /// Okuma alt sınırı (mutlak): DEDEKTÖR kutusu (dolgusuz) bu kadar px'ten darsa OCR çalıştırılmaz. 0 = kapalı.
    /// <para>Piksel, işlenen karenin pikselidir (<c>ScaleWidth</c> ile küçültülmüş karede küçültülmüş px).
    /// Oranın (<see cref="MinPlateWidthRatio"/>) aksine OCR'ın gördüğü bilgi miktarını doğrudan sınırlar, bu yüzden
    /// farklı çözünürlükteki kameralar arasında taşınabilir.</para>
    /// <para>⚠️ <b>Doğruluk ayarı DEĞİLDİR, kapsam ayarıdır</b> (ör. konvoyda kuyruktaki uzak aracı okumamak).
    /// Ölçüldü (2026-08-25): 100 px altındaki okumaların ~%76'sı doğruydu — eşik çoğunlukla doğru okumaları
    /// atar. Satıcıların 130 px önerisi bu sahada araçların %31'ini okunamaz kılıyordu (2026-10-27,
    /// <c>scripts/plate_size_sim.py</c>). Eşiği sitenin kendi ölçümünden seçin.</para>
    /// <para>Tespit ATILMAZ, yalnız OCR atlanır: motor atlanan tespiti <see cref="AnprEngine.LastSizeSkipped"/> ile
    /// bildirir (SDK işçisi onu aktif izleme, yön ve canlı kutu için kullanmaya devam eder) ve
    /// <see cref="AnprEngine.SizeDroppedCount"/> artar. Sınır, bölge dışı gözlem yuvaları dağıtılmadan ÖNCE uygulanır.
    /// Çift satır plakalar aynı mesafede ~0,62× dar görünür.</para>
    /// </summary>
    public int MinPlateWidthPx { get; set; } = 0;

    /// <summary>
    /// Okuma üst sınırı (mutlak): dedektör kutusu (dolgusuz) bu kadar px'ten genişse OCR çalıştırılmaz. 0 = kapalı.
    /// Kameraya çok yakın (kesilmiş/odak dışı) plakaları dışlamak içindir. Bkz. <see cref="MinPlateWidthPx"/>.
    /// </summary>
    public int MaxPlateWidthPx { get; set; } = 0;

    /// <summary>
    /// Mesafe kalibrasyonu (tek referans ölçümü). Doluysa her okumaya <see cref="PlateReading.EstimatedDistanceMeters"/>
    /// yazılır ve <see cref="MinReadDistanceMeters"/>/<see cref="MaxReadDistanceMeters"/> geçerli olur. <c>null</c> = kalibrasyon
    /// yok → mesafe hiçbir yerde sayı olarak üretilmez.
    /// </summary>
    public PlateDistanceCalibration? DistanceCalibration { get; set; }

    /// <summary>
    /// Kalibrasyonlu en yakın okuma mesafesi (metre). Daha yakın plakalar okunmaz. 0 = kapalı.
    /// İçeride, kare genişliğine göre bir piksel üst sınırına çevrilir (<see cref="MaxPlateWidthPx"/> ile birlikte
    /// en sıkı olan geçerlidir). Kalibrasyon yoksa ETKİSİZDİR (SDK doğrulaması bu yapılandırmayı reddeder).
    /// </summary>
    public double MinReadDistanceMeters { get; set; } = 0;

    /// <summary>
    /// Kalibrasyonlu en uzak okuma mesafesi (metre). Daha uzak plakalar okunmaz. 0 = kapalı.
    /// İçeride bir piksel alt sınırına çevrilir (<see cref="MinPlateWidthPx"/> ile birlikte en sıkı olan geçerlidir).
    /// </summary>
    public double MaxReadDistanceMeters { get; set; } = 0;

    /// <summary>
    /// Bu kare genişliği için ETKİN dedektör-kutusu genişlik sınırları (px, dolgusuz; ikisi de dahil). 0 = o yönde sınır yok.
    /// Piksel ve kalibrasyonlu mesafe sınırlarından EN SIKI olanı alınır: uzak sınır → alt px, yakın sınır → üst px.
    /// <para>Mesafe → px dönüşümünde kayan nokta artığı (ör. 120,000000001) sınırı bir px kaydırmasın diye küçük bir
    /// tolerans uygulanır. Yakın sınır en az 1 px'e çevrilir (0 "kapalı" anlamına geldiği için).</para>
    /// <para><see cref="MinPlateWidthRatio"/> bu hesaba girmez; o ayrı ve daha önce uygulanır.</para>
    /// </summary>
    public (int Min, int Max) ResolvePlateWidthLimits(int frameWidth)
    {
        const double Eps = 1e-6;
        int min = Math.Max(0, MinPlateWidthPx);
        int max = Math.Max(0, MaxPlateWidthPx);
        if (DistanceCalibration is { IsValid: true } cal && frameWidth > 0)
        {
            if (MaxReadDistanceMeters > 0)
                min = Math.Max(min, (int)Math.Ceiling(cal.WidthPxAt(MaxReadDistanceMeters, frameWidth) - Eps));
            if (MinReadDistanceMeters > 0)
            {
                int near = Math.Max(1, (int)Math.Floor(cal.WidthPxAt(MinReadDistanceMeters, frameWidth) + Eps));
                max = max > 0 ? Math.Min(max, near) : near;
            }
        }
        return (min, max);
    }

    /// <summary>
    /// Geometri kapısı: kırpıntının en/boy oranı bunun altındaysa tespit OKUNMAZ (OCR'a hiç girmez).
    /// Türk plakası fiziksel olarak yatıktır — tek satır ≈4.7:1, çift satır (motosiklet/ticari) ≈2:1;
    /// oran &lt;1 (kutu DİKEY) hiçbir plakada mümkün değildir, dedektörün hatalı/parçalı kutusudur ve
    /// OCR bunlardan uydurma plaka üretir (canlı örnek: 52x124 dikey şeritten "25 ZPV 80").
    /// <para>ÖLÇÜM (2026-07-27, 882 canlı roi olayı, kamera ANPR'ı referans): oran&lt;1.5 bandında
    /// doğrulanma %47.8 iken üst bantlarda ~%80. Eşik süpürmesi: &lt;1.0 → NET +41 (53 yanlış engellendi,
    /// 12 doğru kayıp); &lt;1.2 → +31; &lt;1.5 → +8; &lt;1.8 → −55. Bu yüzden varsayılan 1.0 (hem ölçüm
    /// optimumu hem fiziksel olarak tartışmasız). 0 = kapalı.</para>
    /// </summary>
    public float MinPlateAspectRatio { get; set; } = 1.0f;

    /// <summary>
    /// İlgi alanı (ROI) çokgeni, 0-1000 normalize "x1,y1;x2,y2;..." (Hikvision şerit koordinat ölçeği).
    /// Tespit kutusunun merkezi çokgen dışındaysa OKUNMAZ → kapsam kameraya hizalanır + CPU tasarrufu.
    /// Boş = kapalı (tüm kare okunur). Kamera başına ayrı verilmeli.
    /// </summary>
    public string RoiPolygon { get; set; } = "";

    /// <summary>
    /// OCR öncesi plaka kırpıntısını eğiklikten kurtar (deskew). Açılı plakalarda yardımcı olabilir
    /// ama açılı veride ÖLÇÜLMELİ; clean plakada gereksiz döndürme zarar verebilir. Varsayılan KAPALI.
    /// </summary>
    public bool DeskewPlates { get; set; } = false;

    /// <summary>
    /// ROI-kırpımlı tespit: ROI tanımlıysa dedektöre tam kare yerine ROI'nin sınırlayıcı dikdörtgeni
    /// (+%5 pay) verilir → letterbox gri-dolgu israfı geri kazanılır, plakalar dedektör girdisinde büyür
    /// (mevcut çıkış ROI'sinde ×1.35). ROI kare boyutunun ≥%90'ıysa kazanç yok → otomatik tam-kare yolu.
    /// Gözlem katmanı için tam kare FullFrameEveryN karede bir ayrıca taranır.
    /// </summary>
    public bool RoiCropDetector { get; set; } = true;

    /// <summary>ROI-kırpım modunda tam-kare gözlem taraması sıklığı (her N karede 1). Min 1.</summary>
    public int FullFrameEveryN { get; set; } = 3;

    /// <summary>
    /// Kalite kapısı (İkinci-Şans Şeridi Aşama 1): açıkken, bulanıklık varyansı QualityBlurMin
    /// altındaki YA DA karanlık/patlamış piksel oranı eşiği aşan kırpıntılar OCR'a girmez
    /// (oylamayı kirletmez). Eşikler eşik-taramasıyla seçilir; ölçüm kapısını geçmeden AÇILMAZ.
    /// </summary>
    public bool CropQualityGate { get; set; } = false;

    /// <summary>Bulanıklık alt eşiği (gri Laplacian varyansı; altı = bulanık, atılır). 0 = devre dışı.</summary>
    public float QualityBlurMin { get; set; } = 0f;

    /// <summary>Karanlık piksel (gri&lt;30) oranı üst eşiği; üstü atılır. 1 = devre dışı.</summary>
    public float QualityDarkMaxPct { get; set; } = 1f;

    /// <summary>Patlamış piksel (gri&gt;225) oranı üst eşiği; üstü atılır (IR patlaması). 1 = devre dışı.</summary>
    public float QualityBrightMaxPct { get; set; } = 1f;

    /// <summary>
    /// İkinci-Şans Şeridi Aşama 2 — WPOD rektifikasyonu: onaylı ama düşük güvenli izlerde, izin en iyi
    /// kırpıntıları perspektif-düzeltilip OCR'lanır ve oylamaya EK GÖZLEM olarak katılır.
    /// ÖLÇÜM (offline, dağılım-toplamı): açılı 61→67/86, temiz 341→359/416 — her dilimde kazanç.
    /// </summary>
    public bool EnhanceRectify { get; set; } = false;

    /// <summary>Zenginleştirme tetiği: iz uzlaşı OCR güveni bu eşiğin ALTINDAysa çalışır (ölçülen: 0.98).</summary>
    public float EnhanceTriggerOcr { get; set; } = 0.98f;

    /// <summary>İz başına zenginleştirme süre bütçesi (ms); aşımda kalan adımlar atlanır.</summary>
    public int EnhanceBudgetMs { get; set; } = 150;

    /// <summary>Rektifikasyon modeli yolu (yoksa/yüklenemezse aşama kendini kapatır). Varsayılan IWPOD-NET
    /// (MIT); eski WPOD-NET (GPL-3.0) de verilebilir — tür çıktı kanal sayısından anlaşılır.</summary>
    public string WpodModelPath { get; set; } = "models/iwpod_net.onnx";

    /// <summary>Aşama 4 — FSRCNN ×2 SR: KÜÇÜK kırpıntıda (genişlik &lt; SrMaxWidthPx) ek gözlem.
    /// ÖLÇÜM: küçük dilim 28→30/41 (+2/−0); 0.8 ms/çağrı.</summary>
    public bool EnhanceSr { get; set; } = false;

    /// <summary>SR tetiği: kırpıntı genişliği bu değerin altındaysa uygulanır.</summary>
    public int SrMaxWidthPx { get; set; } = 100;

    /// <summary>FSRCNN modeli yolu.</summary>
    public string SrModelPath { get; set; } = "models/fsrcnn_x2.onnx";

    /// <summary>
    /// Gözlem OCR bütçesi: ROI DIŞI tespitlerden kare başına en fazla bu kadarına (en büyükten başlayarak)
    /// OCR uygulanır ve okumalar InZone=false etiketiyle döner. Kameranın kapsam-dışı (cadde/uzak)
    /// okumalarıyla panelde eşleşebilmek için; bariyer kararına girmez. 0 = kapalı (eski davranış).
    /// ROI tanımsızken etkisizdir (her tespit zaten bölge içi sayılır).
    /// </summary>
    public int ObservationMaxPerFrame { get; set; } = 0;

    /// <summary>
    /// Aynı model dosyasını aynı ayarlarla kullanan motorlar (kameralar) TEK ONNX oturumunu paylaşır
    /// (varsayılan açık). Kamera başına ~135 MB (dört modelin ağırlıkları + çalışma alanı) ikinci ve sonraki
    /// kameralarda harcanmaz. Sonuçlar paylaşımsız yolla aynıdır; <c>InferenceSession.Run</c> eşzamanlı çağrıya
    /// açıktır. Kapatmak yalnız teşhis/karşılaştırma içindir.
    /// </summary>
    public bool ShareOnnxSessions { get; set; } = true;

    /// <summary>
    /// Oturum paylaşım anahtarı: aynı anahtara sahip seçenekler aynı <see cref="CreateSessionOptions"/> çıktısını
    /// üretir. <see cref="ShareOnnxSessions"/> kapalıysa null (paylaşım yok).
    /// </summary>
    internal string? SessionShareKey => !ShareOnnxSessions ? null
        : $"t{Math.Max(1, OnnxIntraOpThreads)}|s{(OnnxAllowSpinning ? 1 : 0)}|a{(OnnxCpuMemArena ? 1 : 0)}|m{(OnnxMemoryPattern ? 1 : 0)}";

    /// <summary>
    /// CPU için ONNX Runtime SessionOptions üretir:
    /// CPU EP (varsayılan), IntraOp=OnnxIntraOpThreads, InterOp=1, sequential, ORT_ENABLE_ALL.
    /// Hiçbir yerde CUDA/GPU sağlayıcısı eklenmez.
    /// </summary>
    public SessionOptions CreateSessionOptions()
    {
        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Max(1, OnnxIntraOpThreads),
            InterOpNumThreads = 1,
            EnableCpuMemArena = OnnxCpuMemArena,
            EnableMemoryPattern = OnnxMemoryPattern,
        };
        // Boşta CPU tasarrufu: spin kapalıyken thread'ler işi kuyruktan bekler (boşta ~0 CPU).
        so.AddSessionConfigEntry("session.intra_op.allow_spinning", OnnxAllowSpinning ? "1" : "0");
        return so;
    }
}
