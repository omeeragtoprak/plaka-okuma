using Anpr.Core;

namespace Anpr.Sdk.Events;

/// <summary>
/// Geçerli ve yeni bir plaka okumasından üretilen olay. Web app / veritabanı bu modeli tüketir.
/// </summary>
public sealed record PlateEvent
{
    /// <summary>Olay şeması sürümü (M5). Alan eklenince/anlamı değişince artırılır; webhook + events.jsonl
    /// tüketicileri (PMS) SESSİZ kırılma yerine sürümü kontrol edip uyum sağlayabilir. Mevcut şema = 8
    /// (v2: <see cref="SnappedFrom"/>; v3: <see cref="FrameBox"/>; v4: <see cref="LowConfidenceReasons"/>;
    /// v8: <see cref="TrackFrames"/> + <see cref="TrackAbsorbed"/> eklendi — *"geçiş başına kaç kare
    /// işlendi?"* sorusu `docs/saha-fiziksel-gercekler.md` §4'te ölçülmemiş boşluktu ve #69'un kadraj
    /// kararını doğrudan etkiliyor (§P.4 (425)). Sayı zaten PlateConfirmer'da tutuluyordu.
    /// v7: <see cref="SnapTargetRank"/> + <see cref="SnapTargetOcr"/> eklendi — snap'in OCR tarafından
    /// desteklenip desteklenmediği ölçülebilsin (§P.4 (365)/(366) ölçüm boşluğu).
    /// v6: <see cref="EarlyEmitHeldAs"/> eklendi — v5'in yarım kalan yarısı; tutmanın SONUCU
    /// ancak karşı-olgusal dizgiyle ölçülebilir (§P.4 (335)).
    /// v5: <see cref="EarlyEmitHeldAtOcr"/> eklendi — eski tüketiciler bilinmeyen alanları yoksayarak
    /// çalışmayı sürdürür, hiçbir alan kaldırılmadı/anlamı değişmedi. <see cref="LowConfidence"/>
    /// bool'unun anlamı v4 ve v5'te de AYNIDIR).</summary>
    /// <remarks>
    /// ⚠️ <b>Varsayılan 1'dir, 5 DEĞİL — bu bilinçlidir (2026-08-10).</b> Alan M5'te eklendi; ondan
    /// ÖNCE üretilmiş JSON'da (yayınlanmış <c>v1.0.1</c> dönemi) bu özellik hiç YOKTUR. Varsayılan 5
    /// olduğu sürece böyle bir belge sessizce <b>"en yeni şema"</b> diye okunuyordu — oysa
    /// <see cref="LowConfidenceReasons"/> gibi alanları hiç taşımıyor. Tüketicinin
    /// <c>SchemaVersion &gt;= 4</c> kontrolü bu yüzden YANLIŞ pozitif veriyordu; tam olarak
    /// sürüm alanının önlemesi gereken sessiz kırılma. Varsayılan artık <b>en eski</b> şemadır:
    /// alan yoksa tüketici muhafazakâr davranır (güvenli tarafa düşer).
    /// Üretim yolu sürümü <see cref="CurrentSchemaVersion"/> ile AÇIKÇA yazar.
    /// </remarks>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Bu SDK sürümünün ÜRETTİĞİ şema sürümü. Üretim yolu <see cref="SchemaVersion"/>'ı
    /// buradan yazar; iki değerin ayrışması (varsayılan vs üretilen) böyle imkânsızlaşır.</summary>
    public const int CurrentSchemaVersion = 8;

    public required DateTimeOffset Timestamp { get; init; }
    public required string CameraId { get; init; }
    public required string CameraName { get; init; }

    /// <summary>Normalize edilmiş plaka, ör. "25 ABC 123".</summary>
    public required string Plate { get; init; }

    /// <summary>Boşluksuz plaka, ör. "25ABC123" (eşleştirme/sorgu için).</summary>
    public required string PlateCompact { get; init; }

    /// <summary>OCR'ın ham çıktısı (normalizasyon öncesi).</summary>
    public required string RawText { get; init; }

