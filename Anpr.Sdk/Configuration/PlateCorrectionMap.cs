using Anpr.Core.TurkishPlates;

namespace Anpr.Sdk.Configuration;

/// <summary>
/// KÜRATÖRLÜ TAM-DİZGİ PLAKA DÜZELTMESİ — kanıtlanmış sistematik yanlış okumalar için son çare.
///
/// <para><b>Neden gerekli (2026-07-31 canlı ölçüm):</b> <c>25 LMY 166</c> plakalı araç sistematik
/// olarak <c>25LMV166</c> okunuyordu — <b>39 olay, 39'u da bariyeri OTOMATİK açtı</b>, güvenler
/// 0,852–0,999 (27'si ≥0,99). Var olmayan bir plakayla geçiş veriliyordu ve mevcut korumaların
/// HİÇBİRİ bunu göremiyordu:</para>
/// <list type="number">
///   <item><b>Güven skoru göremez</b> — hata 0,999 ile geliyor. Model emin ve yanlış (korele hata).</item>
///   <item><b>Kamera çaprazı göremez</b> — Hikvision ANPR'ı da AYNI yönde yanılıyor: mutabakat %100,
///         doğruluk %0. İki bağımsız sistemin aynı hatayı yapması bu hatayı görünmez kılar.</item>
///   <item><b>Bulanık snap (<c>KnownPlates</c>) göremez</b> — <c>SnapMaxOcrConfidence</c> (0,99) kapısı
///         tam da bu okumaları geçiriyor. Kapıyı kaldırmak ÖLÇÜLDÜ ve REDDEDİLDİ (2026-07-23):
///         yanlış-snap 1 → 11, çünkü bulanık eşleşme lev1 KOMŞUYA kayabiliyor.</item>
/// </list>
///
/// <para><b>Neden bu mekanizma güvenli:</b> bulanık snap'in riski "yanlışlıkla komşuya kayma"dır.
/// Tam-dizgi eşleşme bu riski <b>yapısal olarak</b> taşımaz — yalnız tek bir kesin dizgide tetiklenir,
/// hiçbir komşuya bulaşmaz. Bu yüzden güven kapısı burada UYGULANMAZ: o kapı bulanık eşleşmenin
/// korumasıydı, bu mekanizmanın değil.</para>
///
/// <para><b>KULLANIM UYARISI:</b> her girdi kanıt görselinden GÖZLE doğrulanmış olmalıdır. Bu harita
/// bir okumayı KANONLAŞTIRIR; yanlış bir girdi, doğru okumaları kalıcı olarak bozar. Aynı hatayı bu
/// projede <c>KnownPlates</c> ile yaşadık (çapraz-mutabakatla üretilen liste iki YANLIŞ plaka içeriyordu,
/// 2026-07-31). Varsayılan BOŞTUR ve boşken davranış birebir eskisiyle aynıdır.</para>
/// </summary>
public sealed class PlateCorrectionMap
{
    private readonly Dictionary<string, string> _map;

    private PlateCorrectionMap(Dictionary<string, string> map) => _map = map;

    /// <summary>Boş harita — saf geçirgen.</summary>
    public static PlateCorrectionMap Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Geçerli kabul edilmiş düzeltmeler (boşluksuz, büyük harf).</summary>
    public IReadOnlyDictionary<string, string> Entries => _map;

    /// <summary>Uygulanacak en az bir düzeltme var mı (sıcak yolda erken çıkış için).</summary>
    public bool HasEntries => _map.Count > 0;

    /// <summary>
    /// Boşluksuz plakayı düzeltir; eşleşme yoksa AYNEN döner. Tam-dizgi eşleşme — hiçbir yakınlık
    /// ölçüsü kullanılmaz.
    /// </summary>
    public string Apply(string compact)
        => compact.Length > 0 && _map.TryGetValue(compact, out string? to) ? to : compact;

    /// <summary>
    /// Config'ten haritayı kurar ve GEÇERSİZ girdileri REDDEDER. Reddedilen her girdi
    /// <paramref name="problems"/> listesine yazılır — çağıran bunları GÜRÜLTÜLÜ loglamalıdır:
    /// sessizce düşen bir düzeltme, config'i yazan kişi için "uyguladım" yanılsaması yaratır.
    /// </summary>
    public static PlateCorrectionMap Build(AnprServiceOptions options, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(options);
        var issues = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (options.PlateCorrections.Count == 0)
        {
            problems = issues;
            return Empty;
        }

        var known = new HashSet<string>(
            options.KnownPlates.Select(Compact).Where(s => s.Length > 0), StringComparer.Ordinal);

        foreach (var kv in options.PlateCorrections)
        {
            string from = Compact(kv.Key), to = Compact(kv.Value);
            if (from.Length == 0 || to.Length == 0) continue;   // boş satır: sessizce atla

            if (from == to)
            {
                issues.Add($"'{from}' kendine eşleniyor — etkisiz girdi, kaldırın.");
                continue;
            }
            // HEDEF geçerli bir TR plakası olmalı: config'e yazılan bir yazım hatası aksi hâlde
            // bariyeri geçersiz bir plakayla açardı.
            var tr = TurkishPlateNormalizer.Normalize(to);
            if (!tr.IsValid)
            {
                issues.Add($"'{from}' -> '{to}': hedef geçerli TR plakası değil, düzeltme UYGULANMIYOR.");
                continue;
            }
            // Aynı dizgi hem "bilinen doğru plaka" hem "kanıtlanmış yanlış okuma" olamaz. Sessizce
            // birini seçmek, hangisinin kazandığını config'i yazan kişiden gizlerdi.
            if (known.Contains(from))
            {
                issues.Add($"'{from}' hem KnownPlates'te hem düzeltme kaynağı — whitelist ile ÇELİŞİYOR, " +
                           "düzeltme UYGULANMIYOR. Biri kaldırılmalı.");
                continue;
            }
            map[from] = tr.Compact!;
        }

        // ZİNCİR YASAK: A->B ve B->C birlikte olursa tek geçişte hangi sonucun çıkacağı sözlük
        // sırasına bağlı olurdu (belirlenimsiz). Tekrarlı uygulamak da döngü riski taşır.
        // Zincirin İKİ halkası da düşer: yarım uygulanan zincir, hiç uygulanmamasından daha kafa
        // karıştırıcı bir sonuç üretir (kaynak düzeltilir ama hedefi düzeltilmez).
        var chainSources = map.Where(kv => map.ContainsKey(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (string src in chainSources)
        {
            string mid = map[src];
            issues.Add($"'{src}' -> '{mid}' -> '{map[mid]}' zincir oluşturuyor; uygulama sırası belirsiz " +
                       "olacağı için zincirin İKİ halkası da UYGULANMIYOR.");
            map.Remove(src);
            map.Remove(mid);
        }

        problems = issues;
        return map.Count == 0 ? Empty : new PlateCorrectionMap(map);
    }

    private static string Compact(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char c in s) if (char.IsLetterOrDigit(c)) buf[n++] = char.ToUpperInvariant(c);
        return new string(buf[..n]);
    }
}
