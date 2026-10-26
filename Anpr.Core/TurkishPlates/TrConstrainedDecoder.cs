namespace Anpr.Core.TurkishPlates;

public readonly record struct TrConstrainedResult(bool Ok, string Province, string Letters, string Numbers, float Confidence)
{
    public string Normalized => $"{Province} {Letters} {Numbers}";
    public string Compact => Province + Letters + Numbers;
    public static TrConstrainedResult Invalid => new(false, "", "", "", 0);
}

/// <summary>
/// Türkiye plaka dilbilgisi ile KISITLI çözümleme. OCR'ın slot-başına olasılık dağılımını + geçerli TR
/// yapısını kullanıp en olası GEÇERLİ plakayı seçer:
///   [il 2 rakam, 01-81] [1-3 harf, A-Z\{Q,W,X}] [2-5 rakam] [kalan slotlar = pad].
/// Her pozisyonda yalnızca İZİNLİ karakterler değerlendirildiği için `8/B`, `0/O`, `1/I`, `5/S` gibi
/// karışıklıklar POZİSYONA göre otomatik çözülür. Arama küçük (birkaç yapı × slot) → ~sıfır CPU.
///
/// Girdi `slotProbs`: uzunluk slots×vocab, satır-öncelikli (slot sonra vocab); tek kare için olasılık,
/// çok-kare için ortalama (0..1) verilmeli (güven skorunun anlamlı olması için normalize edilmiş).
/// </summary>
public static class TrConstrainedDecoder
{
    // Geçerli kombinasyonlar: harf sayısı -> izinli rakam sayıları
    private static readonly (int L, int D)[] Combos =
    {
        (1, 4), (1, 5), (2, 3), (2, 4), (3, 2), (3, 3),
    };

    public static TrConstrainedResult Decode(float[] slotProbs, string alphabet, int slots)
        => Decode(slotProbs, alphabet, slots, skipCharLevel: 0f);

    /// <summary>
    /// skipCharLevel > 0: OpenALPR'ın SKIP_CHAR fikri — bir slotun en güçlü karakter olasılığı bu
    /// eşiğin ALTINDAysa, o slot için "karakter yok" (pad) hipotezi max(p_pad, skipLevel - p_topChar)
    /// tabanıyla güçlendirilir. Plaka kenarı artefaktlarının ürettiği hayalet son haneyi (24ZEF49_
    /// vakası) kısa-uzunluk kombinasyonunun kazanmasına izin vererek düşürür. 0 = kapalı.
    /// </summary>
    public static TrConstrainedResult Decode(float[] slotProbs, string alphabet, int slots, float skipCharLevel)
        => Decode(slotProbs, alphabet, slots, skipCharLevel, collector: null);

