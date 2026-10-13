namespace Anpr.Sdk.Configuration;

/// <summary>appsettings.json "Anpr" bölümünün karşılığı.</summary>
public sealed class AnprServiceOptions
{
    public const string SectionName = "Anpr";

    /// <summary>
    /// Aktif (araç varken) saniyede işlenecek kare sayısı. ffmpeg de bu hızda kare üretir.
    /// ÖLÇÜM (2026-07-02, kamera karşılaştırması): fps artırmak recall'i KURTARMIYOR (kaçan araçlar
    /// kaynak-yakalama/OCR limiti, işlem fps'i değil) ama gürültüyü SERT artırıyor (precision 57→28%) ve
    /// zayıf CPU'yu boğuyor. Bariyerde araç 3-8 sn durduğu için 2 fps bile 6-16 kare = yeterli oylama.
    /// → Zayıf ofis PC için varsayılan 2 (güvenli); güçlü PC'de (AVX2/4-çekirdek) ölçerek 3-4'e çıkılabilir.
    /// </summary>
    public double Fps { get; set; } = 2;

    /// <summary>Boştayken (araç yokken) saniyede işlenecek kare. CPU tasarrufu için düşük tutulur.</summary>
    public double IdleFps { get; set; } = 1;

    /// <summary>Bir tespitten sonra bu kadar saniye "aktif" hızda işlenir (araç geçişini tam yakalamak için).</summary>
    public double ActiveHoldSeconds { get; set; } = 4;

    /// <summary>
    /// HAREKET KAPISI (Faz 2 — düşük donanım). Boş sahnede dedektör koşturmamak için ham karede seyrek ızgara
    /// farkına bakar (bkz. <c>MotionGate</c>). <see cref="MotionGateMode.Shadow"/> (varsayılan): karar yalnız
    /// SAYILIR, uygulanmaz — <c>/health</c>'teki <c>gateMissedDetections</c> sıfır kalıyorsa <see cref="MotionGateMode.On"/>
    /// yapılabilir. <see cref="MotionGateMode.On"/>: hareket yokken kare atlanır (aktif izleme süresi ve zorunlu
    /// periyot hariç). Yalnız BMP/BGRA kare yolunda etkilidir; JPEG karede kapı hep "işle" der.
    /// </summary>
    public MotionGateMode MotionGate { get; set; } = MotionGateMode.Shadow;
    /// <summary>Hareket kapısı: bir örnek noktanın "değişti" sayılması için gereken parlaklık farkı (0–255).</summary>
    public int MotionGatePixelDelta { get; set; } = 20;
    /// <summary>Hareket kapısı: "hareket var" için değişen nokta payı eşiği (0–1). 0,01 = noktaların %1'i.</summary>
    public double MotionGateChangedFraction { get; set; } = 0.01;
    /// <summary>Hareket kapısı: kapı ne derse desin bu periyotla bir kare MUTLAKA işlenir (emniyet kemeri, sn).</summary>
    public double MotionGateForcedIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// <see cref="MotionGateMode.On"/>: hareket bittikten sonra bu süre boyunca kareler işlenmeye devam eder
    /// (aracın son kareleri ve kısa duraksamalar kaçmasın). Varsayılan 1 sn.
    /// </summary>
    public double MotionGateHoldSeconds { get; set; } = 1.0;
    /// <summary>Hareket kapısı ızgarası (sütun × satır). 64×36 = 2304 örnek nokta.</summary>
    public int MotionGateGridColumns { get; set; } = 64;
    public int MotionGateGridRows { get; set; } = 36;

    /// <summary>Tetik bölgesi: OCR yalnızca plaka genişliği kare genişliğinin bu oranından büyükse. 0 = kapalı.</summary>
    public float MinPlateWidthRatio { get; set; } = 0.0f;

    /// <summary>
    /// Geometri kapısı: kırpıntı en/boy oranı bunun altındaysa OCR çalıştırılmaz (dikey/parçalı dedektör
    /// kutusundan uydurma plaka üretilmesini keser; TR plakası ≈4.7:1, çift satır ≈2:1 → oran&lt;1 imkânsız).
    /// Canlı ölçüm: 1.0 eşiği NET +41 doğru olay (bkz. <c>AnprOptions.MinPlateAspectRatio</c>). 0 = kapalı.
    /// </summary>
    public float MinPlateAspectRatio { get; set; } = 1.0f;

    /// <summary>Aynı plaka aynı kamerada bu süre içinde tekrar görülürse TEK olay sayılır.</summary>
    public int DedupWindowSeconds { get; set; } = 8;

    /// <summary>OCR'ın çalıştırılacağı minimum dedektör güven skoru.</summary>
    public float MinConfidence { get; set; } = 0.5f;

    /// <summary>ONNX Runtime IntraOp thread sayısı (zayıf CPU için 2).</summary>
    public int OnnxIntraOpThreads { get; set; } = 2;

    /// <summary>
    /// ONNX thread'leri boşta dönerek mi beklesin? Zayıf PC'de false = boşta CPU ~0 (uyarlanabilir fps ile
    /// çoğu zaman boşta); kare-başı gecikme birkaç ms artabilir. Ölçümle karar verin.
    /// </summary>
    public bool OnnxAllowSpinning { get; set; } = true;

    // Varsayılanlar SDK ile gelen modellere işaret eder (paket, çıktı dizinine models/ kopyalar) —
    // PMS tarafında sıfır model-yapılandırmasıyla çalışır. Servis appsettings.json ile ezmeye devam eder.
    public string DetectorModelPath { get; set; } = "models/yolo-v9-t-384-license-plates-end2end.onnx";
    public string OcrModelPath { get; set; } = "models/cct_xs_v2_erz.onnx";
    public string OcrConfigPath { get; set; } = "models/cct_xs_v2_erz_plate_config.yaml";

    /// <summary>Dedektör ham skor eşiği.</summary>
    public float DetectorScoreThreshold { get; set; } = 0.25f;

    /// <summary>Plaka kırpılırken her kenara eklenecek pay oranı. Bu OCR'ın gördüğü kırpıntıdır —
    /// tanıma doğruluğu için ayarlanmıştır, kanıt görseli için <see cref="EvidencePaddingRatio"/> kullanılır.
    /// (2026-07-27 A/B: 0.10/0.15/0.20/0.30 ölçüldü, hiçbiri anlamlı kazanç vermedi → 0.06 korunur.)</summary>
    public float CropPaddingRatio { get; set; } = 0.06f;

    /// <summary>
    /// KANIT görselinin kadraj payı — plaka kutusunun her kenarına kutu boyutunun bu katı kadar eklenir
    /// (1.0 = 3 kat geniş çerçeve) ve kırpıntı OCR kırpıntısından değil TAM KAREDEN yeniden üretilir.
    /// <para>NEDEN AYRI: OCR kırpıntısı bilinçli olarak dardır (<see cref="CropPaddingRatio"/>=0.06) ve
    /// insan doğrulaması için kötü bir kanıttır. ÖLÇÜM (2026-07-27, 3031 canlı kanıt görseli): kırpıntıların
    /// %60.7'sinde plaka en az bir kenara DEĞİYOR (sağ kenar %46.7; sağdaki boşluk medyan 5 px, %42.8'inde
    /// TAM SIFIR), %30.7'sinde plaka hem soldan hem sağdan kesiliyor — son karakter fiziksel olarak kadraj
    /// dışında kalabiliyor. Tam karede yer bol: 40/40 örnekte 3x kadraj kareye tamamen sığıyor (etrafta
    /// medyan 1246/1330/698/766 px boşluk). Kazanç: medyan 150x68 → 453x214 px (9.3x piksel), yeniden
    /// boyutlandırma YOK (gerçek piksel). Disk maliyeti ihmal edilebilir: kırpıntılar toplam kanıt diskinin
    /// yalnız %0.49'u (tam kareler %99.5).</para>
    /// 0 = kapalı (eski davranış: OCR kırpıntısı kanıt olarak yazılır).
    /// </summary>
    public float EvidencePaddingRatio { get; set; } = 1.0f;

    /// <summary>Kanıt kırpıntısının JPEG kalitesi. 90 ve üstü libjpeg'i 4:4:4 kroma'ya geçirir — küçük
    /// (~150 px) plaka görüntüsünde 4:2:0 renk yarılaması TR mavi bandını ve renk ipuçlarını eziyordu.
    /// Ölçüm: 153x52 kırpıntıda q85/4:2:0 = 2.4 KB → q92/4:4:4 = 3.4 KB (q95 = 4.3 KB, görsel kazanç yok).</summary>
    public int EvidenceJpegQuality { get; set; } = 92;

    /// <summary>TR plaka doğrulaması uygulansın mı.</summary>
    public bool ApplyTurkishValidation { get; set; } = true;

    /// <summary>
    /// Bir olayın yayınlanması için gereken minimum birleşik güven (dedektör*OCR).
    /// TR doğrulaması zaten geçmiş olmalı; bu ek bir güven bariyeridir.
    /// </summary>
    public float EmitMinConfidence { get; set; } = 0.0f;

