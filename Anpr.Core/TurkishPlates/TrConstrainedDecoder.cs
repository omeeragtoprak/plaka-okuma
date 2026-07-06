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
            for (int slot = used; slot < slots; slot++)
                score += LogP(slot, padIdx);

            if (score > bestTotal)
            {
                bestTotal = score;
                int nonPad = 2 + L + D;
                float conf = (float)Math.Exp(nonPadLogSum / nonPad); // geometrik ortalama olasılık
                best = new TrConstrainedResult(true, $"{bd0}{bd1}", new string(letters), new string(numbers), conf);
            }
        }

        return best;
    }
}