    /// <summary>
    /// BİRLEŞİK skor = <see cref="DetectorConfidence"/> × <see cref="OcrConfidence"/>.
    /// <para><b>Bariyer/eşik kararı için KULLANMAYIN.</b> Bu bir ÇARPIM olduğu için her zaman
    /// <see cref="OcrConfidence"/>'tan küçüktür ve <c>ManualReviewMinOcr</c> ile AYNI ÖLÇEKTE DEĞİLDİR:
    /// canlı ölçümde 433 roi olayının %87,8'inde bu değer 0,85'in altındayken olay aslında
    /// otomatik-açma için uygundu (<see cref="LowConfidence"/> = false). Yalnız sıralama/teşhis içindir.</para>
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>Plakayı bulan dedektörün (YOLO) kutu güveni. Metnin doğruluğu hakkında bilgi vermez.</summary>
    public required float DetectorConfidence { get; init; }

    /// <summary>
    /// OKUNAN METNİN güveni — <b>bariyer kararının dayandığı değer budur.</b>
    /// <see cref="LowConfidence"/> tam olarak <c>OcrConfidence &lt; ManualReviewMinOcr</c> ile hesaplanır.
    /// PMS kendi ek eşiğini kuracaksa bu alanı kullanmalıdır, <see cref="Confidence"/>'ı değil.
    /// <para>🔴 <b>SNAP UYGULANDIYSA BU DEĞER <see cref="PlateCompact"/>'İN DEĞİL, SNAP ÖNCESİ
    /// DİZGİNİN GÜVENİDİR</b> (ölçüldü 2026-08-22, §P.4 (365)). <c>ApplyCuratedCorrection</c> metni
    /// değiştirir ama <c>OcrConfidence</c>'a DOKUNMAZ; model snap edilmiş dizgiyi hiç skorlamamıştır.
    /// Yani <c>SnappedFrom != null</c> olan bir olayda "%98 eminim" ifadesi <b>başka bir plaka</b>
    /// hakkındadır — ve belirsiz olan adım tam da snap'in kendisidir.
    /// Ölçülen dağılım: snap ürünü olaylarda medyan <b>0,899</b>, snapsızlarda <b>0,996</b>;
    /// her iki kümenin de %100'ü emit eşiğinin üstünde.
    /// ⇒ <b>Eşik kararı verirken <see cref="SnappedFrom"/> alanına de bakın</b>; null değilse bu skoru
    /// "yayınlanan plakaya duyulan güven" olarak okumayın.</para>
    /// </summary>
    public required float OcrConfidence { get; init; }

    /// <summary>
    /// Okumanın geldiği kutu. <b>DİKKAT:</b> bu kutu <see cref="FramePath"/>'teki kareye ait OLMAYABİLİR —
    /// okuma birden çok kareden birleştirilir ve en iyi okumanın karesi ile kaydedilen kare farklı olabilir.
    /// Kaydedilen kare üzerinde piksel denetimi yapacaksanız <see cref="FrameBox"/> kullanın.
    /// </summary>
    public required PlateBox Box { get; init; }

    /// <summary>
    /// <see cref="FramePath"/>'teki kareye AİT plaka kutusu — yani bu kutuyu o kareden kırptığınızda
    /// plakayı görürsünüz. <see cref="Box"/> ile aynı olmak zorunda değildir.
    /// <para>Neden ayrı: ölçümde olayların <b>%24,3'ünde</b> <see cref="Box"/>, kaydedilen kareye ait
    /// değildi (farklı andan geliyordu); bu yüzden "kutum doğru mu / kırpıntı plakayı kesiyor mu"
    /// sorusu olay kaydından yanıtlanamıyordu. Kanıt kırpıntısı zaten bu kutuyla üretiliyor.</para>
    /// Kare kaydedilmediyse (SaveFullFrame kapalı) ya da kutu bilinmiyorsa null.
    /// </summary>
    public PlateBox? FrameBox { get; init; }

    /// <summary>
    /// Olay kapsamı: "roi" = tetik bölgesi içi (bariyer/otopark yazılımına gider);
    /// "gozlem" = ROI dışı cadde/uzak okuma (yalnız izleme/karşılaştırma — karar üretmez).
    /// </summary>
    public string Scope { get; init; } = "roi";

