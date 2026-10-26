using Anpr.Sdk.Frames;

namespace Anpr.Sdk.Workers;

/// <summary>Hareket kapısının bir kare için kararı.</summary>
/// <param name="Motion">Kare işlenmeli mi (hareket var / ilk kare / referans değişti).</param>
/// <param name="ChangedFraction">Örneklenen noktaların eşikten fazla değişen payı (0–1).</param>
/// <param name="FirstFrame">Karşılaştırma referansı yoktu (ilk kare, boyut ya da ROI değişimi).</param>
/// <param name="ChangedBox">Değişen ızgara hücrelerinin piksel cinsinden sınır kutusu; değişen hücre yoksa ya da
/// referans yoksa null. "Hareket nerede?" sorusunun cevabı — hareket odaklı bakış bu kutudan türetilir.</param>
internal readonly record struct GateDecision(bool Motion, double ChangedFraction, bool FirstFrame,
                                             (int X, int Y, int W, int H)? ChangedBox = null);

/// <summary>
/// HAREKET KAPISI (Faz 2). Ham BGRA karede, tetik bölgesinin (ROI) sınır kutusu üzerine seyrek bir ızgara
/// (varsayılan 64×36 = 2304 nokta) oturtur, her noktanın parlaklığını önceki değerlendirilen kareyle
/// karşılaştırır. Eşikten fazla değişen nokta payı <c>changedFraction</c>'ı aşarsa "hareket var" der.
/// <para><b>Neden:</b> bariyer kamerası günün büyük kısmında boş sahneye bakar; boşta bile saniyede
/// <c>IdleFps</c> kez dedektör koşturmak zayıf PC'nin sabit gideridir. Kapı, dedektörün maliyetinin
/// ~1/1000'i ile (2304 nokta okuma) kareyi işlemeye değer mi diye bakar.</para>
/// <para><b>Doğruluk güvencesi:</b> kapı yalnız "atla" kararını verebilir; ne okur ne düzeltir. Yanlış
/// "hareket var" kararı sadece CPU'ya maliyettir. Yanlış "hareket yok" kararı ise kare kaybıdır — bu yüzden
/// (a) ilk kare, boyut/ROI değişimi her zaman işlenir, (b) çağıran zorunlu bir periyotla (varsayılan 2 sn)
/// kapıyı ezer, (c) aktif izleme süresinde kapı devre dışıdır ve (d) kapı önce GÖLGE modunda çalışır:
/// karar sayılır ama uygulanmaz; "atlanacaktı ama plaka vardı" sayacı sıfır kalmadan açılmaz.</para>
/// <para>JPEG karede (eski MJPEG yolu) piksel erişimi çözme gerektirir; kapı o karede her zaman
/// "işle" der (maliyet eklemez). YUV karede parlaklık doğrudan Y düzleminden okunur (nokta başına tek bayt);
/// sınırlı aralıklı (16–235) Y'de eşik aynı görsel farka denk gelecek şekilde 219/255 ile ölçeklenir.</para>
/// </summary>
internal sealed class MotionGate
{
    private readonly int _cols, _rows, _pixelDelta;
    private readonly double _minChangedFraction;
    private byte[]? _prev, _cur;
    private int _prevW, _prevH;
    private (int X, int Y, int W, int H) _prevRect;
    // Çokgen maskesi: ROI çokgeninin DIŞINDA kalan ızgara hücreleri sayılmaz. Kapı ROI'nin sınır KUTUSUNA
    // bakar; kutunun çokgen dışında kalan köşelerindeki hareket (bitişik cadde, yaya yolu) kapıyı boşuna
    // tetikliyordu. Ölçüm (2026-09-30, giriş kamerası, 40 dk gündüz): hücrelerin %24'ü çokgen dışında ve
    // aynı eşikte işlenen kare %42,7 → %34,5 (hiçbir araç kaçmadan).
    private Anpr.Core.Imaging.RoiPolygon? _maskPoly;
    private bool[]? _mask;
    private int _activeCells;

    public MotionGate(int cols = 64, int rows = 36, int pixelDelta = 20, double changedFraction = 0.01)
    {
        if (cols < 4 || rows < 4) throw new ArgumentOutOfRangeException(nameof(cols), "ızgara en az 4×4");
        if (pixelDelta < 1 || pixelDelta > 254) throw new ArgumentOutOfRangeException(nameof(pixelDelta));
        if (changedFraction <= 0 || changedFraction > 1) throw new ArgumentOutOfRangeException(nameof(changedFraction));
        _cols = cols; _rows = rows; _pixelDelta = pixelDelta; _minChangedFraction = changedFraction;
    }

    public int SampleCount => _cols * _rows;

    /// <summary>Referansı sıfırlar (sonraki kare "ilk kare" sayılır) — kaynak yeniden bağlandığında çağrılabilir.</summary>
    public void Reset() { _prev = null; }

    /// <summary>
    /// Kareyi değerlendirir. <paramref name="roiRect"/> piksel cinsinden tetik bölgesi sınır kutusu (null = tüm kare).
    /// </summary>
    public GateDecision Evaluate(VideoFrame frame, (int X, int Y, int W, int H)? roiRect)
        => Evaluate(frame, roiRect, null);

