namespace Anpr.Sdk.Workers;

/// <summary>
/// TÜM KAMERALARIN son yayınlarını tutan küçük, pencere-sınırlı kayıt. Amacı tek: bir okumanın,
/// saniyeler içinde yayınlanmış DAHA GÜVENLİ bir okumanın bozulmuş hâli olup olmadığını anlamak.
/// İki bozulma biçimi ayrı ayrı sorgulanır:
/// <list type="bullet">
///   <item><see cref="IsTruncationOfRecent"/> — <b>kesilme</b> (tam 1 karakter kısa, ön-ek).</item>
///   <item><see cref="IsWeakVariantOfRecent"/> — <b>ikame</b> (aynı uzunluk, tam 1 karakter farklı).</item>
/// </list>
///
/// <para><b>Neden gerekli:</b> <c>CameraWorker</c>'ın varyant korumaları worker İÇİNDE çalışır ve her
/// kamera ayrı worker'dır; aynı araç iki kameradan saniyeler arayla okunduğunda hiçbiri devreye
/// girmez. Ölçüm (2505 canlı roi olayı): kesilme 8 vaka (7'si bariyeri OTOMATİK açtı) · ikame 37 vaka
/// (33'ü otomatik açtı).</para>
///
/// <para><b>Kamera kimliği ŞART DEĞİLDİR</b> (parametre yalnız çağrı yerini okunur kılmak için var).
/// Ölçüldü: aynı kamerayı da kapsamak ikame sınıfında 30 → 37 çift yapıp doğrulanmış hatayı 17 → 19'a
/// çıkarıyor, karşı örneği ARTIRMIYOR. Aynı kameranın kendi ardışık okumaları zaten Kural 2 varyant
/// bastırmasına takıldığından çifte iş de doğurmuyor (Kural 2'nin 0,95 tavanının üstünde kalanlar
/// buraya düşer — istenen davranış).</para>
///
/// <para><b>Eşzamanlılık:</b> birden çok worker thread'inden çağrılır → tüm erişim kilitli.
/// Kayıt sayısı pencereyle sınırlıdır (her yazımda süresi geçenler atılır), sınırsız büyümez.</para>
/// </summary>
public sealed class RecentEmitRegistry
{
    private readonly record struct Entry(string CameraId, string Compact, float Ocr, DateTimeOffset At);

    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();
    private readonly TimeSpan _prefixWindow;
    private readonly TimeSpan _variantWindow;
    private readonly TimeSpan _keep;

    /// <param name="prefixWindow">Kesilme kapısının penceresi. <see cref="TimeSpan.Zero"/> = KAPALI.
    /// Ölçüm: ±8 sn 3 vaka · ±15 sn 5 · ±30 sn yine 5 → 15 sn doğal pencere.</param>
    /// <param name="variantWindow">İkame kapısının penceresi. <see cref="TimeSpan.Zero"/> = KAPALI.
    /// Ölçüm: ±8 sn 20 çift/%85 isabet · ±15 sn 30/%89 · ±30 sn 34/%90 → 15 sn seçildi (±30, 4 çift
    /// karşılığında yalnız 1 doğrulanmış hata daha getiriyor).</param>
    public RecentEmitRegistry(TimeSpan prefixWindow, TimeSpan variantWindow)
    {
        _prefixWindow = prefixWindow;
        _variantWindow = variantWindow;
        _keep = prefixWindow > variantWindow ? prefixWindow : variantWindow;
    }

    /// <summary>Bellek sınırlılığını doğrulamak için (test).</summary>
    public int Count { get { lock (_lock) return _entries.Count; } }

    /// <summary>Yayınlanan bir okumayı kaydeder ve süresi geçen kayıtları atar.</summary>
    public void Record(string cameraId, string compact, float ocr, DateTimeOffset at)
    {
        if (_keep <= TimeSpan.Zero || string.IsNullOrEmpty(compact)) return;
        lock (_lock)
        {
            _entries.RemoveAll(e => at - e.At > _keep);
            _entries.Add(new Entry(cameraId, compact, ocr, at));
        }
    }