    /// <summary>
    /// Yayınlamak için gereken minimum OCR güveni. OCR güveni, METNİN doğruluğuyla doğrudan
    /// ilintilidir (doğru okumalar ~%92-100, bulanık/kenar okumalar daha düşük). Bariyere belirsiz
    /// okuma gitmesin diye ham/kötü kareleri eler. Daha katı için 0.85+, her şeyi loglamak için 0.
    /// </summary>
    public float EmitMinOcrConfidence { get; set; } = 0.45f;

    /// <summary>
    /// Çok kareli onay: bir plaka, onay penceresi (DedupWindowSeconds) içinde EN AZ bu kadar
    /// kez okunmadan yayınlanmaz. Tek seferlik OCR yanlışlarını (ör. "25 CGM 22" vs "25 CGM 261")
    /// eler. Yayınlanırken o aracın en yüksek güvenli okuması seçilir. 1 = onay kapalı (her okuma).
    /// Bariyerde araç birkaç saniye kaldığı için 2 güvenli bir varsayılandır.
    /// </summary>
    public int MinConfirmations { get; set; } = 2;

    /// <summary>
    /// Onaya ulaşmayan (tek-kare hızlı) araç, son görülmesinden bu kadar saniye sonra yine de yayınlanır.
    /// YALNIZ 1-kare izleri etkiler (2+ kare zaten onayla ANINDA emit) → hızlı-araç bariyer gecikmesi.
    /// Ölçüm (diag4, 26 araç, iki kamera): settle 1..3s emit sonucunu DEĞİŞTİRMİYOR (saf zamanlama knob'u).
    /// 2 = hızlı araçta 3s yerine 2s bariyer + geç gelen 2. kareye 2s tolerans (konservatif).
    /// </summary>
    public int SettleWindowSeconds { get; set; } = 2;

    /// <summary>
    /// Karakter-bazlı çok-kareli oylama. Açıksa aynı aracın tüm karelerinde slot-bazlı olasılıklar
    /// toplanıp pozisyon-başına en olası karakter seçilir (doğruluğu artırır). Kapalıysa en yüksek
    /// güvenli tüm-plaka seçilir.
    /// </summary>
    public bool CharacterVoting { get; set; } = true;

    /// <summary>
    /// TR gramer-kısıtlı çözümleme: biriken slot dağılımını geçerli Türk plaka yapılarına göre çözer
    /// (8/B, 0/O, 1/I gibi karışıklıkları pozisyona göre çözer). CharacterVoting ile birlikte çalışır.
    /// </summary>
    public bool ConstrainedDecoding { get; set; } = true;

    /// <summary>
    /// Kimlik anahtarında il'i yok say (harf|rakam) → il ilk-hanesi yanlış okunan aynı araç tek olaya
    /// iner, il oylamayla belirlenir. Çift/yanlış-il emitlerini azaltır. false = eski (il|harf).
    /// </summary>
    public bool MergeAcrossProvince { get; set; } = false;

    /// <summary>
    /// Bulanık zamansal kümeleme: >0 ise aktif penceredeki okumalar edit-mesafesi ≤ bu değerle birleşir →
    /// aynı aracın geçişindeki il/rakam/harf yanlış-okuma varyantları TEK olaya çöker (kamera-ANPR davranışı).
    /// diag'da emit 18→8/gürültü 11→1; live1h'te yanlış-emit -%24 (recall -1 araç). Bariyerde recall öncelikli
    /// olduğu için varsayılan 1 (güvenli); daha temiz çıktı için 2, kapatmak için 0.
    /// </summary>
    public int FuzzyMergeDistance { get; set; } = 1;

    /// <summary>
    /// Güven-kapılı erken emit: onaylı izin uzlaşı OCR güveni bu eşiğin altındaysa hemen yayınlama,
    /// daha iyi okuma bekle (en geç SettleWindow'da yayınlanır). Uzaktan kırpık okumayla erken/yanlış
    /// emit'i keser; yüksek güvenli plakaya gecikme eklemez. 0 = kapalı. Ölçümle karar verilir.
    /// </summary>
    public float EarlyEmitMinOcr { get; set; } = 0f;

    /// <summary>OCR öncesi plaka deskew (eğiklik düzeltme). Açılı veride ÖLÇÜLMELİ. Varsayılan kapalı.</summary>
    public bool DeskewPlates { get; set; } = false;

    /// <summary>ROI-kırpımlı tespit: dedektöre ROI sınırlayıcı dikdörtgeni verilir (plaka pikseli büyür). </summary>
    public bool RoiCropDetector { get; set; } = true;

    /// <summary>ROI-kırpım modunda tam-kare gözlem taraması sıklığı (her N karede 1).</summary>
    public int FullFrameEveryN { get; set; } = 3;

    /// <summary>Kalite kapısı: bulanık/aşırı karanlık/patlamış kırpıntı OCR'a girmez (oylamayı kirletmez).
    /// ÖLÇÜM (2026-07-10): blur=600 eşiğiyle diag4 birebir korunup gürültü 11→9; live1h recall sabit.</summary>
    public bool CropQualityGate { get; set; } = true;
    public float QualityBlurMin { get; set; } = 600f;
    public float QualityDarkMaxPct { get; set; } = 0.85f;
    public float QualityBrightMaxPct { get; set; } = 0.80f;

    /// <summary>WPOD rektifikasyonu (İkinci-Şans Aşama 2). ÖLÇÜM: açılı 61→67, temiz 341→359 (oylama-birleşimli).</summary>
    public bool EnhanceRectify { get; set; } = true;
    public float EnhanceTriggerOcr { get; set; } = 0.98f;
    public int EnhanceBudgetMs { get; set; } = 150;
    public string WpodModelPath { get; set; } = "models/wpod_lpd.onnx";

    /// <summary>FSRCNN x2 SR (küçük kırpıntı). ÖLÇÜM: küçük dilim 28→30/41 (+2/−0), 0.8 ms.</summary>
    public bool EnhanceSr { get; set; } = true;
    public int SrMaxWidthPx { get; set; } = 100;
    public string SrModelPath { get; set; } = "models/fsrcnn_x2.onnx";

    /// <summary>Uzamsal iz birleştirme IoU eşiği (aynı fiziksel plaka, ağır varyantlar tek ize). 0 = kapalı.</summary>
    public float SpatialMergeIou { get; set; } = 0.35f;

    /// <summary>
    /// Uzamsal iz birleştirmede izin son görülmesiyle yeni okuma arasındaki azami süre.
    /// <para>Bu değer FPS'E BAĞIMLIDIR: 1,5 sn, 5 fps'te ~7 kare ama 3 fps'te ~4,5 karedir. Tespit
    /// boşluğu (araç ilerlerken birkaç karede plaka bulunamaması) bu pencereyi aşarsa iz KOPAR ve aynı
    /// araç ikinci kez — çoğu zaman yanlış bir plakayla — yayınlanabilir (2026-07-28 canlı vakaları).
    /// Kaynak fps düşürüldüğünde bu değerin de gözden geçirilmesi gerekir; eskiden hiç ayarlanamıyordu
    /// (PlateConfirmerOptions varsayılanında sabitti).</para>
    /// Uzun tutmak ters etkilidir: kapıda sıraya giren FARKLI araçlar birbirine zincirlenebilir.
    /// </summary>
    public double SpatialMergeMaxGapSeconds { get; set; } = 1.5;

    /// <summary>Uzamsal birleşmede tolere edilen azami metin mesafesi (sağlamlık sınırı).</summary>
    public int SpatialMergeMaxDistance { get; set; } = 4;

    /// <summary>SKIP_CHAR seviyesi: hayalet son hane bastırma (TrConstrainedDecoder). 0 = kapalı.
    /// ÖLÇÜM (diag4): 0.5 bir birebiri kaybettirdi (agresif); 0.3 birebiri koruyup mekanizmayı açık tutuyor.</summary>
    public float SkipCharLevel { get; set; } = 0.3f;

    /// <summary>Uzlaşı emitinde taşınacak aday sayısı (top-K). 0 = kapalı.</summary>
    public int CandidateCount { get; set; } = 5;

    /// <summary>
    /// Yalnız YAKLAŞAN aracı yayınla: DirectionTracker plaka GENİŞLİĞİ eğilimini izler; iz belirgin
    /// küçülüyorsa (uzaklaşıyor) roi olayı bastırılır. VARSAYILAN KAPALI: 4 canlı koşuda (2026-07-14)
    /// bu geometride KANITLI ETKİSİZ — plaka ancak araç kapıya gelip DURUNCA (boyut donmuş) net
    /// tespit edildiğinden trend hep ~1.0 çıkıyor; ayırt edici boyut değişimi araç uzaktayken (tespit
    /// güvenilmez) oluyor. Doğru sinyal muhtemelen HAREKET-YOLU (konum yönü) — kameralar dönünce
    /// denenecek. Bu bayrak açılsa da güvenli (0 yanlış-bastırma ölçüldü) ama pratikte iş yapmaz.
    /// </summary>
    public bool RequireApproaching { get; set; } = false;

