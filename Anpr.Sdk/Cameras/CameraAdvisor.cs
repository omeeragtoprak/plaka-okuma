using System.Globalization;

namespace Anpr.Sdk.Cameras;

public enum AdviceSeverity { Info, Warning }

/// <summary>
/// Tek bir kamera ayarı önerisi. Kütüphane kamera ayarını DEĞİŞTİRMEZ — öneri insan içindir.
/// </summary>
/// <param name="Code">Kararlı kimlik (ör. "shutter-blur").</param>
/// <param name="Basis">Dayanak: kaynak ve tarih ya da bu depodaki ölçüm.</param>
/// <param name="MeasuredHere">Öneri bu projede ÖLÇÜLMÜŞ bir sonuca mı dayanıyor (false = dış kaynak / akıl yürütme).</param>
public sealed record CameraAdvice(string Code, AdviceSeverity Severity, string Title, string Detail, string Basis, bool MeasuredHere);

/// <summary>
/// Okunan kamera ayarlarından ve sahanın kendi ölçümlerinden ANPR önerileri üretir (saf fonksiyon).
/// <para>Dürüstlük kuralları: sayı veren her öneri hesabını gösterir; varsayım (araç hızı) açıkça yazılır; kaynaklar
/// çelişiyorsa ikisi de anılır; ölçülmemiş etki "ölçülmedi" diye etiketlenir.</para>
/// </summary>
public static class CameraAdvisor
{
    /// <summary>TR standart tek satır plaka genişliği (m) — Resmî Gazete 30318, 110×520 mm.</summary>
    public const double StandardPlateWidthMeters = 0.52;

    /// <summary>Piksel bulanıklığı bu kadar olaydan az ölçümle hesaplanmaz (2 olayın orta değeri "ölçüm" değildir).</summary>
    public const int MinEventsForPixelBlur = 10;

    // Kültür nesnesi DEĞİL: PMS "InvariantGlobalization" ile çalışıyorsa tr-TR kültürü yüklenemez ve fırlatır.
    private static readonly NumberFormatInfo Tr = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
    private static string N(double v, int digits = 1) => v.ToString("N" + digits, Tr);
    private static string Shutter(double seconds) => seconds >= 1 ? N(seconds) + " s" : "1/" + Math.Round(1 / seconds).ToString(Tr);

