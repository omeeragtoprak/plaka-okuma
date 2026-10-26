using System.Text;

namespace Anpr.Core.TurkishPlates;

/// <summary>Türkiye plaka normalizasyon/doğrulama sonucu.</summary>
public sealed record TurkishPlateResult(
    bool IsValid,
    string? Normalized,   // "25 ABC 123" biçimi (geçerliyse)
    string Compact,       // boşluksuz biçim, ör. "25ABC123"
    int Province,         // il kodu (1..81), geçersizse 0
    string Letters,
    string Numbers,
    int Corrections);     // OCR düzeltme sayısı (0 = ham okuma zaten uygundu)

/// <summary>
/// Türkiye plaka formatı doğrulama + normalizasyon.
///
/// Format: [il 01-81] [1-3 harf] [2-5 rakam], geçerli kombinasyonlar:
///   1 harf -> 4-5 rakam   (ör. 34 A 1234)
///   2 harf -> 3-4 rakam   (ör. 34 AB 123)
///   3 harf -> 2-3 rakam   (ör. 34 ABC 12)
/// Türk plakalarında Q, W, X harfleri YOKTUR.
/// ⚠️ KOD DAVRANIŞI BUNDAN İNCE BİR FARKLA AYRILIR (2026-08-05 denetimi): `ForbiddenLetters`
/// yalnız {W, X} içerir; **Q reddedilmez, O'ya DÜZELTİLİR** (bkz. aşağıda "Q neredeyse her
/// zaman yanlış okunan O"). Bu bilinçli bir seçim — Q'yu reddetmek, yanlış okunmuş bir O
/// yüzünden geçerli plakayı atardı. Yorum, kodu yanlış tarif ediyordu; düzeltildi.
///
/// 🌐 OTORİTER KAYNAKLA DOĞRULANDI (2026-08-05, tr.wikipedia "Türkiye taşıt plaka kodu"):
/// yukarıdaki üç kombinasyon sivil plakaların TAMAMINI kapsar; resmî (99 AA 999), polis
/// (99 A 99999) ve jandarma (99 JAA 999) biçimleri de aynı kurala uyar.
/// 🔴 KAPSANMAYAN BİÇİMLER (2026-08-06'da tam liste çıkarıldı): vali "99 9999" (harfsiz, 6 krk) ·
/// TBMM "TBMM 999" (4 harf, il kodu yok) · cumhurbaşkanlığı "CB 999" · askerî (tamamen rakam,
/// ör. "002565") · iş makinesi (11 hane). Hepsi uzunluk/harf kapısına takılır ve okuma
/// `IsValidTr=false` döner → `CameraWorker` onu DÜŞÜRÜR (olay üretilmez, kanıt saklanmaz).
///
/// ✅ AMA ZARAR YÖNÜ GÜVENLİ VE TESTLE TUTULUYOR: aşağıdaki `genuineLetters == 0` kapısı,
/// tamamen rakamlı bir dizginin geçerli bir plakaya ZORLANMASINI engeller (`0->O` ile harf
/// uydurma dâhil; `TurkishPlateNormalizerTests` 5 vaka). Yani başarısızlık **"okuma yok →
/// elle müdahale"** (gürültülü/operasyonel) olur, **asla "yanlış okuma → yanlış tahsilat"**
/// (sessiz/mali) olmaz. Bir kapı sisteminde tercih edilecek yön budur.
///
/// 📊 SIKLIK SINIRLANDI (dün "ölçülemez" demiştim — YANLIŞTI, bkz. §P.4 2026-08-06 (89)):
/// reddedilenler kendi loglarımıza yazılmıyor, ama BAĞIMSIZ kaynak (kameranın kendi ANPR'ı
/// bizim gramerimizden geçmez) sınırlayabiliyor. 10 günde 8562 kayıt → 5 aday dizgi, 4'ü
/// KANITLANMIŞ kamera yanlış okuması (her biri aynı geçişteki normal okumayla çürütüldü),
/// 1'i çözülemedi (`345160`, iki kamerada 8 sn arayla — gerçek geçiş imzası). Yani bu sahada
/// özel biçim sıklığı sıfıra yakın; protokol/askerî aracın düzenli girdiği bir sahada değil.
///
/// OCR hatalarını yalnızca formatın gerektirdiği pozisyonda düzeltir:
///   - İl + rakam bölgesinde harf->rakam (O->0, I->1, B->8, S->5, G->6, Z->2, T->7, D->0, L->1, A->4)
///   - Harf bölgesinde rakam->harf (0->O, 1->I, 2->Z, 4->A, 5->S, 6->G, 7->T, 8->B)
/// Çok fazla düzeltme gerekiyorsa (çöp okuma) GEÇERSİZ döner.
/// </summary>
public static class TurkishPlateNormalizer
{
    private const int MaxCorrections = 3;

    // Türk plakalarında kullanılmayan harfler
    private static readonly HashSet<char> ForbiddenLetters = new() { 'W', 'X' };

    // harf -> rakam (yalnızca rakam beklenen pozisyonlarda)
    private static readonly Dictionary<char, char> LetterToDigit = new()
    {
        ['O'] = '0', ['Q'] = '0', ['D'] = '0',
        ['I'] = '1', ['L'] = '1',
        ['Z'] = '2',
        ['A'] = '4',
        ['S'] = '5',
        ['G'] = '6',
        ['T'] = '7',
        ['B'] = '8',
    };

