namespace Anpr.Service.Panel;

/// <summary>Tek bir ölçüm: oran (0-1) ya da serbest sayı (medyan gecikme vb.).</summary>
/// <param name="Name">Gösterge adı.</param>
/// <param name="Value">Değer. <paramref name="IsProportion"/> ise 0-1 aralığında.</param>
/// <param name="N">Bu değerin dayandığı örneklem büyüklüğü (istatistiksel test için ŞART).</param>
/// <param name="IsProportion">true ise iki-oran z-testi uygulanır; false ise göreli değişime bakılır.</param>
/// <param name="Note">Göstergenin ne anlama geldiği (panelde tooltip).</param>
public sealed record MetricSample(string Name, double Value, int N, bool IsProportion, string Note = "");

/// <summary>Bir göstergedeki kayma bulgusu.</summary>
public sealed record DriftSignal(
    string Metric, double Baseline, double Current, double Delta,
    int BaselineN, int CurrentN, string Severity, string Explanation);

/// <summary>
/// OPERASYONEL KAYMA İZLEME (saf matematik — I/O yok, test edilebilir).
///
/// <para><b>Ne yapar / ne YAPMAZ:</b> yer-gerçeği GEREKTİRMEZ, bu yüzden "doğruluk %X" DEMEZ.
/// Yalnız şunu söyler: "bugünün davranışı dünlerden istatistiksel olarak anlamlı biçimde farklı".
/// Doğruluk iddiası için gözle doğrulama (<see cref="GroundTruth"/>) gerekir; bu ikisi birbirinin
/// yerine geçmez — biri "bozuldu mu?", diğeri "ne kadar doğru?" sorusunu yanıtlar.</para>
///
/// <para><b>Neden gerekli:</b> sahada sessiz bozulmanın tipik yolları ölçümle görülebilir ama
/// kimse bakmıyorsa fark edilmez: kamera odağı kayar → kalite kapısı sayacı patlar; gece/kış
/// koşulları → güven dağılımı düşer; kırpma bozulur → 8-hane payı düşer (kesilme!); ffmpeg
/// yavaşlar → kare kaybı artar. Hepsi yer-gerçeği olmadan görülebilir.</para>
///
/// <para><b>İstatistik dürüstlüğü:</b> oranlar için iki-oran z-testi kullanılır VE ayrıca minimum
/// ETKİ BÜYÜKLÜĞÜ istenir. Sebep: n büyüdükçe z-testi önemsiz farkları da "anlamlı" bulur
/// (%92,8 → %92,3 gibi) ve uyarı gürültüsü operatörü sinyale karşı sağırlaştırır.</para>
/// </summary>
public static class DriftMonitor
{
    /// <summary>Bir uyarı üretmek için gereken en küçük örneklem. Altındaysa "veri yetersiz" denir.</summary>
    public const int MinSample = 30;

    /// <summary>Oranlarda anlamlılığa EK OLARAK istenen en küçük mutlak fark (yüzde puanı / 100).</summary>
    public const double MinProportionEffect = 0.05;

    /// <summary>Oran olmayan göstergelerde uyarı için gereken en küçük GÖRELİ değişim.</summary>
    public const double MinRelativeEffect = 0.20;

    /// <summary>
    /// Taban ile güncel pencereyi karşılaştırır. Yalnız İKİ pencerede de bulunan göstergeler
    /// değerlendirilir; eksik gösterge sessizce atlanmaz, "veri yok" olarak raporlanır.
    /// </summary>
    public static IReadOnlyList<DriftSignal> Compare(
        IReadOnlyList<MetricSample> baseline, IReadOnlyList<MetricSample> current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var byName = baseline.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var result = new List<DriftSignal>();

        foreach (var cur in current)
        {
            if (!byName.TryGetValue(cur.Name, out var bas))
            {
                result.Add(new DriftSignal(cur.Name, double.NaN, cur.Value, double.NaN, 0, cur.N,
                    "veri-yok", "Taban penceresinde bu gösterge yok — karşılaştırma yapılamadı."));
                continue;
            }

            double delta = cur.Value - bas.Value;

            if (bas.N < MinSample || cur.N < MinSample)
            {
                result.Add(new DriftSignal(cur.Name, bas.Value, cur.Value, delta, bas.N, cur.N,
                    "veri-yetersiz",
                    $"Örneklem küçük (taban n={bas.N}, güncel n={cur.N}; en az {MinSample} gerekli) — " +
                    "fark gürültüden ayırt edilemez."));
                continue;
            }

            if (cur.IsProportion && bas.IsProportion)
            {
                double z = TwoProportionZ(bas.Value, bas.N, cur.Value, cur.N);
                bool significant = Math.Abs(z) > 1.959963985;
                bool bigEnough = Math.Abs(delta) >= MinProportionEffect;

                string sev = (significant, bigEnough) switch
                {
                    (true, true) => "uyari",
                    (true, false) => "izle",     // anlamlı ama küçük → gürültü yapmadan not et
                    _ => "normal",
                };
                string why = sev switch
                {
                    "uyari" => $"Anlamlı VE büyük kayma (z={z:F2}, fark {delta * 100:+0.0;-0.0} puan).",
                    "izle" => $"İstatistiksel olarak anlamlı ama küçük (z={z:F2}, fark {delta * 100:+0.0;-0.0} puan) — " +
                              "tek başına aksiyon gerektirmez.",
                    _ => $"Fark gürültü içinde (z={z:F2}).",
                };
                result.Add(new DriftSignal(cur.Name, bas.Value, cur.Value, delta, bas.N, cur.N, sev, why));
            }
            else
            {
                double rel = Math.Abs(bas.Value) < 1e-9 ? 0 : delta / Math.Abs(bas.Value);
                string sev = Math.Abs(rel) >= MinRelativeEffect ? "uyari" : "normal";
                result.Add(new DriftSignal(cur.Name, bas.Value, cur.Value, delta, bas.N, cur.N, sev,
                    sev == "uyari"
                        ? $"Göreli değişim {rel * 100:+0.0;-0.0}% (eşik ±{MinRelativeEffect * 100:F0}%)."
                        : $"Göreli değişim {rel * 100:+0.0;-0.0}% — eşik altında."));
            }
        }

        // En kritik olanlar üstte: uyarı → izle → veri sorunları → normal.
        static int Rank(string s) => s switch
        {
            "uyari" => 0, "izle" => 1, "veri-yetersiz" => 2, "veri-yok" => 3, _ => 4
        };
        return result.OrderBy(r => Rank(r.Severity)).ThenBy(r => r.Metric, StringComparer.Ordinal).ToList();
    }

    /// <summary>İki-oran z istatistiği (havuzlanmış oran ile).</summary>
    public static double TwoProportionZ(double p1, int n1, double p2, int n2)
    {
        if (n1 <= 0 || n2 <= 0) return 0;
        double pooled = (p1 * n1 + p2 * n2) / (n1 + n2);
        double se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        return se < 1e-12 ? 0 : (p2 - p1) / se;
    }
}