    /// <summary>Kanıt kırpıntısının (yalnız PLAKA) servis-köküne göre yolu (ör. "evidence/xxx.jpg"). Yoksa null.</summary>
    public string? EvidencePath { get; init; }

    /// <summary>
    /// En iyi okumanın geldiği TAM KARE (aracın da göründüğü bütün sahne) yolu, ör.
    /// "evidence/xxx_full.jpg". Otopark yazılımı "son okunan aracı" görselleyebilsin diye plaka
    /// kırpıntısının yanında verilir. SaveFullFrame kapalıysa ya da kare yoksa null.
    /// </summary>
    public string? FramePath { get; init; }

    /// <summary>Tekil olay kimliği — webhook/PMS tarafında idempotent işleme ve düzeltme geri-bildirimi için.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// <see cref="OcrConfidence"/> (Confidence DEĞİL) ManualReviewMinOcr eşiğinin altında: otopark
    /// yazılımı bariyeri otomatik açmak yerine operatör onayına düşürmelidir (kirli/karlı/bulanık plaka).
    /// </summary>
    public bool LowConfidence { get; init; }

    /// <summary>
    /// Bu olay <c>EarlyEmitMinOcr</c> tarafından <b>en az bir kez TUTULDUYSA</b>, ilk tutulma anındaki
    /// OCR güveni; hiç tutulmadıysa <c>null</c> (v5, 2026-08-07). Beklemenin kazancı =
    /// <see cref="OcrConfidence"/> − bu değer.
    /// <para>
    /// <b>Neden eklendi:</b> tutma bir <b>gecikme–doğruluk takasıdır</b> — bariyer daha geç açılır,
    /// karşılığında daha iyi bir okuma beklenir. Bugüne dek yalnız SÜREÇ sayaçlarıyla izleniyordu ve
    /// o sayaçlar (a) yeniden başlatmada sıfırlanır, (b) <b>hangi olayın tutulduğunu söylemez</b>.
    /// Dolayısıyla takasın asıl sorusu — <i>"beklemek YANLIŞ bir okumayı DOĞRUYA çevirdi mi?"</i> —
    /// yer-gerçeğiyle eşleştirilemiyor, ayar yalnız çıkarımla savunuluyordu. Bu alan olayın kendisine
    /// iliştiği için soru artık ölçülebilir. <b>Davranış değişmedi</b>: alan yalnız iz bırakır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Güven kazancı bir VEKİLDİR, sonuç DEĞİLDİR.</b> Güvenin artması okumanın doğrulandığı
    /// anlamına gelmez; ölçümde ortalama kazanç kameraya göre yalnız <b>+0,010 ile +0,050</b> arasında
    /// çıktı. Karar, kazanç değerine değil, yer-gerçeğiyle eşleştirilmiş SONUCA dayanmalıdır.
    /// </para>
    /// <para>
    /// ⚠️ <c>SchemaVersion &lt; 5</c> olan olaylarda alan <b>yazılmamıştır</b>: <c>null</c> olması
    /// "tutulmadı" DEĞİL "bilinmiyor" demektir. Geriye dönük doldurulamaz.
    /// </para>
    /// </summary>
    public float? EarlyEmitHeldAtOcr { get; init; }

    /// <summary>
    /// Tutma anında okunan METİN — <b>tutma olmasaydı yayınlanacak dizgi</b> (v6, 2026-08-21).
    /// <c>null</c> = tutulmadı <i>ya da</i> olay v6 öncesi.
    /// <para>
    /// 🔑 <b><see cref="EarlyEmitHeldAtOcr"/> TEK BAŞINA #70'İ YANITLAMAZ.</b> O alan güven
    /// artışını verir; #70'in sorusu *"tutmak YANLIŞ okumayı DOĞRUYA çevirdi mi?"*. Güven,
    /// okuma hiç değişmeden de artabilir. Karar için gereken karşılaştırma şudur:
    /// <c>EarlyEmitHeldAs</c> ↔ <see cref="PlateCompact"/> ↔ <i>yer-gerçeği</i>. Üçü bir arada
    /// dört sonucu ayırır: tutma DÜZELTTİ · BOZDU · değiştirmedi · ikisi de yanlış.
    /// </para>
    /// <para>
    /// ⚠️ <b>Geriye dönük doldurulamaz.</b> <c>SchemaVersion &lt; 6</c> olan olaylarda alan
    /// YOKTUR; o olaylarda tutmanın sonucu KALICI olarak bilinmez. Bu alanla yapılacak her
    /// ölçüm v6 açılışından SONRAYA kısıtlanmalıdır (§P.4 (285)).
    /// </para>
    /// </summary>
    public string? EarlyEmitHeldAs { get; init; }