    // rakam -> harf (yalnızca harf beklenen pozisyonlarda)
    private static readonly Dictionary<char, char> DigitToLetter = new()
    {
        ['0'] = 'O',
        ['1'] = 'I',
        ['2'] = 'Z',
        ['4'] = 'A',
        ['5'] = 'S',
        ['6'] = 'G',
        ['7'] = 'T',
        ['8'] = 'B',
    };

    public static TurkishPlateResult Invalid(string compact) =>
        new(false, null, compact, 0, string.Empty, string.Empty, int.MaxValue);

    public static TurkishPlateResult Normalize(string? rawOcr)
    {
        string compact = Clean(rawOcr);

        // Toplam uzunluk 7-8 olmalı (2 il + 5-6 [harf+rakam]).
        if (compact.Length is < 7 or > 8)
            return Invalid(compact);

        // --- İl kodu: ilk 2 karakter rakama zorlanır ---
        if (!TryCoerceDigit(compact[0], out char p0, out int c0) ||
            !TryCoerceDigit(compact[1], out char p1, out int c1))
            return Invalid(compact);

        int province = (p0 - '0') * 10 + (p1 - '0');
        if (province is < 1 or > 81)
            return Invalid(compact);

        string rest = compact[2..];

        TurkishPlateResult? best = null;

        // Olası harf sayıları (kombinasyon kuralına uyanları dene)
        foreach (int lc in new[] { 2, 3, 1 })
        {
            int dc = rest.Length - lc;
            if (!IsValidCombo(lc, dc)) continue;

            if (TryBuild(province, $"{p0}{p1}", rest, lc, c0 + c1, out var candidate) &&
                (best is null || candidate.Corrections < best.Corrections))
            {
                best = candidate;
            }
        }

        if (best is null || best.Corrections > MaxCorrections)
            return Invalid(compact);

        return best;
    }

    private static bool TryBuild(int province, string provinceStr, string rest, int letterCount, int provinceCost, out TurkishPlateResult result)
    {
        result = Invalid(rest);
        var letters = new StringBuilder(letterCount);
        var numbers = new StringBuilder(rest.Length - letterCount);
        int corrections = provinceCost;
        int genuineLetters = 0; // rakamdan zorlanmamış, gerçek A-Z harf sayısı

        for (int i = 0; i < rest.Length; i++)
        {
            char ch = rest[i];
            if (i < letterCount)
            {
                if (!TryCoerceLetter(ch, out char lt, out int cost)) return false;
                letters.Append(lt);
                corrections += cost;
                if (cost == 0) genuineLetters++; // zaten harfti
            }
            else
            {
                if (!TryCoerceDigit(ch, out char dg, out int cost)) return false;
                numbers.Append(dg);
                corrections += cost;
            }
        }

        // Harf bloğunun TAMAMI rakamdan uydurulmuşsa (gerçek plakada olmaz) reddet:
        // "3412345" gibi tamamen sayısal çöpün geçerli sayılmasını engeller.
        if (genuineLetters == 0) return false;

        string lettersStr = letters.ToString();
        string numbersStr = numbers.ToString();
        string normalized = $"{provinceStr} {lettersStr} {numbersStr}";
        string compact = provinceStr + lettersStr + numbersStr;

        result = new TurkishPlateResult(true, normalized, compact, province, lettersStr, numbersStr, corrections);
        return true;
    }

    // lc harf -> dc rakam kombinasyonu geçerli mi?
    private static bool IsValidCombo(int lc, int dc) => lc switch
    {
        1 => dc is 4 or 5,
        2 => dc is 3 or 4,
        3 => dc is 2 or 3,
        _ => false,
    };

    private static bool TryCoerceDigit(char ch, out char digit, out int cost)
    {
        if (ch is >= '0' and <= '9') { digit = ch; cost = 0; return true; }
        if (LetterToDigit.TryGetValue(ch, out digit)) { cost = 1; return true; }
        digit = '\0'; cost = 0; return false;
    }

    private static bool TryCoerceLetter(char ch, out char letter, out int cost)
    {
        if (ch is >= 'A' and <= 'Z')
        {
            if (ForbiddenLetters.Contains(ch)) { letter = '\0'; cost = 0; return false; }
            if (ch == 'Q') { letter = 'O'; cost = 1; return true; } // Q neredeyse her zaman yanlış okunan O
            letter = ch; cost = 0; return true;
        }
        if (DigitToLetter.TryGetValue(ch, out letter)) { cost = 1; return true; }
        letter = '\0'; cost = 0; return false;
    }

    // Yalnızca A-Z ve 0-9 bırak; Türkçe karakterleri ASCII'ye indir.
    private static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (char c0 in raw)
        {
            char c = c0 switch
            {
                'İ' or 'I' or 'ı' or 'i' => 'I',
                'Ö' or 'ö' => 'O',
                'Ü' or 'ü' => 'U',
                'Ç' or 'ç' => 'C',
                'Ş' or 'ş' => 'S',
                'Ğ' or 'ğ' => 'G',
                _ => char.ToUpperInvariant(c0),
            };
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