    /// <summary>Uzaklaşıyor eşiği: son-yarı-max / erken-yarı-max plaka GENİŞLİK oranı bu değerin ALTINDAYSA
    /// "uzaklaşıyor" sayılır. Yarı-başına-max mülayim küçülmeyi yumuşattığı için 0.85 (belirgin küçülmeyi
    /// yakalar, yaklaşan/sabit izlere ~1.0'a güvenli marj bırakır). trend=-1 (kararsız) geçer. Ölçümle ayarla.</summary>
    public float ApproachDepartRatio { get; set; } = 0.85f;

    /// <summary>Yön kararı için gereken asgari genişlik örneği (yarı-başına-max sağlamlığı için ≥4).
    /// 2 fps'te araç ~2 karede emit olduğundan, filtre açıkken emit bu kadar kere görülene dek TUTULUR
    /// (settle'da her hâlükârda yayınlanır) — böylece yön tek/iki kareye değil yeterli kareye dayanır.</summary>
    public int ApproachMinSamples { get; set; } = 4;

    /// <summary>Yön kararı için izin kapsaması gereken asgari süre (sn). Altındaysa karar verilmez (geçer).</summary>
    public double ApproachMinSpanSeconds { get; set; } = 0.6;

    /// <summary>
    /// Bilinen plaka whitelist'i (ör. resmi/protokol araçları). Emit anında, okuma bu listeden bir
    /// plakaya edit-mesafesi ≤ SnapMaxLev yakınsa ona DÜZELTİLİR ("snap"). Siyah resmi plaka gibi
    /// FREKANSLI araçların sistematik yanlış-okumasını düzeltir (offline ölçüm: lev≤1 siyah +27pp,
    /// beyazda 0 yanlış-snap). Boş = etkisiz. Güvenli: küçük, küratörlü liste + lev≤1.
    /// </summary>
    public List<string> KnownPlates { get; set; } = new();

    /// <summary>
    /// KÜRATÖRLÜ TAM-DİZGİ düzeltme haritası: <c>{"yanlış okuma": "gerçek plaka"}</c>. Emit anında
    /// TAM eşleşme ile uygulanır, <b>güven kapısı YOKTUR</b>. Varsayılan BOŞ (davranış değişmez).
    /// <para><b>Ne zaman kullanılır:</b> yalnız kanıt görselinden gözle doğrulanmış, SİSTEMATİK ve
    /// yüksek güvenli yanlış okumalar için. Güven skoru, kamera çaprazı ve <see cref="KnownPlates"/>
    /// bulanık snap'i bu hata sınıfını yapısal olarak göremez — bu yüzden ayrı bir mekanizma var.</para>
    /// <para>🔴 <b>UYARI ÖRNEĞİ (eskiden BU MADDENİN OLUMLU ÖRNEĞİYDİ — yönü TERSTİ):</b> burada
    /// <c>25 LMY 166</c> plakalı bir aracın 39 kez <c>25LMV166</c> okunduğu yazıyordu. **2026-08-05'te
    /// kanıtla saptandı ki gerçek plaka <c>25 LMV 166</c>'dir** — dört ayrı geçişin kanıt görseli de
    /// böyle okunuyor, kameranın bağımsız kaydı <c>25LMV166</c>'yi 124 kez / <c>25LMY166</c>'yi 1 kez
    /// görmüş, ve gözle doğrulanmış hükümler de bunu söylüyor. Bu gerekçeyle kurulan düzeltme
    /// <b>DOĞRU okumaları YANLIŞA çevirdi</b> (ölçülen zarar: 7 bozulmuş olay). Harita sonradan
    /// boşaltıldı; örnek burada <b>uyarı olarak</b> bırakıldı.</para>
    /// <para>📌 <b>ÇIKARILACAK DERS — "N kez aynı şekilde okundu" bir DOĞRULUK KANITI DEĞİLDİR.</b>
    /// Tekrar eden bir yanlış okuma da tam olarak bunu üretir; sıklık, sonucun aşağısındadır. Yön
    /// ancak <b>bağımsız</b> bir kaynakla belirlenir: kanıt görseli ya da karşı kameranın kaydı.
    /// ⚠️ Yukarıdaki "yalnız kanıt görselinden gözle doğrulanmış" koşulu boşuna yazılmamıştır —
    /// bu maddenin kendi örneği o koşulu çiğnediği için yanlış yöne kurulmuştu.</para>
    /// <para><b>Neden güven kapısı yok:</b> <see cref="SnapMaxOcrConfidence"/> kapısı BULANIK eşleşmenin
    /// korumasıdır (lev1 komşuya kayma riski). Tam-dizgi eşleşme o riski taşımaz.</para>
    /// <para>⚠️ <b>Bu harita bir okumayı KANONLAŞTIRIR.</b> Yanlış bir girdi doğru okumaları kalıcı
    /// bozar — aynı hata bu projede <see cref="KnownPlates"/> ile yaşandı. Geçersiz hedef, whitelist
    /// çelişkisi, kendine eşleme ve zincir girdileri REDDEDİLİR ve başlangıçta UYARI olarak loglanır.</para>
    /// </summary>
    public Dictionary<string, string> PlateCorrections { get; set; } = new();

    /// <summary>
    /// ÇAPRAZ-KAMERA KESİLME KAPISI (sn). Bir okuma, bu pencere içinde <b>herhangi bir kameranın</b>
    /// yayınladığı tam 1 karakter daha uzun ve DAHA GÜVENLİ bir okumanın tam ön-eki ise
    /// <see cref="Anpr.Sdk.Events.PlateEvent.LowConfidence"/> = true yapılır → bariyeri OTOMATİK açmaz,
    /// kararı operatör verir. 0 = kapalı.
    /// <para><b>Neden (1766 canlı roi olayı):</b> worker-içi ön-ek muafiyeti her kamera ayrı worker
    /// olduğu için çapraz vakayı göremiyordu. Ölçüm: <b>5 vaka, 5'i de bariyeri kesik plakayla
    /// otomatik açtı</b> (kullanıcının ilk bildirdiği <c>34YR992</c> dahil). Yanlış-pozitif riski
    /// ölçüldü ve SIFIR: 11 ön-ek çiftinin 11'inde de kısa taraf tek seferlik, uzun taraf yerleşik.</para>
    /// <para><b>Neden bastırma değil operatöre düşürme:</b> giriş ve çıkış AYRI kapılardır; giriş
    /// olayını bastırmak aracın giriş kaydını kaybettirirdi. Olay yayınlanır, yalnız otomatik açmaz.</para>
    /// <para>Pencere ölçümü: ±8 sn 3 vaka · ±15 sn 5 · ±30 sn yine 5 → genişletmek yeni vaka
    /// getirmez, yalnız yanlış-eşleşme riskini büyütür.</para>
    /// <para>🔄 <b>YUKARIDAKİ SAYILAR 2,5× ÖRNEKLEMDE YENİDEN ÖLÇÜLDÜ (2026-08-05, 4464 roi olayı) —
    /// SAYILAR DÜŞTÜ, KARAR DEĞİŞMEDİ.</b> <b>Nedensel</b> tarama (kapının gerçekte yapabildiği: yalnız
    /// olaydan ÖNCE yayınlanmış okumalar referans olabilir): <b>±8 sn 1 · ±15 sn 3 · ±30 sn 3.</b>
    /// Yeniden kurma canlı kapıya karşı doğrulandı (v4 döneminde 1 bayrak; kurma 1/1 eşleşti, 0 kaçak),
    /// dolayısıyla düşük sayı yöntem hatası değil.</para>
    /// <para>⚠️ <b>Eski sayılar muhtemelen ÇİFT YÖNLÜ taramadan geliyor</b> — bu projede §P.4 2026-08-01'de
    /// "çift yönlü tarama kapsamayı 2–4× şişirir" diye belgelenen hata sınıfı. Bugünkü veride çift yönlü
    /// tarama ±8→6 · ±15→10 · ±30→10 veriyor; eski 3/5/5 rakamları 1766 olayda buna oranla tutarlı.
    /// (Özgün yöntemi kanıtlayamam, çıkarım yapıyorum — bu yüzden eski satır SİLİNMEDİ.)</para>
    /// <para>✅ <b>DESTEKLEDİĞİ İKİ KARAR AYAKTA:</b> ① pencereyi genişletmek yeni vaka getirmiyor
    /// (±15 ile ±30 aynı: 3 = 3) → varsayılan 15 kalır; ② yanlış-pozitif riski hâlâ sıfır gözlendi —
    /// 3 ön-ek çiftinin hiçbirinde kısa taraf "yerleşik" değil (bizde ≥2 ya da kamerada ≥3 kaydı yok).
    /// n küçüldüğü için iddia zayıfladı ama yönü değişmedi.</para>
    /// </summary>
    public double CrossCameraPrefixWindowSeconds { get; set; } = 15.0;