    /// <summary>
    /// <paramref name="compact"/>, pencere içinde yayınlanmış DAHA UZUN ve DAHA GÜVENLİ bir okumanın
    /// tam ön-eki mi (tam olarak 1 karakter kısa)?
    ///
    /// <para><b>YÖN ŞART — güven karşılaştırması atlanamaz.</b> Ölçülmüş karşı-örnek: gerçek plaka
    /// <c>25 A 6104</c> (0,991), bizim uzun okumalarımız <c>25A61040</c>/<c>25A61045</c> (0,814/0,784)
    /// YANLIŞTI. Orada KISA taraf doğrudur; yön koşulu olmasaydı doğru okumayı operatöre atardık.</para>
    /// </summary>
    public bool IsTruncationOfRecent(string cameraId, string compact, float ocr, DateTimeOffset at)
        => Match(compact, ocr, at, _prefixWindow, prefix: true);

    /// <summary>
    /// <paramref name="compact"/>, pencere içinde yayınlanmış AYNI UZUNLUKTA, TAM 1 KARAKTER farklı ve
    /// DAHA GÜVENLİ bir okumanın bozulmuş hâli mi?
    ///
    /// <para><b>ÖLÇÜLMÜŞ İSABET %89 — kesilme kapısından ZAYIF, karşı örnekleri VAR.</b> 2505 roi
    /// olayında 30 çapraz-kamera çifti; gözle doğrulanan 19'unun 17'sinde zayıf taraf gerçekten
    /// yanlıştı, 2'sinde DOĞRUYDU (<c>34YF7170</c> ← <c>34YFT170</c> ve <c>25GE891</c> ←
    /// <c>25GE895</c>). Güven farkı ikisini AYIRMIYOR (karşı örnek 0,092 farkla gelirken hatalar
    /// 0,003–0,166 aralığına yayılıyor) → ek eşik EKLENMEDİ, ölçümle reddedildi.</para>
    ///
    /// <para><b>lev 1, lev 2 DEĞİL:</b> lev≤2 hacmi 30 → 62 çifte çıkarıp isabeti %89 → %84'e
    /// düşürüyor (karşı örnek 2 → 4). Bu sahada yerleşik araçların %17'sinin lev-1 komşusu DA gerçek
    /// bir araçtır; eşiği gevşetmek doğrudan o çakışmayı büyütür.</para>
    ///
    /// <para><b>Neden yine de açık:</b> eylem plakayı DEĞİŞTİRMEZ ve olayı BASTIRMAZ, yalnız
    /// <c>LowConfidence=true</c> yapar. Yanlış-pozitifin bedeli 1 operatör onayı; yanlış-negatifin
    /// bedeli var olmayan (ya da BAŞKA BİR ARACIN) plakasıyla açılan bariyer — bu sınıfın ilk
    /// doğrulanmış örneği <c>25MT297</c> ← <c>25MT299</c>, ikisi de gerçek filo aracı.</para>
    /// </summary>
    public bool IsWeakVariantOfRecent(string cameraId, string compact, float ocr, DateTimeOffset at)
        => Match(compact, ocr, at, _variantWindow, prefix: false);

    private bool Match(string compact, float ocr, DateTimeOffset at, TimeSpan window, bool prefix)
    {
        if (window <= TimeSpan.Zero || string.IsNullOrEmpty(compact)) return false;
        lock (_lock)
        {
            foreach (var e in _entries)
            {
                if (at - e.At > window || e.At - at > window) continue;
                if (e.Ocr <= ocr) continue;                                    // diğeri DAHA güvenli olmalı
                if (prefix)
                {
                    if (e.Compact.Length != compact.Length + 1) continue;      // tam 1 karakter uzun
                    if (!e.Compact.StartsWith(compact, StringComparison.Ordinal)) continue;
                    return true;
                }
                if (e.Compact.Length != compact.Length) continue;              // aynı uzunluk
                int diff = 0;
                for (int i = 0; i < compact.Length && diff < 2; i++)
                    if (compact[i] != e.Compact[i]) diff++;
                if (diff == 1) return true;                                    // AYNI dizgi (0) dedup'ın işi
            }
            return false;
        }
    }
}
