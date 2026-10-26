using System.Collections.Concurrent;

namespace Anpr.Sdk.Events;

/// <summary>
/// Müdavim plaka kaydı: yüksek güvenle (≥0.98) defalarca görülmüş plakalar. Otoparkta düzenli
/// araçlar baskındır; bir müdavimin yakın-varyantı (lev≤2) kısa süre içinde ikinci olay üretiyorsa
/// bu neredeyse kesin aynı geçişin yanlış-okumasıdır (ölçülen vaka: 25CS185 müdavim, 4 sn sonra
/// 25CS725@0.89 emit — iki bastırma kuralı da kaçırdı). Kayıt events.jsonl'dan yüklenir, canlı güncellenir.
/// </summary>
public sealed class FrequentPlateRegistry
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    /// <summary>Bu kadar yüksek-güvenli görülme müdavim sayılır.</summary>
    /// <summary>
    /// Bir plakanın "müdavim" (bilinen araç) sayılması için gereken roi görülme sayısı. Müdavim olan bir
    /// okuma snap'ten KORUNUR (<see cref="SnapTarget"/> onu aynen döndürür) — yani sistem, tekrar tekrar
    /// gördüğü bir plakayı whitelist'teki BENZER bir plakaya çekmez.
    /// <para>ÖLÇÜM (2026-07-28, 444 canlı roi olayı, kamera ANPR referans): eşik 5 iken 10 doğru / 2 YANLIŞ
    /// snap (net +8); eşik 3'te değişmiyor; <b>eşik 2'de 10 doğru / 0 yanlış (net +10)</b> — hiç doğru
    /// düzeltme kaybetmeden her iki yanlış snap'i önlüyor. Önlenen gerçek vakalar: 25VG319→25VG318 ve
    /// 25PPN892→25PPN872; ikisinde de araç whitelist'te OLMAYAN, komşusu whitelist'te OLAN gerçek plakaydı
    /// (whitelist'in %16'sı birbirine lev1 uzaklıkta ardışık seri plakalardan oluşuyor — belirsiz hedefler).
    /// Alternatif "ambiguity guard" (belirsiz hedefe hiç snap yapma) ölçüldü ve DAHA KÖTÜ çıktı (9/1, net +8).</para>
    /// <para>⚠️ YUKARIDAKİ "ambiguity guard DAHA KÖTÜ" SONUCU 9× DAHA BÜYÜK ÖRNEKLEMDE TUTMADI
    /// (2026-08-04, n=11 → n=100 gözle doğrulanmış snap olayı). Yeni ölçüm: koruma KAPALI 75 doğru /
    /// 25 yanlış snap (net +50) · koruma AÇIK 52 / 2 (net <b>+50</b>) — yani net EŞİT, "daha kötü"
    /// değil. Eski sonuç n=11'de gürültüydü; ölçüm silinmedi, üstüne yazıldı.</para>
    /// <para>🔑 VE ASIL NOKTA: <b>net, bu kararın DOĞRU METRİĞİ DEĞİL.</b> İki hata sınıfının zararı
    /// farklıdır. Koruma açıkken önlenen 23 bozulmanın <b>5'i, GERÇEK BAŞKA bir aracın plakasını</b>
    /// yayınlıyordu (ücret yanlış aboneye yazılır, denetim izi geçerli görünür → <b>sessiz ve mali</b>).
    /// Kaybedilen 23 düzeltmenin ise 22'sinin ham dizgisi hiçbir kaynakta yoktur (abone eşleşmez, elle
    /// müdahale → <b>gürültülü ve operasyonel</b>). Sayılar eşit, zararlar eşit değil.</para>
    /// <para>Yukarıda "whitelist'in %16'sı belirsiz" deniyor; FİİLEN KULLANILAN hedeflerde bu oran daha
    /// yüksek: snap hedefi 112 plakanın <b>%24'ünün</b>, snap edilen 207 olayın ise <b>%35'inin</b> lev≤1
    /// komşusu da sahada bağımsız doğrulanmış gerçek bir plakadır (ardışık numaralı FİLO plakaları).
    /// Kök neden "zehirli whitelist girdisi" DEĞİL: bozan dizgiler config listesinde yok, yani girdi
    /// silmek sorunu kapatmıyor.</para>
    /// <para>✅ <b>"MÜDAVİM OLAN DOĞRUDUR" VARSAYIMI İLK KEZ SAYILDI (2026-08-05, 1157 hüküm) — GÜÇLÜ
    /// AMA MUTLAK DEĞİL:</b> müdavim dizgilerde hata <b>13/952 = %1,4</b>; müdavim olmayanlarda
    /// <b>155/205 = %75,6</b>. Yani ayrım <b>54×</b> — bu kaydın snap'ten koruma gerekçesi ölçümle
    /// desteklendi. Ama 13 istisna var ve <b>iki AYRI mekanizmadan</b> geliyorlar:</para>
    /// <para>  ① <b>GERİ BESLEME (7 olay, <c>25LMY166</c>):</b> saf bir yanlış okuma "müdavim" hâline
    /// gelmişti — çünkü küratörlü düzeltme haritası onu KANONLAŞTIRMIŞTI. Kameranın bağımsız kaydında
    /// yalnız 1 izi var. <b>Ayırt edici test: bağımsız kaynakta iz yoksa müdavimlik kendi kuyruğunu
    /// yiyordur.</b> ② <b>FİLO KARIŞMASI (6 olay: <c>25MT297</c>, <c>25YBN279</c>, <c>25MT245</c>,
    /// <c>25NM996</c>):</b> bunlar GERÇEK araçlar (kamerada 16–139 kayıt) ama o olaylarda komşu filo
    /// plakasıyla karıştırılmışlar — müdavimlik doğru, eşleştirme yanlış.</para>
    /// <para>✅ Ayrıca yukarıdaki <b>"%17" iddiası 2,5× örneklemde DOĞRULANDI: bugün %16</b>
    /// (59/377 müdavim dizginin lev-1 komşusu da sahada bağımsız doğrulanmış gerçek bir plaka).</para>
    /// <para>ÜÇÜNCÜ SEÇENEK ölçüldü: "belirsiz hedefe snap yapma <i>ama olayı bayrakla</i>" — bozulma
    /// 2/100'e iner ve düzeltme sessizce kaybolmaz; maliyeti operatör kuyruğunda <b>+%19</b>
    /// (276 → 329; naif toplama −%8 der, YANLIŞTIR — onay sinyali yayınlanan plakaya bakar, snap ise
    /// yayınlanan plakayı değiştirir, yani iki önlem etkileşir). <b>Karar kullanıcınındır; varsayılan
    /// davranış DEĞİŞTİRİLMEDİ.</b></para>
    /// </summary>
    public int MinSightings { get; init; } = 2;

    /// <summary>Kayda katılmak için gereken OCR güveni.</summary>
    public float MinConfidence { get; init; } = 0.98f;

    public int Count => _counts.Count;

    /// <summary>Manuel whitelist: sitenin BİLİNEN plakaları (ör. resmi/protokol araçları).
    /// Config'den doldurulur.
    /// <para>⛔ <b>"Bunlara snap HER ZAMAN GÜVENLİDİR (küçük, küratörlü küme)" — bu cümle burada
    /// yazıyordu ve YANLIŞTI (2026-08-11'de kaldırıldı).</b> Aynı dosyanın aşağıdaki ölçümleri onu
    /// çürütüyor: whitelist'in %16'sı birbirine lev-1 ardışık seri plakalardan oluşuyor, fiilen
    /// kullanılan snap hedeflerinde bu oran <b>%24</b>, snap edilen olaylarda <b>%35</b>. Küratörlü
    /// olmak, ÜYELERİN BİRBİRİNE KARIŞMAYACAĞI anlamına gelmez. Sahada ölçüm (2026-08-11): 395
    /// girdinin <b>66 çifti</b> birbirine lev-1; 390 snap olayının <b>30'u (%7,7)</b> ham okuması
    /// ≥2 whitelist girdisine EŞİT uzaklıkta olduğu için BELİRSİZ hedefliydi.</para></summary>
    public IReadOnlyCollection<string> KnownPlates { get; init; } = System.Array.Empty<string>();

    /// <summary>Config'den gelen whitelist'i güvenle normalize eder: null/boş öğeler atlanır (bir
    /// null girdi SDK init'ini çökertmemeli), boşluk/harf-dışı temizlenir, BÜYÜK harfe çevrilir.</summary>
    public static HashSet<string> NormalizeKnownPlates(IEnumerable<string?>? plates)
    {
        var set = new HashSet<string>();
        if (plates == null) return set;
        foreach (var p in plates)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var c = new string(p.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (c.Length >= 5) set.Add(c);
        }
        return set;
    }

    /// <summary>
    /// Plaka OTOMATİK sayaçta müdavim eşiğini geçti mi. <b>Küratörlü <see cref="KnownPlates"/> listesini
    /// KAPSAMAZ</b> — orada olmak "bu araç sık geçiyor" demek değil, "bu plakayı biliyoruz" demektir.
    /// <para>Snap koruması bu ayrımı zaten yapıyor (<see cref="SnapTarget"/> içinde
    /// <c>KnownPlates.Contains(compact) || IsFrequent(compact)</c>). Varyant bastırma Kural D ise yalnız
    /// bu metoda bakar; whitelist'i de dahil etmek ÖLÇÜLDÜ (2026-07-28, 5 yakın-varyant çifti):
    /// <b>ek katkı 0</b> — Kural 2 (düşük güvenli yakın-varyant) zaten 5/5'ini yakalıyor. Bu yüzden
    /// kapsam bilinçli olarak DAR bırakıldı; genişletmek ölçülebilir fayda vermiyor.</para>
    /// </summary>
    public bool IsFrequent(string compact)
        => _counts.TryGetValue(compact, out int n) && n >= MinSightings;

    /// <summary>
    /// Yanlış-okuma düzeltme: <paramref name="compact"/> okuması, BİLİNEN bir plakaya (manuel whitelist
    /// ve isteğe bağlı müdavimler) edit-mesafesi ≤ <paramref name="maxLev"/> yakınsa o bilinen plakaya
    /// "snap"lenir; değilse aynen döner. Okumanın kendisi zaten biliniyorsa dokunulmaz. Aynı mesafede
    /// birden çok aday varsa: whitelist > en yüksek görülme sayılı müdavim. Siyah resmi plaka gibi
    /// FREKANSLI araçların sistematik yanlış-okumalarını düzeltir (offline ölçüm: lev≤1 siyah +27pp,
    /// beyazda 0 yanlış-snap). maxLev≤0 = kapalı.
    /// </summary>
    public string SnapTarget(string compact, int maxLev, bool includeFrequent)
    {
        if (maxLev <= 0 || compact.Length < 5) return compact;
        if (KnownPlates.Contains(compact) || IsFrequent(compact)) return compact;

        string best = compact; int bestLev = maxLev + 1; long bestScore = -1;
        void Consider(string cand, long score)
        {
            if (cand == compact) return;
            int d = BoundedLev(compact, cand, maxLev);
            if (d > maxLev) return;
            if (d < bestLev || (d == bestLev && score > bestScore)) { bestLev = d; best = cand; bestScore = score; }
        }
        // ⚠️ BERABERLİK KIRMA BURADA SESSİZ VE KEYFİDİR (2026-08-11'de belgelendi, davranış
        // DEĞİŞMEDİ — #78 kullanıcı kararı):  her whitelist adayına AYNI skor (long.MaxValue)
        // veriliyor, dolayısıyla `score > bestScore` iki eşit-uzaklıklı whitelist girdisi arasında
        // ASLA doğru olmaz → kazanan, `KnownPlates` (bir HashSet) enumerasyon sırasında İLK gelendir.
        // Yani seçim sıklığa, güvene veya bir önceliğe göre DEĞİL; küme sırasına göredir ve
        // ekleme sırası/hash değişince değişebilir. Canlı örnek: ham `25GKN770` hem `25GKS770`e
        // hem `25GKM770`e lev-1; ikisi de whitelist'te; `25GKS770` seçildi ve kanıt görüntüsünde
        // araç `25GKS770`in aracı DEĞİLDİ (beyaz Dacia Duster vs koyu yeşil SsangYong Musso).
        foreach (var w in KnownPlates) Consider(w, long.MaxValue);          // whitelist önceliği
        if (includeFrequent)
            foreach (var kv in _counts)
                if (kv.Value >= MinSightings) Consider(kv.Key, kv.Value);
        return best;
    }

    // Erken-çıkışlı Levenshtein: mesafe maxLev'i aşarsa maxLev+1 döner (hız).
    private static int BoundedLev(string a, string b, int max)
    {
        if (System.Math.Abs(a.Length - b.Length) > max) return max + 1;
        var dp = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) dp[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = dp[0]; dp[0] = i; int rowMin = dp[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = dp[j];
                dp[j] = System.Math.Min(System.Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur; if (dp[j] < rowMin) rowMin = dp[j];
            }
            if (rowMin > max) return max + 1;
        }
        return dp[b.Length];
    }

    public void Record(string compact, float ocrConfidence)
    {
        if (ocrConfidence >= MinConfidence && compact.Length >= 7)
            _counts.AddOrUpdate(compact, 1, (_, n) => n + 1);
    }

    /// <summary>events.jsonl geçmişinden tohumla (PascalCase/camelCase toleranslı, bozuk satırlar atlanır).</summary>
    public int LoadFromJsonl(string path)
    {
        if (!File.Exists(path)) return 0;
        int loaded = 0;
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                string? compact = Get(root, "plateCompact")?.GetString();
                float conf = (float)(Get(root, "ocrConfidence")?.GetDouble() ?? 0);
                string scope = Get(root, "scope")?.GetString() ?? "roi";
                if (compact != null && scope == "roi") { Record(compact, conf); loaded++; }
            }
            catch { /* bozuk satır */ }
        }
        return loaded;

        static System.Text.Json.JsonElement? Get(System.Text.Json.JsonElement e, string name)
        {
            if (e.TryGetProperty(name, out var v)) return v;
            string pascal = char.ToUpperInvariant(name[0]) + name[1..];
            return e.TryGetProperty(pascal, out var v2) ? v2 : null;
        }
    }
}