    /// <summary>
    /// Çapraz-kamera <b>İKAME</b> kapısının penceresi (saniye). 0 = kapalı. Varsayılan 15.
    /// Bir okuma, pencere içinde yayınlanmış AYNI UZUNLUKTA + tam 1 karakter farklı + DAHA GÜVENLİ bir
    /// okumanın bozulmuş hâliyse olay yayınlanır ama <c>LowConfidence=true</c> olur (plaka DEĞİŞMEZ).
    /// <para><b>Kesilme kapısından AYRI knob olmasının sebebi: ölçülmüş isabetleri farklı.</b> Kesilme
    /// 8/8 (karşı örnek YOK), ikame <b>17/19 = %89</b> (2 karşı örnek: <c>34YF7170</c>, <c>25GE891</c>).
    /// Sahada gürültülü bulunursa bu kapı tek başına kapatılabilmeli, sıfır-karşı-örnekli kesilme
    /// kapısı kaybolmamalı.</para>
    /// <para><b>Ölçüm (2505 canlı roi olayı):</b> 30 çapraz çift, <b>26'sı bariyeri otomatik açmıştı</b>.
    /// Pencere: ±8 sn 20 çift/%85 · ±15 sn 30/%89 · ±30 sn 34/%90. lev≤2 denendi ve REDDEDİLDİ
    /// (62 çift, isabet %84) — bu sahada yerleşik araçların %17'sinin lev-1 komşusu da GERÇEK araçtır.</para>
    /// <para>🔄 <b>YENİDEN ÖLÇÜLDÜ (2026-08-05, 4464 roi olayı). NEDENSEL tarama: ±8 sn 22 · ±15 sn 35 ·
    /// ±30 sn 42 çift; ±15'te 28/35'i bariyeri otomatik açmış.</b> Yeniden kurma canlı kapıya karşı
    /// doğrulandı (v4'te 14 bayrak; kurma 14/14, 0 kaçak).</para>
    /// <para>⚠️ <b>ESKİ SAYILAR ÇİFT YÖNLÜ TARAMADAN GELİYOR — birim-olay oranı bunu ele veriyor:</b>
    /// yorumun oranı <b>1,20 çift/100 olay</b>; bugünkü <i>nedensel</i> oran 0,78; bugünkü <i>çift yönlü</i>
    /// oran <b>1,21</b> — yorumla birebir. Aynı imza kesilme kapısında da çıktı, yani bu iki bağımsız
    /// ölçüm de §P.4 2026-08-01'de belgelenen "çift yönlü tarama 2–4× şişirir" hatasına ait.
    /// Eski satır SİLİNMEDİ; ölçüm silinmez, üstüne yazılır.</para>
    /// <para>✅ <b>DESTEKLEDİĞİ KARARLAR AYAKTA:</b> pencereyi büyütmek burada (kesilme kapısının aksine)
    /// yeni vaka GETİRİYOR (±15→35, ±30→42) ama karşılığında yanlış-eşleşme riski artar; varsayılan 15
    /// bilinçli olarak korunur. Otomatik-açma payı da benzer (%80 vs eski %87).</para>
    /// <para><b>Neden değer:</b> bu sınıf, bir hatanın BAŞKA BİR GERÇEK ARACIN plakasına düşebildiği
    /// tek doğrulanmış yoldur (<c>25MT297</c> ← <c>25MT299</c>); orada plaka geçerli, whitelist'te ve
    /// yüksek güvenli olduğu için gramer/snap/güven korumalarının HİÇBİRİ göremez.</para>
    /// </summary>
    public double CrossCameraVariantWindowSeconds { get; set; } = 15.0;

    /// <summary>Snap için azami edit-mesafesi (boşluksuz plaka). 1 = varsayılan; 2 = daha çok düzeltir
    /// (siyah +45pp) ama yanlış-snap riski artar. 0 = snap kapalı.
    /// <para>🔴 <b>ESKİ YORUM "1 = güvenli (0 yanlış-snap ölçüldü)" DİYORDU — BU İDDİA ÜRETİMDE ÇÜRÜDÜ
    /// (2026-08-04).</b> Paket varsayılanlarıyla (<c>SnapMaxLev=1</c>, <c>SnapToFrequent=false</c>)
    /// çalışan sahada, gözle doğrulanmış <b>100 snap olayının 25'i YANLIŞ</b> snap'ti (75'i doğru
    /// düzeltme). İddia yanlış değil, <b>KAPSAMI dardı</b>: küçük ve küratörlü bir whitelist'te
    /// doğruydu — bu sahadaki liste <b>463 girdi</b>.</para>
    /// <para>🔑 <b>ASIL DERS: snap güvenliği bu AYARIN değeri değil, WHITELIST'İN YAPISIdır.</b>
    /// Ölçülen: listenin %14'ünün liste-içi lev≤1 komşusu var; daha kritiği, <b>%5'inin komşusu
    /// listede YOK ama sahada GERÇEK</b> — liste o aracı tanımadığı için okumasını komşusuna çeker ve
    /// bir gerçek aracı BAŞKA bir gerçek araca çevirir (ücret yanlış aboneye, denetim izi geçerli
    /// görünür → sessiz hata). Liste ortalaması yanıltıcıdır: fiilen kullanılan hedeflerde belirsizlik
    /// <b>%24</b>, snap edilen OLAYLARDA <b>%35</b> — çok geçen plakalar belirsiz kümede yoğunlaşıyor.
    /// ⚠️ Bu iki oran 2026-08-05'te DÜZELTİLDİ (%28/%40 idi): "sahada gerçek plaka" ölçütü HAM kamera
    /// kaydıydı, oysa kamera aynı aracı saniye-altı patlamalarla defalarca raporluyor (7667 ham →
    /// 5014 GEÇİŞ, %53 fazlalık). Ham saymak 39 plakayı fazladan "yerleşik" gösteriyordu (25'i tek
    /// geçiş). Ölçüt GEÇİŞ bazına çevrildi; oranlar 4–5 puan düştü, sonuç değişmedi.</para>
    /// <para>⚠️ Risk liste boyutuyla KARESEL büyür (n(n-1)/2 çift). Kendi sahanız için
    /// <c>scripts/audit_whitelist.py</c> ile ölçün; çözüm girdiyi silmek değil, <b>eksik aracı listeye
    /// eklemek</b> ya da o girdide snap'i kapatmaktır.</para></summary>
    public int SnapMaxLev { get; set; } = 1;

    /// <summary>Manuel whitelist'e EK olarak, otomatik öğrenilen MÜDAVİM plakalara da snap et. Müdavim
    /// kaydı yanlış-okumalarla kirlenebileceğinden varsayılan KAPALI (canlı doğrulama sonrası açılır).</summary>
    public bool SnapToFrequent { get; set; } = false;

    /// <summary>Güven-kapısı: yalnız OCR güveni bu değerin ALTINDA olan okumayı snap et. Yüksek-güvenli
    /// bir okuma whitelist plakasına yakın olsa bile GERÇEK farklı bir plaka olabilir (yanlış-snap riski).
    /// **0.99 varsayılan (2026-07-23 gate-sweep optimum):** model ≥0.99 okumaları DOĞRU (snap'leme),
    /// sistematik yanlış-okumalar 0.99'un ALTINDA; 0.99→1.0 uçurumu (395-plaka whitelist'te yanlış-snap 1→11).
    /// val+night: 0.99 → +9.3pp, %0.16 yanlış-snap. Küçük manuel whitelist için 1.0 da güvenli olabilir
    /// (az komşu) ama 0.99 her ölçekte güvenli. Daha düşürmek (0.90) yüksek-güvenli okumaları daha korur.</summary>
    public float SnapMaxOcrConfidence { get; set; } = 0.99f;

    /// <summary>
    /// Emit-sonrası varyant bastırma penceresi (sn): yeni emitin plakası, aynı kameranın/kapsamın son
    /// emitlerinden birinin ADAY listesinde geçiyorsa (veya birebir aynıysa) yeni OLAY yayınlanmaz —
    /// aynı geçişin farklı-uzunluk hayalet varyantları tek olaya iner. 0 = kapalı.
    /// </summary>
    public int VariantSuppressSeconds { get; set; } = 30;

    /// <summary>
    /// Gözlem OCR bütçesi: ROI DIŞI tespitlerden kare başına en fazla bu kadarı (en büyükten) okunur ve
    /// Scope="gozlem" olayı olarak yayınlanır (bariyer kararına girmez; panelde kamerayla eşleşir).
    /// 0 = kapalı. ROI tanımsız kamerada etkisizdir.
    /// </summary>
    public int ObservationMaxPerFrame { get; set; } = 2;

    /// <summary>
    /// Çok-kareli füzyon: düşük güvenli uzlaşı emitlerinde, izin en iyi kırpıntıları hizalanıp
    /// ortalanır ve füzyon görüntüsünde OCR bir kez daha çalışır; daha güvenli sonuç kazanır.
    /// Bulanık/düşük çözünürlüklü plakalar için. Araç başına en fazla 1 ekstra OCR. Ölçümle açılır.
    /// </summary>
    public bool MultiFrameFusion { get; set; } = false;

    /// <summary>Füzyonda kullanılacak en iyi kırpıntı sayısı (iz başına saklanan üst sınır).</summary>
    public int FusionMaxFrames { get; set; } = 5;

    /// <summary>Uzlaşı OCR güveni bu eşiğin ALTINDAysa füzyon denenir (yüksek güvenli emitlere dokunulmaz).</summary>
    public float FusionTriggerOcr { get; set; } = 0.85f;