    /// <summary>
    /// <see cref="LowConfidence"/> NEDEN true — sıfır veya daha fazla neden (v4, 2026-08-03).
    /// <b><see cref="SchemaVersion"/> &gt;= 4 olan olaylarda</b>
    /// <c>LowConfidence == (LowConfidenceReasons.Count > 0)</c> her zaman geçerlidir; bayrağın
    /// anlamı DEĞİŞMEDİ, yalnız gerekçesi açıldı.
    /// <para>
    /// ⚠️ <b>v3 ve ÖNCESİ kayıtlarda bu eşitlik GEÇERLİ DEĞİLDİR:</b> alan o dönemde yazılmadığı için
    /// eski bir olay <c>LowConfidence = true</c> ile birlikte <b>BOŞ</b> bir gerekçe listesi taşır
    /// (ölçüldü: bu sahanın arşivinde 3041 olayın 270'i böyle). Geçmişi de okuyan bir PMS,
    /// <c>Reasons</c> boşken "bayrak sebepsiz" SONUCUNA VARMAMALIDIR — önce
    /// <see cref="SchemaVersion"/> kontrol edilmelidir. Bu, alanın kendisinin değil ARŞİVİN sınırıdır
    /// ve geriye dönük doldurulamaz (bkz. <c>Candidates</c> sırasızlığı).
    /// </para>
    /// <para>
    /// <b>Neden var — nedenlerin İSABETİ 5 kat farklı ve operatör bunu göremiyordu:</b>
    /// </para>
    /// <list type="table">
    /// <item><term><c>weak-reading</c></term><description>
    ///   <c>OcrConfidence &lt; ManualReviewMinOcr</c>. Kuyruğun büyük çoğunluğu; ölçülen isabet
    ///   <b>~%56</b>. Eylem: kırpıntıya bak. ⚠️ Bu grubun <b>%30'u insan gözüyle de okunamıyor.</b></description></item>
    /// <item><term><c>last-char-ambiguity</c></term><description>
    ///   Son karakter var mı yok mu kararsızlığı. Ölçülen isabet <b>%13</b> (2526 olayda 16 tetikleme,
    ///   15 doğrulamanın 2'si haklı) — yani kuyruğun EN VERİMSİZ kalemi. Bilinçli korunuyor: <b>tek
    ///   kameralı</b> kurulumda kesilme sınıfının TEK korumasıdır.</description></item>
    /// <item><term><c>cross-camera-truncation</c></term><description>
    ///   Başka kameranın daha uzun + daha güvenli okumasının ön-eki. İsabet <b>%100</b> (2/2, nedensel).</description></item>
    /// <item><term><c>cross-camera-substitution</c></term><description>
    ///   Saniyeler içinde okunmuş, aynı uzunlukta, tek karakter farklı ve DAHA GÜVENLİ bir okumanın
    ///   bozulmuş hâli. İsabet <b>~%89–94</b> (nedensel ölçüm). <b>Kuyruğun en değerli kalemi:</b>
    ///   ~%8'ini oluşturur ama neredeyse hepsi gerçek hatadır. Eylem: karşı kameranın aynı
    ///   geçişteki okumasıyla karşılaştır.</description></item>
    /// </list>
    /// <para>
    /// <b>Operatör kuyruğunu bu alana göre SIRALAYIN:</b> çapraz-kamera nedenleri önce, son-karakter
    /// belirsizliği en sonda. Ölçülen fark, aynı kuyrukta %13'lük ve %89'luk işlerin yan yana
    /// durmasıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu alan olmadan neden geriye dönük hesaplanamıyor:</b> <see cref="Candidates"/>
    /// SIRASIZDIR, dolayısıyla <c>last-char-ambiguity</c> kuralı (aday[0] vs aday[1]) olay
    /// günlüğünden yeniden üretilemez. Kayıt anında yazılmazsa bilgi KALICI olarak kaybolur.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LowConfidenceReasons { get; init; } = Array.Empty<string>();

