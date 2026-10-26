using System.Text.Json;

namespace Anpr.Service.Panel;

/// <summary>
/// Kamera-başına ROI çokgeninin kalıcılığı: `roi.&lt;kameraId&gt;.json` (uygulama dizini).
/// Panelden çizilen çokgen BURAYA yazılır — appsettings'e ve KAMERAYA asla dokunulmaz.
/// Dosya varsa config'teki başlangıç değerini (Cameras[].Roi) ezer.
/// </summary>
public sealed class RoiStore
{
    private readonly string _dir;

    public RoiStore(string? directory = null) => _dir = directory ?? AppContext.BaseDirectory;

    private string PathFor(string cameraId) => System.IO.Path.Combine(_dir, $"roi.{cameraId}.json");

    /// <summary>Kayıtlı ROI ("x,y;x,y;..."); dosya yoksa fallback döner.</summary>
    public string LoadOrDefault(string cameraId, string fallback = "")
    {
        try
        {
            string p = PathFor(cameraId);
            if (!File.Exists(p)) return fallback;
            var doc = JsonSerializer.Deserialize<RoiDoc>(File.ReadAllText(p));
            return doc?.Roi ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>ROI'yi kaydeder; boş dizge = ROI temizlendi (dosyaya boş yazılır, filtre kapanır).</summary>
    public void Save(string cameraId, string roi)
        => File.WriteAllText(PathFor(cameraId), JsonSerializer.Serialize(new RoiDoc(roi)));

    private sealed record RoiDoc(string Roi);
}