    /// <summary>
    /// Kanıt kırpıntısı: her olayda, izin en yüksek güvenli plaka kırpıntısı JPEG olarak evidence/
    /// klasörüne kaydedilir ve olayda yolu taşınır (panelde küçük resim; ihtilaflarda gözle karar).
    /// Disk FIFO ile sınırlanır (EvidenceMaxFiles).
    /// </summary>
    public bool EvidenceCrops { get; set; } = true;

    /// <summary>evidence/ klasöründe tutulacak azami dosya sayısı (aşınca en eskiler silinir).
    /// NOT: SaveFullFrame açıkken olay başına 2 dosya (plaka + tam kare) yazılır; bu sınır ikisini
    /// birlikte sayar — aynı gün-sayısı kanıt tutmak için gerekiyorsa iki katına çıkarın.
    /// <para>🔴 <b>PRATİKTE SAKLAMA SÜRESİNİ BELİRLEYEN AYAR BUDUR — <see cref="EvidenceRetentionDays"/>
    /// DEĞİL.</b> İkisi de tek başına yanlış değil ama birlikte yanıltıyor: <c>EvidenceRetentionDays=30</c>
    /// bir ÜST SINIRdır ("30 günden eskisini sil"), bu adet tavanı ise tipik hacimlerde çok daha ÖNCE
    /// bağlar. <b>ÖLÇÜM (2026-08-05, orta ölçekli otopark):</b> saatte ~37 roi olayı, olay başına 2,26
    /// dosya → 6000 tavanı <b>efektif 4,5 gün</b> saklama veriyor; belgelenen 30 günün <b>1/7'si</b>.</para>
    /// <para><b>KENDİ SAHANIZ İÇİN HESAPLAYIN:</b> <c>gereken tavan ≈ saatlik_olay × 24 × hedef_gün ×
    /// olay_başına_dosya</c>. Bu sahada 30 gün için ≈ <b>59.500 dosya ≈ 3,7 GB</b> (medyan kanıt 61 KB);
    /// bugünkü 6000 tavanı yalnız ~377 MB tutuyor.</para>
    /// <para>🔴 <b>AMA "TAVANI YÜKSELTİN" TAVSİYESİ KVKK AÇISINDAN TERS YÖNDEDİR (2026-08-05 düzeltmesi).</b>
    /// Kişisel Verileri Koruma Kurumu'nun yayımlanmış ilkesi: <i>"Gereğinden uzun süre kayıtların
    /// saklanması 6698 sayılı Kanuna aykırılık teşkil edebileceğinden mümkün olan en kısa süre yeterli
    /// görülmeli, sistemde otomatik imha mekanizması bulunmalıdır"</i> ve <i>"Bir olay yaşanması halinde
    /// hukuki süreç boyunca SADECE İLGİLİ OLAN kayıt saklanmalıdır."</i> Kurum sabit bir gün sayısı
    /// vermiyor; ölçüt AMAÇ-BAĞLANTILI ve orantılı olmaktır.</para>
    /// <para>✅ <b>DOĞRU DESEN:</b> kısa toptan saklama (bugünkü hâl) + <b>uyuşmazlık çıkınca O KAYDI
    /// koruma</b>. Plaka görüntüsü kişisel veridir; 30 günlük toptan saklama, günde ~1800 aracın
    /// görüntüsünü gereksizce tutmak demektir — Kurumun uyardığı desenin ta kendisi.
    /// ✅ <b>SEÇİCİ KORUMA ARTIK VAR</b> (2026-08-07): <see cref="EvidencePreserveFlagged"/> açıkken
    /// bayraklı olayın kanıtı bu ADET tavanının erken tahliyesinden muaf tutulur. KVKK gün sınırı
    /// DEĞİŞMEZ. Tavanı büyütmek hâlâ onun yerine geçmez — biri gürültüyü, öteki sinyali korur.
    /// ⚠️ Bağlayıcı süreyi <b>hukuk birimi</b> belirler — bu not hukuki görüş değil, Kurumun
    /// yayımlanmış ilkesinin teknik tavsiyeye yansımasıdır.</para>
    /// <para>⚠️ Formül muhafazakârdır (sabit hız varsayar); gece/hafta sonu düşüşü gerçek saklamayı
    /// uzatır — ölçümde formül 3,0 gün derken gerçek 4,5 gün çıktı.</para></summary>
    public int EvidenceMaxFiles { get; set; } = 6000;

    /// <summary>
    /// Her olayda plaka kırpıntısının YANINDA, en iyi okumanın geldiği TAM KAREyi (aracın da göründüğü
    /// sahne) evidence/..._full.jpg olarak kaydeder ve olayda FramePath ile taşır — otopark yazılımı
    /// "son okunan aracı" görselleyebilsin diye. EvidenceCrops kapalıysa etkisizdir. Disk için kapatılabilir.
    /// </summary>
    public bool SaveFullFrame { get; set; } = true;

    /// <summary>
    /// KVKK saklama süresi: bu günden eski kanıt kırpıntıları otomatik silinir (kamu pratiği 30-45 gün;
    /// bağlayıcı süreyi hukuk birimi belirler). 0 = süre sınırı yok (yalnız adet sınırı çalışır).
    /// <para>⚠️ <b>BU BİR ÜST SINIRDIR, SAKLAMA GARANTİSİ DEĞİL.</b> <see cref="EvidenceMaxFiles"/> tipik
    /// hacimlerde çok daha önce bağlar: bu sahada 30 gün ayarlıyken efektif saklama <b>4,5 gün</b> ölçüldü.
    /// Gerçek saklama süreniz <c>min(bu değer, adet tavanının karşılığı)</c>'dır — ikincisini
    /// <see cref="EvidenceMaxFiles"/> açıklamasındaki formülle hesaplayın.</para>
    /// </summary>
    public int EvidenceRetentionDays { get; set; } = 30;

    /// <summary>
    /// <b>SEÇİCİ KORUMA</b> (varsayılan <c>false</c> = kapalı): <see cref="Anpr.Sdk.Events.PlateEvent.LowConfidence"/>
    /// bayraklı olayların kanıtı <c>evidence/preserved/</c> altına alınır ve <b>ADET tavanının</b>
    /// (<see cref="EvidenceMaxFiles"/>) erken tahliyesinden muaf tutulur.
    /// <para>
    /// 🔑 <b>KVKK SAKLAMA SÜRESİ DEĞİŞMEZ.</b> Korunan dosyalara da <see cref="EvidenceRetentionDays"/>
    /// gün kesimi <b>AYNEN</b> uygulanır; bu ayar hiçbir kanıtı yasal üst sınırdan uzun tutmaz.
    /// Yalnızca "adet dolduğu için ERKEN silinme"yi engeller. Korunan alanın da kendi tavanı vardır
    /// (<see cref="EvidencePreserveMaxFiles"/>) — sınırsız birikim YOKTUR.
    /// </para>
    /// <para>
    /// <b>Neden var — bedeli ÖLÇÜLDÜ (2026-08-07):</b> bir kapı kararına (#76) doğrudan bağlı
    /// <b>91 olayın 20'si (%19,8)</b> kanıtı silindiği için <b>kalıcı olarak doğrulanamaz</b> hâle
    /// gelmişti. Silinenler rastgele değil, popülasyonun <b>en eskileriydi</b> — yani tam da bir
    /// kararın olgunlaşması için gereken süre boyunca bekleyenler. Kaybın nedeni gün sınırı DEĞİL,
    /// adet tavanıydı: ölçümde 30 gün ayarlıyken efektif saklama <b>4,5 gün</b> çıkıyordu.
    /// Karar vermek için gereken kanıt, karar verilene kadar siliniyordu.
    /// </para>
    /// <para>
    /// ⚠️ Bayrak oranı bu sahada olayların <b>~%3'ü</b>; tipik hacimde korunan alan yavaş dolar.
    /// Yine de <see cref="EvidencePreserveMaxFiles"/> dolduğunda korunan alan da FIFO ile budanır —
    /// koruma bir ÖNCELİKTİR, sonsuz garanti değil.
    /// </para>
    /// </summary>
    public bool EvidencePreserveFlagged { get; set; }

    /// <summary>
    /// Seçici korumaya ayrılan dosya tavanı (<see cref="EvidencePreserveFlagged"/> açıkken).
    /// Dolunca korunan alan da en eskiden başlayarak budanır — <b>sınırsız birikim yoktur</b> (KVKK).
    /// Bu tavan <see cref="EvidenceMaxFiles"/>'a EK diske mal olur: kabaca
    /// <c>bu değer × ortalama JPEG boyutu</c>.
    /// </summary>
    public int EvidencePreserveMaxFiles { get; set; } = 1000;

    /// <summary>
    /// Loglarda plaka maskeleme (KVKK): true ise "25ABC123" -> "25***123" yazılır. Geliştirme/gölge
    /// çalışma döneminde karşılaştırma için false; canlı işletmede hukuk birimi kararına göre açılır.
    /// </summary>
    public bool MaskPlatesInLogs { get; set; } = false;