    /// <summary><see cref="LowConfidenceReasons"/> sabitleri — dizgi yerine bunları karşılaştırın.</summary>
    public const string ReasonWeakReading = "weak-reading";
    /// <inheritdoc cref="ReasonWeakReading"/>
    public const string ReasonLastCharAmbiguity = "last-char-ambiguity";
    /// <inheritdoc cref="ReasonWeakReading"/>
    public const string ReasonCrossCameraTruncation = "cross-camera-truncation";
    /// <inheritdoc cref="ReasonWeakReading"/>
    public const string ReasonCrossCameraSubstitution = "cross-camera-substitution";

    /// <summary>
    /// Alternatif okuma adayları. <b>SIRALAMA GARANTİSİ YOKTUR</b> — <see cref="PlateCandidate.Confidence"/>'a
    /// göre sıralı DEĞİLDİR. Değişmez olan tek şey: ilk eleman her zaman <see cref="PlateCompact"/> ile
    /// aynıdır (sistemin SEÇTİĞİ okuma; canlı 505 olayda 0 istisna).
    /// <para><b>En yüksek Confidence'lı adayı SEÇMEYİN</b> — canlı ölçümde olayların %36,4'ünde bu, son
    /// karakteri düşmüş KESİK bir varyanttır. Adayları yalnız KÜME olarak kullanın (abone/whitelist
    /// eşleşmesinde "adaylardan herhangi biri listede var mı" sorusu için).</para>
    /// </summary>
    public IReadOnlyList<PlateCandidate>? Candidates { get; init; }

    /// <summary>
    /// KnownPlates/müdavim SNAP uygulandıysa, snap ÖNCESİ ham okuma (boşluksuz); uygulanmadıysa null.
    /// Snap, okumayı bilinen bir plakaya lev≤SnapMaxLev düzeltir — bu alan olmadan düzeltmenin kendisi
    /// olayda görünmez olur (<see cref="RawText"/> snap sonrası değerle yazılır), dolayısıyla üretimde
    /// snap ne denetlenebilir ne de ölçülebilir. Kullanım: (a) canlı doğruluk denetimi (snap doğru muydu),
    /// (b) PMS güvenmediği snap'i reddedip ham okumayı kullanabilir, (c) yanlış-snap'te kanıt/geri-bildirim.
    /// KVKK: içeriği plakadır — <see cref="PlateCompact"/> ile aynı saklama/maskeleme kuralına tabidir.
    /// <para>🔴 <b>SNAP, GÜVEN VE ADAY LİSTESİNİN ANLAMINI DEĞİŞTİRİR (§P.4 (365)):</b></para>
    /// <list type="bullet">
    ///   <item><see cref="OcrConfidence"/> <b>snap ÖNCESİ</b> dizginin skorudur; snap edilmiş plaka
    ///   modelce hiç skorlanmamıştır.</item>
    ///   <item><see cref="Candidates"/>[0] yayınlanan plakaya yeniden yazılır ve <b>aynı ödünç güveni</b>
    ///   taşır; ham okuma 2. sıraya iner.</item>
    ///   <item>Snap hedefine EŞİT olan özgün aday listeden <b>elenir</b> → "OCR'ın kendisi bu düzeltmeyi
    ///   destekliyor muydu?" sorusu olaydan <b>artık yanıtlanamaz</b>. Ölçüldü: 536 snap olayının
    ///   536'sında hedef 1. sırada, kaynak 2. sırada — yani liste snap SONRASI durumu yansıtır ve
    ///   snap kararı için <b>bağımsız kanıt değildir</b>.</item>
    /// </list>
    /// </summary>
    public string? SnappedFrom { get; init; }

