using Microsoft.ML.OnnxRuntime;

namespace Anpr.Core.Onnx;

/// <summary>
/// ONNX OTURUM PAYLAŞIMI (2.0.0): aynı model dosyası aynı ayarlarla bir kez yüklenir; kaç motor (kamera)
/// isterse istesin tek <see cref="InferenceSession"/> kullanılır.
/// <para><b>Neden (2026-10-13 ölçümü):</b> her kamera kendi motorunu, her motor dört oturumunu (dedektör, OCR,
/// rektifikasyon, süper çözünürlük) ayrı yüklüyordu. Oturum başına bellek (ağırlıklar + çalışma alanı):
/// dedektör ~68 MB, OCR ~18 MB, rektifikasyon ~42 MB, SR ~9 MB → kamera başına ~135 MB. İki kamerada servis
/// belleğinin ~300 MB'ı yerel (GC dışı) bellekti. Paylaşımla ikinci ve sonraki kameralar bu bedeli ödemez.</para>
/// <para><b>Güvenlik:</b> <c>InferenceSession.Run</c> ONNX Runtime'da eşzamanlı çağrıya açıktır (oturum durumu
/// salt-okunurdur; her çağrı kendi tensörlerini kullanır). Paylaşılan yalnız oturumdur — girdi tamponları ve
/// son-işleme her sarmalayıcıda (kamera başına) ayrı kalır. Sonuçlar paylaşımsız yolla bit düzeyinde aynıdır
/// (bkz. <c>OnnxSessionPoolTests</c>).</para>
/// <para>Anahtar: tam yol + dosya boyutu + son yazma zamanı + ayar imzası. Model dosyası diskte değişirse ya da
/// iş parçacığı ayarı farklıysa AYRI oturum açılır (yanlış paylaşım olmaz).</para>
/// </summary>
internal static class OnnxSessionPool
{
    private sealed class Entry
    {
        public required InferenceSession Session;
        public required string Key;
        public int Refs;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> ByKey = new(StringComparer.Ordinal);

    /// <summary>Havuzdaki (paylaşılan) açık oturum sayısı. Teşhis.</summary>
    internal static int Count { get { lock (Gate) return ByKey.Count; } }

    /// <summary>Bu oturumu kaç kullanıcı tutuyor (havuzda değilse 0). Test: sayım BU oturuma özeldir, süreçteki
    /// diğer motorlardan etkilenmez.</summary>
    internal static int RefCount(InferenceSession session)
    {
        lock (Gate)
        {
            foreach (var e in ByKey.Values) if (ReferenceEquals(e.Session, session)) return e.Refs;
            return 0;
        }
    }

    /// <summary>
    /// Oturum alır. <paramref name="shareKey"/> null ise paylaşım yoktur: her çağrı yeni oturum açar (eski davranış).
    /// Paylaşılan oturum zaten varsa <paramref name="options"/> kullanılmaz ve burada serbest bırakılır.
    /// Her <see cref="Acquire"/> için bir <see cref="Release"/> çağrılmalıdır.
    /// </summary>
    internal static InferenceSession Acquire(string fullPath, SessionOptions options, string? shareKey)
    {
        if (shareKey == null) return new InferenceSession(fullPath, options);

        var fi = new FileInfo(fullPath);
        string key = $"{fullPath}|{(fi.Exists ? fi.Length : -1)}|{(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0)}|{shareKey}";
        lock (Gate)
        {
            if (ByKey.TryGetValue(key, out var e))
            {
                e.Refs++;
                options.Dispose();   // kullanılmadı
                return e.Session;
            }
            // Yükleme kilit altında: aynı modeli iki kamera aynı anda isterse ikincisi bekler ve HAZIR oturumu alır
            // (kilitsiz olsaydı ikisi de yükler, biri çöpe giderdi — açılışta bir anlık çift bellek).
            var s = new InferenceSession(fullPath, options);
            ByKey[key] = new Entry { Session = s, Key = key, Refs = 1 };
            return s;
        }
    }

    /// <summary>Oturumu bırakır; son kullanıcı bıraktığında oturum kapatılır. Havuzda olmayan oturum doğrudan kapatılır.</summary>
    internal static void Release(InferenceSession session)
    {
        lock (Gate)
        {
            foreach (var e in ByKey.Values)
            {
                if (!ReferenceEquals(e.Session, session)) continue;
                if (--e.Refs > 0) return;
                ByKey.Remove(e.Key);
                break;
            }
        }
        session.Dispose();
    }
}