    /// <summary>
    /// Operatör-onayı eşiği: uzlaşı OCR güveni bu değerin altındaki olaylar LowConfidence=true
    /// bayrağıyla yayınlanır (otopark yazılımı bariyeri otomatik açmak yerine operatöre düşürebilir).
    /// <para><b>0,75 → 0,85 (2026-07-29).</b> Varsayılan, ÜRETİMDE DOĞRULANMIŞ değere hizalandı: canlı
    /// sistem aylardır 0,85 ile çalışıyor, ama paket 0,75 sevk ediyordu — yani "varsayılanlar yeterli"
    /// diyen müşteri, hiç doğrulanmamış bir yapılandırma çalıştırıyordu. ÖLÇÜM (1306 canlı roi olayı):
    /// 0,75–0,85 bandında <b>44 olay (%3,4)</b> var ve bunlar eski varsayılanla operatöre HİÇ düşmeden
    /// otomatik bariyer açıyordu. Bant, gözle doğrulanmış yanlış okumalar içeriyor: <c>25A61045</c>
    /// (0,784 — gerçeği "25 A 6104"), <c>25PC879</c> (0,765), <c>25PU939</c> (0,782).
    /// BEDELİ: operatör yükü olayların ~%3,4'ü kadar artar. Otomatik açmayı önceleyen kurulumlar
    /// değeri düşürebilir — ama güven skorunun İYİ KALİBRE OLMADIĞINI bilerek: ölçümde hataların
    /// %78'i bu eşiğin ÜSTÜNDE kalıyor, yani eşik tek başına yeterli bir güvenlik katmanı DEĞİLDİR.</para>
    /// <para>🔄 <b>YENİDEN ÖLÇÜLDÜ (2026-08-05, 1157 gözle doğrulanmış hüküm): "%78" bugün ~%59.</b>
    /// Ham oran %56'ydı; doğrulama yoğunluğu düşük bantlarda daha yüksek olduğu için <b>aşağı yanlıydı</b>,
    /// post-stratifiye edilince %59. Eski %78 muhtemelen <c>&lt;0,85</c> bandı henüz yoğun doğrulanmamışken
    /// yazıldı (o bandın hataları eksik sayılınca üst-eşik payı şişer).</para>
    /// <para>✅ <b>SAYI DÜŞTÜ, SONUÇ DEĞİŞMEDİ:</b> hataların hâlâ <b>çoğunluğu</b> eşiğin üstünde
    /// (%59 [48–63 ham aralık]) — yani "eşik tek başına yeterli bir güvenlik katmanı DEĞİLDİR" cümlesi
    /// ayakta. Bant yapısı da doğruluyor: hata oranı ≥0,99'da %2 · 0,95–0,99'da %10 · 0,85–0,95'te %23 ·
    /// 0,75–0,85'te %41 · &lt;0,75'te %78. <b>0,75–0,85 bandının payı da doğrulandı: %3,4 → %3,3</b>
    /// (148/4464) ve o bandın hata oranı <b>%41</b> — eski 0,75 varsayılanıyla bu olaylar operatöre
    /// HİÇ düşmeden bariyer açardı.</para>
    /// <para>⚠️ Sınır: bant-içi hata oranları da hedefli örneklemeden etkilenmiş olabilir; post-strat
    /// bandlar ARASI yanlılığı düzeltir, bant İÇİ yanlılığı düzeltmez.</para>
    /// </summary>
    public float ManualReviewMinOcr { get; set; } = 0.85f;

    /// <summary>
    /// SON-HANE BELİRSİZLİĞİ kapısı: ilk iki aday yalnız son karakterde farklıysa (biri diğerinin tam
    /// ön-eki) ve güven farkı bu değerin ALTINDAYSA olay <c>LowConfidence=true</c> ile yayınlanır —
    /// yani otomatik açma yerine operatör onayına düşer. <b>Plaka metnini DEĞİŞTİRMEZ</b>, yalnız
    /// bayrak koyar; tek yönlüdür (asla daha gevşek yapmaz). <c>0 = kapalı</c>.
    /// <para>Neden var: kırpıntı son karakteri kestiğinde model "karakter yok" hipotezinden EMİN olur
    /// ve OCR güveni ŞİŞER — güven eşiği bu hatayı göremez (ölçüm: hataların %78'i eşiğin üstünde).
    /// Aday farkı ise görebilir: emin okumalarda fark 0,45–0,52, belirsizlerde 0,000–0,092.</para>
    /// <para>ÖLÇÜLEN ETKİ (1308 canlı roi olayı, 0,10): +13 yeni işaret (%1,0); 4'ü bilinen/şüpheli
    /// yanlış okuma, 9'u doğru okuma (marjinal kesinlik %31). Yani ~1% ek operatör yükü karşılığında
    /// 4 hayalet bariyer açılması engellenir. Otomatik açmayı önceleyen kurulumlar 0 yapıp kapatabilir.
    /// Bilinen yanlış-alarm sınıfı: gerçekten 2 haneli plakalar ("34 HEC 26" gibi).</para>
    /// <para>🔄 <b>YENİDEN ÖLÇÜLDÜ (2026-08-05, 1838 v4 olayı — gerekçe artık olayda YAZILI, yeniden
    /// kurmaya gerek yok): yük %1,0 → <b>%1,7</b> (31 olay yalnız bu gerekçeyle bayraklanıyor); marjinal
    /// kesinlik %31 → <b>0/15</b> [üst sınır %20].</b></para>
    /// <para>🔑 <b>DÜŞÜŞÜN NEDENİ BİLİNİYOR, GİZEM DEĞİL:</b> bu kapının isabeti tek bir sayı değildir —
    /// <b>karşı kameranın aynı plakayı okuyup okumadığına göre %0 ile %73 arasında değişir</b> (ölçüldü:
    /// onaylı hücrede 0/16, onaysızda 8/11). Bayrakların %43'ü onaylı hücrede olduğundan, örneklem
    /// karışımı değiştikçe "marjinal kesinlik" de değişir. <b>Tek rakam yerine hücre bazında okuyun.</b></para>
    /// <para><b>KARAR: kural DEĞİŞTİRİLMEDİ</b> — tek kameralı kurulumda kesilme sınıfının TEK korumasıdır
    /// ve orada karşı-kamera sinyali YOKTUR. İki kameralı kurulumda ise ucuz bir triyaj var: karşı kamera
    /// aynı plakayı ±60 sn içinde okuduysa bu bayrağı öncelik listesinin SONUNA koyun.</para>
    /// </summary>
    public float LastCharAmbiguityMaxGap { get; set; } = 0.10f;

    /// <summary>Dolu ise açılışta tespit modelinin SHA-256'sı doğrulanır; uyuşmazsa servis BAŞLAMAZ (bozuk/yanlış model koruması).</summary>
    public string? DetectorModelSha256 { get; set; }

    /// <summary>Dolu ise açılışta OCR modelinin SHA-256'sı doğrulanır; uyuşmazsa servis BAŞLAMAZ.</summary>
    public string? OcrModelSha256 { get; set; }

    public int HttpPort { get; set; } = 8080;

    /// <summary>HTTP dinleme adresi. Varsayılan "localhost" (yalnız bu makine — güvenli varsayılan, KVKK).
    /// LAN'dan erişim için açıkça "0.0.0.0" yapın VE PanelToken ayarlayın (aksi halde canlı görüntü/plaka/ROI
    /// tüm ağa kimlik-doğrulamasız açık olur — açılışta uyarılır). Boş "" = loopback.</summary>
    public string HttpBindAddress { get; set; } = "localhost";

    /// <summary>/events/recent için bellekte tutulacak maksimum olay sayısı.</summary>
    public int RecentEventsCapacity { get; set; } = 200;

    /// <summary>Webhook URL — YALNIZ Anpr.Service paneli kullanır (doluysa her roi olayı bu URL'e POST
    /// edilir, disk-kuyruk + retry ile). **DLL/AnprRuntime gömülümünde YOK SAYILIR** — PMS olayları
    /// PlateDetected olayından ya da extraSinks (IEventSink) ile alır. Set edilirse AnprRuntime uyarır.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook doluysa bu aralıkta sağlık kalp-atışı POST edilir (kamera durumu, sayaçlar). 0 = kapalı.</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>events.jsonl KVKK saklama süresi (gün) — açılışta eski satırlar budanır. 0 = sınırsız.</summary>
    public int EventRetentionDays { get; set; } = 30;

    /// <summary>Dolu ise panel ve API uçları bu belirteci ister (?token= veya X-Anpr-Token başlığı).
    /// Boş = koruma kapalı (geliştirme). Canlı sahada doldurulmalı (KVKK erişim kontrolü).</summary>
    public string? PanelToken { get; set; }

    public FfmpegOptions Ffmpeg { get; set; } = new();

    public List<CameraOptions> Cameras { get; set; } = new();