    /// <param name="stream">Akış ayarları (ISAPI ya da ffprobe'dan; null olabilir).</param>
    /// <param name="image">Görüntü ayarları (yalnız üretici API'siyle okunabilir; null olabilir).</param>
    /// <param name="widths">Bu kameranın son olaylarındaki plaka genişliği dağılımı (null = henüz olay yok).</param>
    /// <param name="processedFps">Kütüphanenin kullandığı kaynak hızı (ör. 5).</param>
    /// <param name="assumedSpeedKmh">Bulanıklık hesabı için VARSAYILAN araç hızı (sahada ölçülmedi).</param>
    public static IReadOnlyList<CameraAdvice> Evaluate(CameraStreamSettings? stream, CameraImageSettings? image,
        PlateWidthStats? widths, double processedFps, double assumedSpeedKmh = 15)
    {
        var list = new List<CameraAdvice>();

        if (image?.ShutterSeconds is { } t && assumedSpeedKmh > 0)
            list.Add(ShutterAdvice(t, image, widths, assumedSpeedKmh));

        if (widths is { Count: >= 20 } w && w.P10 < 100)
            list.Add(new CameraAdvice("plate-small", AdviceSeverity.Warning,
                "Plakaların bir kısmı küçük görünüyor",
                $"Son {w.Count} olayın %10'unda plaka {w.P10} px'ten dar (orta değer {w.P50} px). Bu projede gözle doğrulanmış " +
                "olaylarda 100 px altındaki plakaların doğruluğu %64–76, 200 px üstünde %96–98 ölçüldü. Plakayı büyütmek (zoom, kamera " +
                "konumu, odak) doğruluğu artırır ama bedeli var: dar kadraj aracın görüntüde kalma süresini, yani okunabilecek kare " +
                "sayısını azaltır (bu projede 20 km/s için kare bütçesi 3,7'den 2,8'e düşer diye HESAPLANDI, ölçülmedi). Okuma eşiği " +
                "koymak doğruluğu artırmaz, yalnız okumayı keser.",
                "Bu depo: §P.4 (291)/(479) doz-yanıt ölçümü", MeasuredHere: true));

        if (stream?.Fps is { } fps && processedFps > 0 && fps > 2 * processedFps)
        {
            string ten = stream.FpsOptions?.Contains(10) == true ? " Kamera 10 fps'i destekliyor." : "";
            list.Add(new CameraAdvice("fps-oversupply", AdviceSeverity.Info,
                "Kamera kullanılandan çok kare gönderiyor",
                $"Kamera {N(fps, 0)} fps gönderiyor, kütüphane {N(processedFps, 0)} fps kullanıyor; ffmpeg yine de gelen karelerin " +
                "hepsini çözer. Akış hızını düşürmek (ör. 10 fps) çözme yükünü azaltır — azalmanın oranı bu projede ölçülmedi." + ten +
                $" 5 fps'in altına inmeyin: ölçüldü, 4 fps'te 93 araçtan 12'si ikinci okumasını kaybediyor.",
                // Önerinin ASIL iddiası (çözme kazancı) ölçülmedi; ölçülen yalnız alt sınır → "ölçüldü" rozeti yanıltırdı.
                "Bu depo: §P.4 (508) gate_sim (yalnız 5 fps alt sınırı ölçüldü); çözme kazancı ölçülmedi", MeasuredHere: false));
        }

        if (stream is { Width: > 0, Height: > 0, Fps: > 0, BitrateKbps: > 0 })
        {
            double bpp = stream.BitrateKbps!.Value * 1000.0 / ((double)stream.Width!.Value * stream.Height!.Value * stream.Fps!.Value);
            if (bpp < 0.05)
            {
                string room = stream.MaxBitrateKbps is { } max && max > stream.BitrateKbps
                    ? $" Kamera {max} kbps'ye kadar izin veriyor (şu an {stream.BitrateKbps})." : "";
                // VBR'de BitrateKbps üst sınırdır → gerçek bit/piksel EN FAZLA bu kadardır.
                string upTo = string.Equals(stream.BitrateMode, "VBR", StringComparison.OrdinalIgnoreCase) ? "en fazla " : "";
                list.Add(new CameraAdvice("low-bitrate", AdviceSeverity.Info,
                    "Sıkıştırma yüksek",
                    $"Piksel başına {upTo}{N(bpp, 3)} bit ({stream.BitrateKbps} kbps{(upTo.Length > 0 ? " üst sınır" : "")}, " +
                    $"{stream.Width}×{stream.Height}, {N(stream.Fps.Value, 0)} fps).{room} " +
                    "Bit hızını artırmak çözünürlüğü değiştirmez, yalnız sıkıştırma bozulmasını azaltır; plaka okumaya etkisi bu projede " +
                    "ölçülmedi ve çözme maliyetini biraz artırır.",
                    "Bu depo: §P.4 (342) salt-okunur akış incelemesi (#88)", MeasuredHere: false));
            }
        }

        if (stream?.SmartCodec == true)
            list.Add(new CameraAdvice("smart-codec", AdviceSeverity.Info,
                "Akıllı kodek açık",
                "Üreticinin akıllı kodeği (H.265+/H.264+) sahne durağanken kaliteyi ve anahtar kare aralığını değiştirebilir. ANPR " +
                "akışında kapatılması önerisini yalnız tek bir resmî olmayan kaynakta bulduk (doğrulanmadı); bu sahada etkisi ölçülmedi.",
                "Dış kaynak (doğrulanmadı): üretici/entegratör notları, 2026-10-27 araştırması", MeasuredHere: false));

        if (image?.TemporalNoiseReductionLevel is >= 50)
            list.Add(new CameraAdvice("temporal-nr", AdviceSeverity.Info,
                "Kareler arası (3D) gürültü azaltma orta/yüksek",
                $"3D gürültü azaltma seviyesi {image.TemporalNoiseReductionLevel}. Yüksek seviye hareketli nesnede iz bırakabilir " +
                "(plaka karakterleri bulaşabilir). Etki bu sahada ölçülmedi.",
                "Dış kaynak (doğrulanmadı): forum/entegratör gözlemleri", MeasuredHere: false));

        if (stream?.Codec is { } codec && (codec.Contains("265") || codec.Equals("hevc", StringComparison.OrdinalIgnoreCase)))
            list.Add(new CameraAdvice("hevc-decode", AdviceSeverity.Info,
                "Akış H.265 (HEVC)",
                "Eski Intel işlemcilerde (6. nesil öncesi) HEVC için donanım çözme yoktur; yazılımla çözme pahalıdır. Zayıf bilgisayarda " +
                "işlemci yüksekse önce ffmpeg'in payına bakın; H.264'e geçişin etkisi bu projede ölçülmedi.",
                "Dış kaynak: Intel Quick Sync kodek tablosu (2026-10-27 araştırması)", MeasuredHere: false));

        if (image == null)
            list.Add(new CameraAdvice("no-imaging", AdviceSeverity.Info,
                "Görüntü ayarları okunamadı",
                "Deklanşör/kazanç/WDR gibi ayarlar yalnız üretici API'siyle okunabilir (şu an Hikvision ISAPI destekleniyor; ONVIF " +
                "okuyucu henüz yok). Akış bilgisi yine de RTSP üzerinden okunur.",
                "—", MeasuredHere: false));

        return list;
    }

