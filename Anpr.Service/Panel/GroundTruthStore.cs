using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anpr.Service.Panel;

/// <summary>
/// Gözle doğrulama kayıtlarının kalıcılığı: <c>ground-truth.jsonl</c> (uygulama dizini, ekleme-only).
///
/// <para><b>Mevcut <c>corrections.jsonl</c>'den farkı — ve neden gerekli:</b> o dosya YALNIZ
/// düzeltmeleri tutar, onayları tutmaz. Yani "kaç okuma kontrol edildi" bilinmediği için doğruluk
/// oranının PAYDASI yoktur ve örneklem taraflıdır (yalnız birinin fark ettiği hatalar girer).
/// Ölçüm: 1308 roi olayında 2,5 haftada 1 düzeltme / 0 onay → hesaplanabilir doğruluk: yok.
/// Burada her iki verdict de yazılır, dolayısıyla oran hesaplanabilir.</para>
///
/// <para><b>KVKK:</b> dosya plaka metni içerir — <c>events.jsonl</c> ile aynı saklama/erişim
/// kuralına tabidir ve repoya GİRMEZ (gitignored dizinde üretilir).</para>
/// </summary>
public sealed class GroundTruthStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _root;
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, GroundTruthEntry> _byEvent = new();

    public GroundTruthStore(string? directory = null)
    {
        _root = directory ?? AppContext.BaseDirectory;
        _path = Path.Combine(_root, "ground-truth.jsonl");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<GroundTruthEntry>(line, Json);
                if (e != null) _byEvent[e.EventId] = e;   // aynı olay tekrar doğrulanırsa SON kayıt geçerli
            }
            catch (JsonException)
            {
                // Bozuk satır tüm geçmişi kaybettirmemeli; atla.
            }
        }
    }

    /// <summary>Bu olay daha önce doğrulandı mı?</summary>
    public bool IsVerified(Guid eventId)
    {
        lock (_lock) return _byEvent.ContainsKey(eventId);
    }

    /// <summary>Tüm doğrulama kayıtları (zaman sırasız).</summary>
    public IReadOnlyList<GroundTruthEntry> All()
    {
        lock (_lock) return _byEvent.Values.ToList();
    }

    /// <summary>Bant başına doğrulanmış kayıt sayısı — tabakalı seçim bunu kullanır.</summary>
    public IReadOnlyDictionary<ConfBand, int> VerifiedPerBand()
    {
        lock (_lock)
            return _byEvent.Values.GroupBy(v => GroundTruth.BandOf(v.Ocr))
                                  .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Kaydı ekler (dosyaya da yazar). Aynı olayın önceki kaydını EZER (düzeltme mümkün).</summary>
    public void Record(GroundTruthEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _byEvent[entry.EventId] = entry;
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
        }
        ArchiveEvidence(entry);
    }

    /// <summary>Korumalı etiketli set dizini (kanıt budayıcısı burayı TARAMAZ).</summary>
    public string ArchiveRoot => Path.Combine(_root, "dataset-groundtruth");

    /// <summary>
    /// Doğrulanan olayın kanıt görselini KORUMALI dizine kopyalar.
    ///
    /// <para><b>Neden zorunlu:</b> kanıt dizini <c>EvidenceMaxFiles</c> (6000) ve
    /// <c>EvidenceRetentionDays</c> ile budanır; ölçülen efektif saklama ~2,6 GÜN. Yani gözle
    /// doğrulanan bir kaydın görseli günler içinde SİLİNİR ve harcanan insan emeği buharlaşır —
    /// geriye yalnız "şu plaka doğruydu" satırı kalır, ama bir daha ne denetlenebilir ne A/B yapılabilir.
    /// Budayıcı <c>evidence/</c> dizinini tarar; buraya KOPYALAMAK tek satır SDK değişikliği
    /// gerektirmeden koruma sağlar.</para>
    ///
    /// <para><b>İkinci faydası:</b> bu dizin aynı zamanda KALICI ETİKETLİ TEST SETİdir —
    /// <c>&lt;gerçek-plaka&gt;/&lt;dosya&gt;</c> yapısıyla birikir ve <c>Anpr.Bench cropab</c> için
    /// doğrudan kullanılabilir hâle gelir. Yani doğrulama emeği İKİ işe yarar (precision ölçümü +
    /// regresyon seti). Eski etiketli setlerin (dataset/{live1h,s1,s2}) silinmiş olması bu yüzden
    /// tekrar etmemeli.</para>
    ///
    /// <para>Hata durumunda SESSİZ geçilir: arşivleme başarısız olsa bile verdict kaydı kaybolmamalı.</para>
    /// </summary>
    private void ArchiveEvidence(GroundTruthEntry e)
    {
        if (e.Evidence is null) return;
        // Etiket: doğru okuduysak kendi okumamız, yanlışsa GERÇEK plaka. Okunamadıysa ayrı kova.
        string label = e.Verdict switch
        {
            GroundTruthVerdict.Yanlis when !string.IsNullOrWhiteSpace(e.Truth) => e.Truth!,
            GroundTruthVerdict.Okunamadi => "_okunamadi",
            // Doğrusu bilinmediği için plaka etiketi altına KONULAMAZ — set kirlenir.
            GroundTruthVerdict.YanlisBelirsiz => "_yanlis_belirsiz",
            _ => e.Ours,
        };
        if (string.IsNullOrWhiteSpace(label)) return;

        try
        {
            string dir = Path.Combine(ArchiveRoot, label);
            Directory.CreateDirectory(dir);
            CopyIfExists(e.Evidence, dir);
            // Tam kare de kopyalanır: kırpma/kadraj hatalarını sonradan incelemenin TEK yolu
            // (34ZH7641 vakası tam kareden çözülmüştü).
            CopyIfExists(e.Evidence.Replace(".jpg", "_full.jpg", StringComparison.OrdinalIgnoreCase), dir);
        }
        catch (IOException) { /* arşiv başarısız olsa da verdict kaydı korunur */ }
        catch (UnauthorizedAccessException) { }
    }

    private void CopyIfExists(string relative, string targetDir)
    {
        string src = Path.Combine(_root, relative);
        if (!File.Exists(src)) return;
        File.Copy(src, Path.Combine(targetDir, Path.GetFileName(src)), overwrite: true);
    }
}