    /// <summary>Açılışta yapılandırmayı doğrular (M6). Aralık-dışı eşik/fps/port SESSİZCE kabul edilip
    /// bariyerin hiç açılmamasına yol açmasın diye TÜM sorunları toplayıp tek net hata fırlatır.
    /// Konservatif: yalnız açıkça-geçersiz değerler yakalanır (üretim varsayılanları her zaman geçer).</summary>
    public void Validate()
    {
        var errs = new List<string>();
        void Range01(float v, string name) { if (v < 0f || v > 1f) errs.Add($"{name}={v} [0,1] aralığında olmalı (tipik hata: 0.45 yerine 45 → hiç emit olmaz)"); }

        if (Fps <= 0) errs.Add($"Fps={Fps} > 0 olmalı");
        if (IdleFps < 0) errs.Add($"IdleFps={IdleFps} >= 0 olmalı");
        Range01(MinConfidence, nameof(MinConfidence));
        Range01(DetectorScoreThreshold, nameof(DetectorScoreThreshold));
        Range01(EmitMinConfidence, nameof(EmitMinConfidence));
        Range01(EmitMinOcrConfidence, nameof(EmitMinOcrConfidence));
        Range01(SnapMaxOcrConfidence, nameof(SnapMaxOcrConfidence));
        Range01(EarlyEmitMinOcr, nameof(EarlyEmitMinOcr));
        if (MinConfirmations < 1) errs.Add($"MinConfirmations={MinConfirmations} >= 1 olmalı");
        if (SnapMaxLev < 0) errs.Add($"SnapMaxLev={SnapMaxLev} >= 0 olmalı");
        if (SpatialMergeMaxGapSeconds <= 0) errs.Add($"SpatialMergeMaxGapSeconds={SpatialMergeMaxGapSeconds} > 0 olmalı");
        if (CropPaddingRatio < 0) errs.Add($"CropPaddingRatio={CropPaddingRatio} >= 0 olmalı");
        if (HttpPort is < 1 or > 65535) errs.Add($"HttpPort={HttpPort} 1..65535 olmalı");
        // Faz 2 — hareket kapısı ve kare yolu
        if (MotionGatePixelDelta is < 1 or > 254) errs.Add($"MotionGatePixelDelta={MotionGatePixelDelta} 1..254 olmalı");
        if (MotionGateChangedFraction <= 0 || MotionGateChangedFraction > 1) errs.Add($"MotionGateChangedFraction={MotionGateChangedFraction} (0,1] aralığında olmalı");
        if (MotionGateHoldSeconds < 0 || MotionGateHoldSeconds > 60) errs.Add($"MotionGateHoldSeconds={MotionGateHoldSeconds} 0..60 olmalı");
        if (MotionGateForcedIntervalSeconds < 0.5) errs.Add($"MotionGateForcedIntervalSeconds={MotionGateForcedIntervalSeconds} >= 0.5 olmalı (emniyet kemeri kapatılamaz)");
        if (MotionGateGridColumns < 4 || MotionGateGridRows < 4) errs.Add("MotionGateGridColumns/Rows >= 4 olmalı");
        if (Ffmpeg.HwAccel is not null && !System.Text.RegularExpressions.Regex.IsMatch(Ffmpeg.HwAccel.Trim(), "^[A-Za-z0-9_]*$"))
            errs.Add($"Ffmpeg.HwAccel='{Ffmpeg.HwAccel}' geçersiz (auto | none | ffmpeg hwaccel adı, ör. d3d11va)");
        if (Ffmpeg.DecoderThreads < -1 || Ffmpeg.DecoderThreads > 64) errs.Add($"Ffmpeg.DecoderThreads={Ffmpeg.DecoderThreads} -1..64 olmalı (0 = otomatik, -1 = ffmpeg varsayılanı)");
        if (Ffmpeg.JpegQuality is < 1 or > 31) errs.Add($"Ffmpeg.JpegQuality={Ffmpeg.JpegQuality} 1..31 olmalı (yalnız Mjpeg yolunda kullanılır)");
        // Seçici koruma açıkken tavanın 0/negatif olması "koruma var" sanılıp hiçbir şey korumaz.
        if (EvidencePreserveFlagged && EvidencePreserveMaxFiles < 1)
            errs.Add($"EvidencePreserveMaxFiles={EvidencePreserveMaxFiles} >= 1 olmalı "
                     + "(EvidencePreserveFlagged açıkken 0 tavan = koruma YOK, ama açık SANILIR)");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Cameras.Count; i++)
        {
            var c = Cameras[i];
            string tag = string.IsNullOrWhiteSpace(c.Id) ? $"Cameras[{i}]" : $"kamera '{c.Id}'";
            if (string.IsNullOrWhiteSpace(c.Id)) errs.Add($"{tag}: Id boş olamaz");
            else if (!seenIds.Add(c.Id)) errs.Add($"kamera Id '{c.Id}' yinelenmiş (benzersiz olmalı)");
            if (string.IsNullOrWhiteSpace(c.RtspUrl) && string.IsNullOrWhiteSpace(c.Ip))
                errs.Add($"{tag}: RtspUrl ya da Ip verilmeli (aksi halde bağlanılamaz, sessizce)");
        }

        if (errs.Count > 0)
            throw new InvalidOperationException(
                "ANPR yapılandırması GEÇERSİZ (düzeltilmezse bariyer sessizce hiç açılmayabilir):\n  - "
                + string.Join("\n  - ", errs));
    }
}

public sealed class CameraOptions
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Kamera RTSP şifresi.
    /// <para><b>⚠ BİLİNEN SINIRLAMA — şifre ffmpeg sürecinin komut satırında görünür.</b> RTSP
    /// kimlik bilgisi protokol gereği URL'in içinde taşınır ve ffmpeg'de URL yalnız argüman olarak
    /// verilebilir: ffmpeg 8.0.1'de RTSP demuxer'ının kullanıcı/şifre seçeneği YOKTUR (yalnız
    /// <c>user_agent</c> ve CA dosyası vardır) ve argüman-dosyası desteği de yoktur. Dolayısıyla bunu
    /// SDK içinden kapatmanın bir yolu bulunmuyor; "ortam değişkeniyle geçir" mümkün değildir.</para>
    /// <para>AZALTICI ÖNLEMLER: (1) bariyer PC'sinde kameranın <b>admin</b> hesabını değil, yalnız
    /// akış izni olan ayrı bir hesabı kullanın — sızarsa kamera yapılandırması ele geçmez;
    /// (2) makineyi paylaşmayın: Unix'te <c>ps</c> tüm kullanıcıların argümanlarını gösterir,
    /// Windows'ta başka kullanıcının komut satırını okumak yükseltilmiş yetki ister, yani hedef
    /// win-x64 kurulumda maruziyet daha dardır ama sıfır değildir; (3) şifre LOG'lara yazılmaz —
    /// <c>Anpr.Sdk.Internal.UrlMasking</c> ile maskelenir (2026-07-28'de kapatılan ayrı bir sızıntıydı).</para>
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Tam RTSP URL. Boşsa <see cref="RtspUrlTemplate"/> ile Ip/Username/Password'dan üretilir.</summary>
    public string RtspUrl { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="RtspUrl"/> boşken URL üretmek için kullanılan şablon. Yer tutucular:
    /// <c>{user}</c>, <c>{pass}</c> (ikisi de URL-kaçışlı yazılır), <c>{ip}</c>, <c>{port}</c>.
    /// <para><b>Varsayılan HİKVİSION ana akış yoludur ve marka-özeldir.</b> Yol üreticiye göre değişir —
    /// Dahua: <c>/cam/realmonitor?channel=1&amp;subtype=0</c>, Axis: <c>/axis-media/media.amp</c>,
    /// ONVIF cihazlarda profil başına farklı olabilir. Kameranızın yolunu bilmiyorsanız şablonla
    /// uğraşmak yerine <see cref="RtspUrl"/>'i tam olarak verin; aksi hâlde bağlantı SESSİZCE kurulamaz.</para>
    /// </summary>
    public string RtspUrlTemplate { get; set; } = "rtsp://{user}:{pass}@{ip}:{port}/Streaming/Channels/101";

    /// <summary>Şablondaki <c>{port}</c> için RTSP portu (standart 554).</summary>
    public int RtspPort { get; set; } = 554;

    /// <summary>
    /// Bu kameranın ROI çokgeni, 0-1000 normalize "x1,y1;x2,y2;..." (Hikvision şerit koordinat ölçeği —
    /// kameranın LaneConfig noktalarından türetilebilir). Merkezi dışarıda kalan tespit OKUNMAZ.
    /// Boş = kapalı (tüm kare). Ölçmeden etkinleştirme.
    /// </summary>
    public string Roi { get; set; } = string.Empty;
}

/// <summary>Hareket kapısı çalışma modu.</summary>
public enum MotionGateMode
{
    /// <summary>Kapı yok: her yeni kare işlenir (eski davranış).</summary>
    Off = 0,
    /// <summary>Gölge: karar hesaplanır ve sayılır ama uygulanmaz (doğruluk riski sıfır; ölçüm için).</summary>
    Shadow = 1,
    /// <summary>
    /// Açık — HAREKET TETİKLİ zamanlama: kapı kaynağın HER karesine bakar; hareket varken (ve kısa bir süre
    /// sonrasında) her kare işlenir, sahne boşken yalnız zorunlu aralıkta bir kare işlenir. Boşta işlemci
    /// düşer, hızlı geçen araçta işlenen kare sayısı ARTAR (zamanlayıcı fazına bağlı kaçırma kalkar).
    /// </summary>
    On = 2,
}