    /// <summary>
    /// Kareyi değerlendirir; <paramref name="polygon"/> verilmişse yalnız çokgenin İÇİNDE kalan ızgara
    /// hücreleri sayılır (pay da payda da). Çokgen değişirse referans sıfırlanır.
    /// </summary>
    public GateDecision Evaluate(VideoFrame frame, (int X, int Y, int W, int H)? roiRect, Anpr.Core.Imaging.RoiPolygon? polygon)
    {
        if (!frame.IsRaw || frame.Width < 2 || frame.Height < 2)
            return new GateDecision(true, 1.0, true);

        var rect = ClampRect(roiRect ?? (0, 0, frame.Width, frame.Height), frame.Width, frame.Height);
        if (rect.W < 2 || rect.H < 2) rect = (0, 0, frame.Width, frame.Height);

        _cur ??= new byte[_cols * _rows];
        Sample(frame, rect, _cur);

        bool first = _prev == null || _prevW != frame.Width || _prevH != frame.Height || _prevRect != rect
                     || !ReferenceEquals(polygon, _maskPoly);
        if (first) BuildMask(polygon, rect, frame.Width, frame.Height);
        var mask = _mask;
        double fraction = 1.0;
        (int X, int Y, int W, int H)? box = null;
        if (!first)
        {
            // Sınırlı aralıklı YUV'de Y 16–235 arasındadır: aynı görsel fark 219/255 kat daha küçük sayıdır.
            int delta = frame.Format == VideoPixelFormat.Yuv420p && !frame.FullRange
                ? Math.Max(1, (int)Math.Round(_pixelDelta * 219.0 / 255.0))
                : _pixelDelta;
            int changed = 0, minC = int.MaxValue, maxC = -1, minR = int.MaxValue, maxR = -1;
            var prev = _prev!;
            for (int i = 0; i < _cur.Length; i++)
            {
                if (mask != null && !mask[i]) continue;                 // çokgen dışı hücre
                if (Math.Abs(_cur[i] - prev[i]) <= delta) continue;
                changed++;
                int c = i % _cols, r = i / _cols;
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
            }
            fraction = changed / (double)Math.Max(1, _activeCells);
            if (changed > 0)
            {
                // Hücre sınırları: sütun c → [c·G/sütun, (c+1)·G/sütun). Kutu, değişen hücrelerin tamamını kaplar.
                int x0 = rect.X + (int)((long)minC * rect.W / _cols), x1 = rect.X + (int)((long)(maxC + 1) * rect.W / _cols);
                int y0 = rect.Y + (int)((long)minR * rect.H / _rows), y1 = rect.Y + (int)((long)(maxR + 1) * rect.H / _rows);
                box = (x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
            }
        }

        // Tamponları değiştir: şimdiki, sonraki karşılaştırmanın referansı olur.
        (_prev, _cur) = (_cur, _prev ?? new byte[_cols * _rows]);
        _prevW = frame.Width; _prevH = frame.Height; _prevRect = rect;

        return new GateDecision(first || fraction >= _minChangedFraction, fraction, first, box);
    }

    /// <summary>Çokgen içindeki (sayılan) ızgara hücresi sayısı. Çokgen yoksa tüm hücreler.</summary>
    public int ActiveCells => _activeCells == 0 ? _cols * _rows : _activeCells;

    private void BuildMask(Anpr.Core.Imaging.RoiPolygon? polygon, (int X, int Y, int W, int H) r, int frameW, int frameH)
    {
        _maskPoly = polygon;
        int n = _cols * _rows;
        if (polygon == null) { _mask = null; _activeCells = n; return; }
        var m = new bool[n];
        int active = 0;
        for (int row = 0; row < _rows; row++)
        {
            float y = r.Y + (row + 0.5f) * r.H / _rows;
            for (int col = 0; col < _cols; col++)
            {
                float x = r.X + (col + 0.5f) * r.W / _cols;
                if (polygon.ContainsPixel(x, y, frameW, frameH)) { m[row * _cols + col] = true; active++; }
            }
        }
        // Çokgen ızgaraya hiç değmiyorsa (bozuk tanım) maske uygulanmaz: kapı kör kalmasın.
        if (active == 0) { _mask = null; _activeCells = n; return; }
        _mask = m; _activeCells = active;
    }

    private void Sample(VideoFrame f, (int X, int Y, int W, int H) r, byte[] dst)
    {
        var data = f.Data;
        if (f.Format == VideoPixelFormat.Yuv420p)
        {
            // Y düzlemi: nokta başına tek bayt (renk dönüşümü yok).
            for (int row = 0; row < _rows; row++)
            {
                int y = Math.Min(r.Y + (int)((row + 0.5) * r.H / _rows), f.Height - 1);
                int rowOff = f.PixelOffset + y * f.Width;
                int dstRow = row * _cols;
                for (int col = 0; col < _cols; col++)
                {
                    int x = r.X + (int)((col + 0.5) * r.W / _cols);
                    dst[dstRow + col] = data[rowOff + Math.Min(x, f.Width - 1)];
                }
            }
            return;
        }
        for (int row = 0; row < _rows; row++)
        {
            int y = r.Y + (int)((row + 0.5) * r.H / _rows);
            int rowOff = f.RowOffset(Math.Min(y, f.Height - 1));
            int dstRow = row * _cols;
            for (int col = 0; col < _cols; col++)
            {
                int x = r.X + (int)((col + 0.5) * r.W / _cols);
                int p = rowOff + Math.Min(x, f.Width - 1) * 4;   // B,G,R,A
                dst[dstRow + col] = (byte)((data[p + 2] * 77 + data[p + 1] * 150 + data[p] * 29) >> 8); // ITU-R 601 yaklaşık luma
            }
        }
    }

    internal static (int X, int Y, int W, int H) ClampRect((int X, int Y, int W, int H) r, int w, int h)
    {
        int x0 = Math.Clamp(r.X, 0, w - 1), y0 = Math.Clamp(r.Y, 0, h - 1);
        int x1 = Math.Clamp(r.X + r.W, x0, w), y1 = Math.Clamp(r.Y + r.H, y0, h);
        return (x0, y0, x1 - x0, y1 - y0);
    }
}