    private static TrConstrainedResult Decode(
        float[] slotProbs, string alphabet, int slots, float skipCharLevel,
        Action<double, TrConstrainedResult>? collector)
    {
        int vocab = alphabet.Length;
        if (vocab == 0 || slots < 5 || slotProbs.Length < slots * vocab) return TrConstrainedResult.Invalid;

        // Karakter -> vocab indeksi
        int[] digitIdx = new int[10];
        for (int d = 0; d < 10; d++) digitIdx[d] = alphabet.IndexOf((char)('0' + d));
        var letterIdx = new List<(char ch, int idx)>();
        foreach (char c in "ABCDEFGHIJKLMNOPRSTUVYZ") // Q,W,X yok
        {
            int i = alphabet.IndexOf(c);
            if (i >= 0) letterIdx.Add((c, i));
        }
        int padIdx = alphabet.IndexOf('_');
        if (padIdx < 0) padIdx = vocab - 1;
        foreach (var d in digitIdx) if (d < 0) return TrConstrainedResult.Invalid;

        double LogP(int slot, int idx) => Math.Log(slotProbs[slot * vocab + idx] + 1e-6);

        // Pad (karakter yok) hipotezinin etkin log-olasılığı. skipCharLevel açıkken: slottaki EN GÜÇLÜ
        // pad-dışı karakter eşiğin ALTINDAysa, "burada karakter yok" hipotezi en az skipCharLevel
        // olasılığında sayılır — kenar artefaktının ürettiği eşik-altı hayalet hane, kısa-uzunluk
        // kombinasyonuna yenilir. Eşik ÜSTÜ karakter kanıtı varsa taban devreye girmez (temiz plaka etkilenmez).
        double PadLogP(int slot)
        {
            double padP = slotProbs[slot * vocab + padIdx];
            if (skipCharLevel > 0f && padP < skipCharLevel)
            {
                float top = 0f;
                int start = slot * vocab;
                for (int c = 0; c < vocab; c++)
                    if (c != padIdx && slotProbs[start + c] > top) top = slotProbs[start + c];
                if (top < skipCharLevel) padP = skipCharLevel;
            }
            return Math.Log(padP + 1e-6);
        }

        // İl (slot 0,1): 01-81 aralığında en yüksek olasılıklı rakam çifti.
        double provScore = double.NegativeInfinity; int bd0 = 0, bd1 = 1;
        for (int d0 = 0; d0 <= 8; d0++)
            for (int d1 = 0; d1 <= 9; d1++)
            {
                int prov = d0 * 10 + d1;
                if (prov < 1 || prov > 81) continue;
                double s = LogP(0, digitIdx[d0]) + LogP(1, digitIdx[d1]);
                if (s > provScore) { provScore = s; bd0 = d0; bd1 = d1; }
            }

        double bestTotal = double.NegativeInfinity;
        TrConstrainedResult best = TrConstrainedResult.Invalid;

        foreach (var (L, D) in Combos)
        {
            int used = 2 + L + D;
            if (used > slots) continue;

            double score = provScore;
            double nonPadLogSum = provScore; // il rakamları da güvene dahil
            var letters = new char[L];
            var numbers = new char[D];

            // Harf slotları
            for (int i = 0; i < L; i++)
            {
                int slot = 2 + i;
                double bestP = double.NegativeInfinity; char bestC = 'A';
                foreach (var (ch, idx) in letterIdx)
                {
                    double p = LogP(slot, idx);
                    if (p > bestP) { bestP = p; bestC = ch; }
                }
                letters[i] = bestC; score += bestP; nonPadLogSum += bestP;
            }

            // Rakam slotları
            for (int i = 0; i < D; i++)
            {
                int slot = 2 + L + i;
                double bestP = double.NegativeInfinity; int bestD = 0;
                for (int d = 0; d < 10; d++)
                {
                    double p = LogP(slot, digitIdx[d]);
                    if (p > bestP) { bestP = p; bestD = d; }
                }
                numbers[i] = (char)('0' + bestD); score += bestP; nonPadLogSum += bestP;
            }

            // Kalan slotlar pad olmalı — pad olasılığı skora katılır (doğru uzunluğu ödüllendirir).
            // SKIP_CHAR: zayıf hayalet karakterli slotta pad hipotezi desteklenir (PadLogP).
            for (int slot = used; slot < slots; slot++)
                score += PadLogP(slot);

            // GÜVEN, UZUNLUKTAN BAĞIMSIZ olmalı: skora katılan pad slotları güvene de katılır.
            // (2026-07-28, canlı CRITICAL bulgu) Eskiden yalnız nonPad slotların ortalaması alınıyordu;
            // bu, bir hane DÜŞÜREN kısa çözümü ödüllendiriyordu — zayıf slot ortalamadan çıkarıldığı için
            // güven YÜKSELİYORDU. Aynı dağılımda: "25 FGB 718" conf 0.825 iken "25 FGB 71" conf 0.990.
            // Sonuç zincirleme felaketti: (a) kesik plaka LowConfidence=false ile yayınlanıp bariyeri
            // VAR OLMAYAN plakayla açıyordu, (b) doğrusu whitelist'te olduğu hâlde snap kapısı
            // (ocr < SnapMaxOcrConfidence = 0.99) şişmiş güven yüzünden hiç çalışmıyordu — yani hatayı,
            // hatanın kendi ürettiği güven onarımdan koruyordu. Canlı kanıt: 25FAU18 (gerçek 25 FAU 185),
            // 25FYV74 (25 FYV 744), 25FGB71 (25 FGB 718) — üçü de kanıt görselinden gözle doğrulandı.
            // Pad'den EMİN olunan gerçek kısa plakalar cezalanmaz (padP≈1 → log≈0), yalnız hane
            // düşürerek kazanılan sahte güven ortadan kalkar.
            float conf = (float)Math.Exp(score / slots);   // tüm slotlar üzerinden geometrik ortalama
            var result = new TrConstrainedResult(true, $"{bd0}{bd1}", new string(letters), new string(numbers), conf);
            collector?.Invoke(score, result);

            if (score > bestTotal)
            {
                bestTotal = score;
                best = result;
            }
        }

        return best;
    }

    /// <summary>
    /// En iyi K GEÇERLİ aday (skora göre sıralı; ilk eleman Decode ile aynıdır). Her TR uzunluk
    /// kombinasyonunun en olası dizilimi bir adaydır — whitelist toleransı ve emit-sonrası
    /// varyant bastırma (aynı aracın farklı-uzunluk yanlış okumaları) için kullanılır.
    /// </summary>
    public static IReadOnlyList<TrConstrainedResult> DecodeTopK(
        float[] slotProbs, string alphabet, int slots, int k, float skipCharLevel = 0f)
    {
        var all = new List<(double Score, TrConstrainedResult R)>();
        Decode(slotProbs, alphabet, slots, skipCharLevel, (s, r) => all.Add((s, r)));
        all.Sort((a, b) => b.Score.CompareTo(a.Score));
        var list = new List<TrConstrainedResult>(Math.Min(k, all.Count));
        foreach (var (_, r) in all)
        {
            if (list.Count >= k) break;
            if (!list.Exists(x => x.Compact == r.Compact)) list.Add(r);
        }
        return list;
    }
}
