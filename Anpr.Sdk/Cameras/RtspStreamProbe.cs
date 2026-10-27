using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Anpr.Sdk.Cameras;

/// <summary>
/// Satıcıdan BAĞIMSIZ akış bilgisi: RTSP akışının kendisini <c>ffprobe</c> ile yoklar (kodek, çözünürlük, kare hızı) —
/// her IP kamerada çalışır, kamera API'si ya da yönetici yetkisi gerektirmez, kameraya hiçbir şey YAZMAZ.
/// <para>⚠️ RTSP adresi çoğu zaman kullanıcı adı/şifre içerir: bu sınıf adresi hiçbir yere yazmaz, hata metinlerine koymaz.</para>
/// </summary>
public static class RtspStreamProbe
{
    /// <summary>Akışı yoklar; ulaşılamazsa ya da ffprobe yoksa <c>Stream = null</c> ve <c>Problem</c> doldurulur.</summary>
    public static async Task<(CameraStreamSettings? Stream, string? Problem)> ProbeAsync(
        string ffprobePath, string url, string rtspTransport = "tcp", TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(ffprobePath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("-rtsp_transport");
            psi.ArgumentList.Add(string.IsNullOrWhiteSpace(rtspTransport) ? "tcp" : rtspTransport);
        }
        foreach (var a in new[] { "-v", "error", "-select_streams", "v:0",
                                  "-show_entries", "stream=codec_name,width,height,avg_frame_rate,r_frame_rate,bit_rate",
                                  "-of", "json", url })
            psi.ArgumentList.Add(a);

        ct.ThrowIfCancellationRequested();   // iptal edilmiş çağrı süreç BAŞLATMAZ
        Process proc;
        try { proc = Process.Start(psi) ?? throw new InvalidOperationException(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, $"ffprobe çalıştırılamadı ('{ffprobePath}'): kurulu ve PATH'te mi?");
        }

        using (proc)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
            try
            {
                var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);
                string json = await stdout;
                await stderr;   // içeriği adres içerebilir → kullanılmaz
                if (proc.ExitCode != 0) return (null, $"ffprobe akışı okuyamadı (çıkış {proc.ExitCode}); adres/ağ/kimlik bilgisini kontrol edin.");
                var s = ParseFfprobeJson(json);
                return s == null ? (null, "ffprobe çıktısında video akışı yok.") : (s, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null, "ffprobe zaman aşımı (akışa ulaşılamadı).");
            }
            finally
            {
                // Zaman aşımı DA, çağıranın iptali DE: süreç geride kalmasın (panelde sayfa kapanınca istek iptal olur;
                // takılmış bir kamerada ffprobe'un kendi ağ zaman aşımı yoktur).
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }

    /// <summary><c>ffprobe -of json</c> çıktısından ilk video akışı. Bozuk/boş girdide <c>null</c>.</summary>
    internal static CameraStreamSettings? ParseFfprobeJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0) return null;
            var s = streams[0];
            int? Int(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
            string? Str(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            double? fps = ParseRate(Str("avg_frame_rate")) ?? ParseRate(Str("r_frame_rate"));
            int? kbps = long.TryParse(Str("bit_rate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bps) && bps > 0
                ? (int)(bps / 1000) : null;
            return new CameraStreamSettings { Codec = Str("codec_name"), Width = Int("width"), Height = Int("height"), Fps = fps, BitrateKbps = kbps };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>"25/1" → 25; "0/0" ya da tanınmayan → null.</summary>
    internal static double? ParseRate(string? r)
    {
        if (string.IsNullOrWhiteSpace(r)) return null;
        var p = r.Split('/');
        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
            double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && n > 0 && d > 0)
            return Math.Round(n / d, 3);
        return null;
    }

    /// <summary>ffmpeg yolundan ffprobe yolunu türetir ("C:\ff\bin\ffmpeg.exe" → "C:\ff\bin\ffprobe.exe"; "ffmpeg" → "ffprobe").</summary>
    public static string DeriveFfprobePath(string? ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath)) return "ffprobe";
        string dir = Path.GetDirectoryName(ffmpegPath) ?? "";
        string file = Path.GetFileName(ffmpegPath);
        string probe = file.Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(probe, file, StringComparison.Ordinal)) probe = "ffprobe";
        return dir.Length == 0 ? probe : Path.Combine(dir, probe);
    }
}