    /// <summary>
    /// (şema v7) SNAP uygulandıysa: snap hedefinin, snap <b>ÖNCESİ</b> aday listesindeki 1-tabanlı
    /// sırası; hedef modelin ilk-K adayında hiç yoksa <c>null</c>. Snap yoksa <c>null</c>.
    /// <para>🔑 <b>NEDEN VAR:</b> snap, aday listesini yeniden yazıp hedefi başa koyar ve hedefe eşit
    /// olan özgün adayı <b>eler</b> (belgelenmiş sözleşme: <c>Candidates[0] == PlateCompact</c>).
    /// Bu, *"OCR'ın kendisi bu düzeltmeyi destekliyor muydu?"* sorusunu olaydan yanıtlanamaz
    /// kılıyordu — ölçüldü: 536 canlı snap olayının 536'sında hedef 1., kaynak 2. sırada.</para>
    /// <para>⚠️ <c>null</c>, *"model buna sıfır olasılık verdi"* DEMEK DEĞİLDİR; yalnız *"ilk-K
    /// içinde değildi"* demektir. Yokluk kanıtı değil, <b>kapsam sınırı</b>.</para>
    /// </summary>
    public int? SnapTargetRank { get; init; }

    /// <summary>
    /// (şema v7) SNAP uygulandıysa: snap hedefine <b>MODELİN KENDİ</b> verdiği güven (snap öncesi
    /// aday listesinden). Hedef ilk-K'da yoksa <c>null</c>.
    /// <para>⚠️ <see cref="OcrConfidence"/> ile KARIŞTIRMAYIN: snap sonrası <c>OcrConfidence</c> hâlâ
    /// <b>snap ÖNCESİ dizginin</b> ödünç skorudur. Bu alan hedefin kendi skorudur; ikisinin
    /// <b>farkı</b> snap'in modele ne kadar aykırı olduğunu ölçer.</para>
    /// </summary>
    public float? SnapTargetOcr { get; init; }

    /// <summary>
    /// Yön göstergesi (denetim/ayar): izin plaka GENİŞLİK eğilimi = son-yarı-max / erken-yarı-max
    /// (DirectionTracker, OCR'dan bağımsız her tespit kutusundan). &gt;1 araç yaklaşıyordu, &lt;1
    /// uzaklaşıyordu, -1 karar verilemedi (kısa iz). RequireApproaching açıkken belirgin uzaklaşan
    /// (bu değer &lt; ApproachDepartRatio) roi olayları zaten yayınlanmaz; bu alan, yayınlananların yönünü
    /// tam-kare kanıtla karşılaştırıp eşiği ayarlamak için taşınır.
    /// </summary>
    public float SizeTrend { get; init; } = -1f;

    /// <summary>
    /// Bu geçişte plakanın <b>bulunup okunduğu</b> kare sayısı (uzlaşı izinin <c>Count</c>'u).
    /// <para><b>Neden var (§P.4 (425)):</b> #69'un kadraj tavsiyesi bir TAKAS taşıyor — dar kadraj
    /// görüş süresini kısaltır ve kare bütçesi 20 km/s'te 3,7'ye iner. Ama o tablo <b>varsayılan
    /// hızlarla</b> hesaplandı; sahada geçiş başına gerçekte kaç kare işlendiği
    /// <c>docs/saha-fiziksel-gercekler.md</c> §4'te <b>ölçülmemiş</b> diye kayıtlıydı. Bu alan onu
    /// doğrudan ölçülebilir kılar; davranışı DEĞİŞTİRMEZ.</para>
    /// <para>⚠️ İşlenen tüm kareler değil, plakanın <b>görüldüğü</b> kareler. Dedektörün kaçırdığı
    /// kareler sayılmaz → <b>ALT SINIR</b>. Ayrıca uzlaşıya soğurulan okumalar
    /// <see cref="TrackAbsorbed"/>'dedir, burada değil.</para>
    /// </summary>
    public int? TrackFrames { get; init; }

    /// <summary>
    /// Bu ize soğurulan (aynı araç sayılıp birleştirilen) diğer okuma sayısı.
    /// <see cref="TrackFrames"/> ile birlikte uzlaşının ne kadar veriyle kurulduğunu gösterir.
    /// </summary>
    public int? TrackAbsorbed { get; init; }
}