/// <summary>ffmpeg'in boruya yazdığı kare biçimi.</summary>
public enum FfmpegOutputFormat
{
    /// <summary>Sıkıştırmasız BMP/BGRA: kodlama/çözme yok, kare başlığı uzunluk taşır. 2.0.0 öncesi denemelerin yolu; karşılaştırma için durur.</summary>
    Bmp = 0,
    /// <summary>MJPEG (eski yol): kare başına JPEG kodlama (ffmpeg) + çözme (C#).</summary>
    Mjpeg = 1,
    /// <summary>
    /// Y4M / YUV 4:2:0 (2.0.0, VARSAYILAN): çözücünün KENDİ biçimi. ffmpeg renk dönüşümü yapmaz, kare 16,3 MB
    /// yerine 6,1 MB'tır (4 MP) ve motor kareyi tam RGB'ye çevirmeden okur. En düşük işlemci ve bellek yolu.
    /// </summary>
    Y4m = 2,
}

public sealed class FfmpegOptions
{
    /// <summary>
    /// Kare çıkış biçimi. <see cref="FfmpegOutputFormat.Y4m"/> varsayılan (2.0.0): ffmpeg çözdüğü kareyi renk
    /// dönüşümü yapmadan YUV 4:2:0 olarak yazar; motor dedektör girdisini doğrudan bu kareden örnekler, yalnız
    /// plaka kırpıntısı RGB'ye çevrilir.
    /// <para>ÖLÇÜM (2026-09-30, 4 MP HEVC 25 fps, tek kamera, 10 dk kayıt oynatma, BMP → Y4M): süreç işlemcisi
    /// tek çekirdeğin %23–26'sından %17–21'ine, ffmpeg %10 → %2,7; süreç belleği ortalama 205–216 MB → 131–138 MB.
    /// DOĞRULUK: aynı 8 kayıt (80 dk) iki yoldan kare kare işlendi — okuma farkları, yalnız renk yuvarlaması
    /// değişen ara kolun farklarıyla aynı büyüklükte (gürültü tabanı); etiketli sette kesinlik/duyarlılık ±1 örnek.</para>
    /// <see cref="FfmpegOutputFormat.Bmp"/> ve <see cref="FfmpegOutputFormat.Mjpeg"/> karşılaştırma/geri dönüş için durur.
    /// </summary>
    public FfmpegOutputFormat OutputFormat { get; set; } = FfmpegOutputFormat.Y4m;

    /// <summary>
    /// Video çözücünün iş parçacığı sayısı (<c>-threads</c>). <b>0 (varsayılan) = otomatik: en çok 2</b>
    /// (çekirdek sayısı 1 ise 1). -1 = ffmpeg'e bırak (çekirdek sayısı kadar).
    /// <para><b>Neden sınırlı (2026-09-30 ölçümü, 4 MP HEVC, yazılım çözme):</b> ffmpeg varsayılanı her çekirdek
    /// için ayrı çözme bağlamı ve kare tamponu tutar — 10 çekirdekli makinede kamera başına <b>243 MB</b>. İki iş
    /// parçacığıyla aynı akış <b>100 MB</b>, tek iş parçacığıyla 93 MB; işlemci zamanı değişmiyor (%17–18 tek
    /// çekirdek). Yani fazladan iş parçacıkları hız değil yalnız bellek harcıyordu.</para>
    /// </summary>
    public int DecoderThreads { get; set; } = 0;

    /// <summary>
    /// Donanım çözmede kare düşürmeyi EKRAN KARTINDA yap (varsayılan true): kareler grafik belleğinde kalır,
    /// <c>fps</c> süzgeci orada uygulanır ve yalnız kullanılacak kareler sistem belleğine indirilir
    /// (<c>-hwaccel_output_format … -vf fps=N,hwdownload,format=nv12</c>). Kapalıyken 25 karenin 25'i de
    /// indirilip 20'si atılır. Ölçüm (2026-09-30, videotoolbox, 5 fps çıkış): ffmpeg işlemci %5,6 → %4,3.
    /// Sürücü bu düzeni desteklemezse iki karesiz denemeden sonra kendiliğinden düz donanım çözmeye,
    /// o da olmazsa yazılıma düşülür (bkz. <see cref="HwAccel"/>).
    /// </summary>
    public bool GpuFrameDrop { get; set; } = true;

    /// <summary>
    /// Donanım video çözme (<c>-hwaccel</c>). "auto" (varsayılan): ffmpeg'in desteklediklerinden işletim
    /// sistemine uygun olanı seçer — Windows: d3d11va (Intel/AMD/NVIDIA iGPU dâhil), macOS: videotoolbox; yoksa
    /// yazılım. "none": kapalı. Açık ad da yazılabilir (ör. "qsv", "vaapi"). Ölçüm (M4, 4 MP HEVC): çözme
    /// maliyeti %16 → %5 tek-çekirdek. Donanım akışı desteklemezse ffmpeg kendi içinde yazılıma düşer; iki
    /// ardışık karesiz denemede bu kaynak da kapatır. Doğruluğa etkisi yok (HEVC çözme spesifikasyonla belirlidir).
    /// </summary>
    public string HwAccel { get; set; } = "auto";

    /// <summary>
    /// ffmpeg'in RTSP'den ÜRETECEĞİ kare hızı. 0 = <see cref="AnprServiceOptions.Fps"/> kullanılır
    /// (geriye uyumlu varsayılan).
    /// <para>NEDEN AYRI (2026-07-28 ölçümü): ffmpeg süreç ömrü boyunca SABİT hızda üretir; uyarlanabilir
    /// <see cref="AnprServiceOptions.IdleFps"/> yalnız C# yoklama döngüsüne uygulanır. Yani "boştayken CPU
    /// tasarrufu" hattın pahalı ucunda GERÇEKLEŞMİYOR: canlı 60 sn ölçümde 601 kare üretildi, 246'sı
    /// işlendi — <b>%59,1 kare hiç işlenmeden atıldı</b> (her biri ~0,9 MB tahsis + ffmpeg decode maliyeti).
    /// Bench A/B (diag4/giris, 4500 kare): kaynak fps 5→3→2'de benzersiz plaka 13→14→15, kamerayla eşleşen
    /// 9→9→10 ve fps=5'te yakalanıp fps=2'de kaybolan araç YOK. Yine de bench sentetik zaman kullandığı
    /// için (dedup/settle pencereleri de esner) canlı doğrulama şarttır → varsayılan DEĞİŞTİRİLMEDİ.</para>
    /// </summary>
    public double SourceFps { get; set; } = 0;

    /// <summary>ffmpeg yürütülebilir yolu (PATH'te ise "ffmpeg").</summary>
    public string Path { get; set; } = "ffmpeg";

    /// <summary>RTSP taşıma: tcp (önerilir) veya udp.</summary>
    public string RtspTransport { get; set; } = "tcp";

    /// <summary>MJPEG kalitesi (-q:v): 2 en iyi .. 31 en kötü.</summary>
    public int JpegQuality { get; set; } = 4;

    /// <summary>0 = doğal çözünürlük; >0 ise kareyi bu genişliğe küçültür (CPU tasarrufu).</summary>
    public int ScaleWidth { get; set; } = 0;

    /// <summary>Bu süre boyunca kare gelmezse bağlantı kopmuş sayılır ve yeniden bağlanılır.</summary>
    public int NoFrameTimeoutSeconds { get; set; } = 12;

    /// <summary>Yeniden bağlanmada üstel beklemenin üst sınırı (sn).</summary>
    public int MaxBackoffSeconds { get; set; } = 30;

    /// <summary>
    /// İleri düzey CPU ayarı için ek ffmpeg GİRİŞ argümanları (ör. "-discard nokey" sadece
    /// anahtar kareleri çözer). Boş bırakılabilir.
    /// </summary>
    /// <para>🌐 <b>ARAŞTIRMA NOTU (2026-08-05): burada ayarlanabilecek en yararlı şey bir TAŞIMA
    /// KATMANI ZAMAN AŞIMIdır.</b> Bu sahada 10 günde <b>116 "asılı akış" epizodu</b> ölçüldü:
    /// ffmpeg hata vermiyor, ama kare de gelmiyor — RTSP/TCP bağlantısı veri beklerken takılıyor.
    /// Argüman listemizde soket zaman aşımı YOK, dolayısıyla ffmpeg süresiz bekleyebilir; onu
    /// kesen şey bizim uygulama seviyesindeki bekçimiz (<see cref="NoFrameTimeoutSeconds"/> = 12 sn).
    /// <c>ExtraInputArgs = "-rw_timeout 5000000"</c> (mikrosaniye) ffmpeg'in KENDİSİNİN hızlı
    /// başarısız olmasını sağlar. ⚠️ <b>KAZANÇ GERÇEK AMA KÜÇÜK:</b> epizot başına ~7 sn (12→5),
    /// 116 epizotta ~14 dakika / 10 gün. Kod değişikliği GEREKMEZ, yalnız config.
    /// ✅ <c>RtspTransport</c> zaten <c>tcp</c> — en sık verilen tavsiye bizde uygulanmış durumda.</para>
    public string? ExtraInputArgs { get; set; }
}