    private static CameraAdvice ShutterAdvice(double t, CameraImageSettings image, PlateWidthStats? widths, double kmh)
    {
        double v = kmh / 3.6;
        double travelCm = v * t * 100;
        double tMax = 1.0 / (11 * kmh);        // Milestone: t ≤ 1/(11·km/s) ⇔ pozlamada ≤ ~2,5 cm yol
        string pxPart = "";
        bool measured = widths is { Count: >= MinEventsForPixelBlur };
        if (measured)
        {
            var w = widths!.Value;
            double pxPerM = w.P50 / StandardPlateWidthMeters;
            double BlurPx(double s) => v * s * pxPerM;
            // Karşılaştırma: formülü karşılayan en uzun değer + yaygın 1/250 ve 1/500 (kamera destekliyorsa).
            var opts = (image.ShutterOptionsSeconds ?? Array.Empty<double>()).Where(o => o < t).ToArray();
            var picks = opts.Where(o => o <= tMax).OrderByDescending(o => o).Take(1)
                .Concat(opts.Where(o => Math.Abs(o - 1.0 / 250) < 1e-9 || Math.Abs(o - 1.0 / 500) < 1e-9))
                .Distinct().OrderByDescending(o => o)
                .Select(o => $"{Shutter(o)} ≈ {N(BlurPx(o))} px").ToArray();
            pxPart = $" Son {w.Count} olayda plakalar ~{w.P50} px genişlikte (≈{N(pxPerM, 0)} px/m); bu, en kötü durumda (yana doğru " +
                     $"hareket) ≈{N(BlurPx(t))} px bulanıklık demek, kameraya doğru gelen araçta daha az." +
                     (picks.Length > 0 ? " Kameranın desteklediği daha kısa değerler: " + string.Join(", ", picks) + "." : "");
        }
        bool over = t > tMax;
        return new CameraAdvice("shutter-blur", over ? AdviceSeverity.Warning : AdviceSeverity.Info,
            over ? "Deklanşör hareket bulanıklığı için uzun olabilir" : "Deklanşör",
            $"Deklanşör {image.Shutter ?? Shutter(t)}{(image.ExposureMode is { } m ? $" (pozlama: {m}; otomatik kipte çoğu kamerada üst sınırdır)" : "")}. " +
            $"{N(kmh, 0)} km/s'teki araç pozlama süresinde {N(travelCm)} cm ilerler.{pxPart} " +
            $"Kaynaklar farklı: Milestone formülü {N(kmh, 0)} km/s için {Shutter(tMax)} ya da daha kısa diyor; Hikvision ANPR SSS 30 km/s " +
            "altı için 1/150–1/200 aralığını kabul ediyor. Daha kısa deklanşörün bedeli: gece görüntü kararır, kamera kazancı (gürültü) " +
            $"artar — değiştirmeden önce gece kaydıyla deneyin. Hız {N(kmh, 0)} km/s VARSAYIMDIR, sahada ölçülmedi.",
            "Milestone XProtect LPR (t ≤ 1/(11·km/s)); Hikvision ANPR Camera FAQ V2.1 (2024-07-22)" +
            (measured ? "; px hesabı bu kameranın ölçülmüş plaka genişliğinden (yalnız ölçek ölçüldü)" : ""),
            // Asıl iddia (uyarı düzeyi) formüle ve VARSAYILAN hıza dayanır; ölçülen yalnız px ölçeği → "ölçüldü" denmez.
            MeasuredHere: false);
    }
}
