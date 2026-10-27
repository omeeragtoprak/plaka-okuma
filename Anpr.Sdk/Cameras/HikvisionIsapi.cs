using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace Anpr.Sdk.Cameras;

/// <summary>
/// Hikvision ISAPI <b>SALT-OKUNUR</b> ayar okuyucu. Yalnız HTTP <c>GET</c> yapar; bu sınıfta yazma (PUT/POST/DELETE) yolu
/// YOKTUR ve bu bir testle kilitlidir (kamera ayarları kullanıcının açık onayı olmadan değiştirilmez).
/// <para><b>Kilitlenme koruması:</b> kamera 401/403 dönerse okuma HEMEN durur ve aynı nesne bir daha istek atmaz — Hikvision
/// art arda hatalı girişte istemci IP'sini bir süre kilitleyebilir; yanlış şifreyle döngüde deneme yapılmaz.</para>
/// <para>Uçlar (ISAPI General Application Guide v2.0): <c>/ISAPI/Image/channels/1</c>, <c>…/capabilities</c>,
/// <c>/ISAPI/Streaming/channels/&lt;id&gt;</c>, <c>…/capabilities</c>. Tehlikeli uçlar (<c>/reset</c>, <c>/restore</c>,
/// <c>configurationData</c>) bu sınıfta hiç anılmaz.</para>
/// </summary>
public sealed class HikvisionIsapiReader : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _base;
    private bool _authRejected;
    private bool _unreachable;

    /// <summary>ISAPI ayar yanıtları birkaç KB'tır; bu sınır düşük bellekli hedefte bozuk/kötü niyetli yanıta karşı korur.</summary>
    internal const long MaxResponseBytes = 1024 * 1024;

    /// <param name="host">Kameranın adresi (ör. "192.168.1.64" ya da "kamera:8080").</param>
    /// <exception cref="ArgumentException">Adres geçerli bir HTTP ana makinesi değilse.</exception>
    public HikvisionIsapiReader(string host, string username, string password, TimeSpan? timeout = null)
        : this(BaseUri(host), CreateHandler(BaseUri(host), username, password), timeout) { }

    internal HikvisionIsapiReader(Uri baseUri, HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _base = baseUri;
        _http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = MaxResponseBytes };
    }

    private static Uri BaseUri(string host) =>
        !string.IsNullOrWhiteSpace(host) && Uri.TryCreate($"http://{host.Trim()}", UriKind.Absolute, out var u) && u.Host.Length > 0
            ? u
            : throw new ArgumentException("Kamera adresi geçerli bir ana makine değil (ör. 192.168.1.64 ya da [fe80::1]).", nameof(host));

    private static HttpMessageHandler CreateHandler(Uri baseUri, string username, string password) =>
        new HttpClientHandler
        {
            Credentials = new CredentialCache { { baseUri, "Digest", new NetworkCredential(username, password) } },
            PreAuthenticate = false,
        };

    /// <summary>Görüntü + akış ayarlarını ve yetenek aralıklarını okur. Hata fırlatmaz; sorunlar <see cref="CameraSettingsSnapshot.Problems"/>'a yazılır.</summary>
    public async Task<CameraSettingsSnapshot> ReadAsync(string cameraId, int streamChannel = 101, CancellationToken ct = default)
    {
        var problems = new List<string>();
        string? image = await GetAsync("/ISAPI/Image/channels/1", problems, ct);
        string? imageCaps = await GetAsync("/ISAPI/Image/channels/1/capabilities", problems, ct);
        string? stream = await GetAsync($"/ISAPI/Streaming/channels/{streamChannel}", problems, ct);
        string? streamCaps = await GetAsync($"/ISAPI/Streaming/channels/{streamChannel}/capabilities", problems, ct);

        return new CameraSettingsSnapshot
        {
            CameraId = cameraId,
            Source = "hikvision-isapi",
            CapturedUtc = DateTimeOffset.UtcNow,
            Image = image != null ? IsapiParser.ParseImage(image, imageCaps, problems) : null,
            Stream = stream != null ? IsapiParser.ParseStream(stream, streamCaps, problems) : null,
            // Not: ayrıştırıcı ISAPI dışı bir yanıtta (ör. HTML giriş sayfası) null döner → "görüntü ayarı yok" dürüstçe görünür.
            Problems = problems,
            AuthRejected = _authRejected,
        };
    }

    // TEK istek yolu: yalnız GET. Yeni bir uç eklenirse de buradan geçmelidir.
    private async Task<string?> GetAsync(string path, List<string> problems, CancellationToken ct)
    {
        if (_authRejected) return null;
        if (_unreachable) return null;   // ilk bağlantı hatasından sonra kalan uçlar beklenmez (4 × zaman aşımı yerine 1)
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, path));
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _authRejected = true;
                problems.Add($"{path}: kimlik doğrulama reddedildi ({(int)resp.StatusCode}) — okuma durduruldu; yanlış şifreyle "
                             + "tekrar denemek kamerayı kilitleyebilir. Kullanıcı adı/şifreyi kontrol edin.");
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                problems.Add($"{path}: kamera {(int)resp.StatusCode} döndü (uç desteklenmiyor olabilir).");
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _unreachable = true;
            problems.Add($"{path}: kameraya ulaşılamadı ya da yanıt okunamadı ({ex.GetType().Name}); kalan ISAPI uçları atlandı.");
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>ISAPI XML ayrıştırıcıları — saf, test edilebilir. Ad alanından bağımsız (yerel adla eşleşir).</summary>
internal static class IsapiParser
{
    /// <summary>Görüntü ayarları; yanıt bir ISAPI <c>ImageChannel</c> belgesi değilse <c>null</c> (sorun yazılır).</summary>
    public static CameraImageSettings? ParseImage(string xml, string? capsXml, List<string>? problems = null)
    {
        var root = Load(xml, "Image", "ImageChannel", problems);
        var caps = capsXml != null ? Load(capsXml, "Image/capabilities", "ImageChannel", problems) : null;
        if (root == null) return null;
        string? shutter = Text(root, "Shutter", "ShutterLevel");
        string? wdrMode = Text(root, "WDR", "mode");
        string? nrMode = Text(root, "NoiseReduce", "mode");
        return new CameraImageSettings
        {
            ExposureMode = Text(root, "Exposure", "ExposureType"),
            Shutter = shutter,
            ShutterSeconds = ParseShutter(shutter),
            GainLevel = Int(Text(root, "Gain", "GainLevel")),
            WdrMode = wdrMode,
            Wdr = wdrMode?.ToLowerInvariant() switch { "open" => true, "close" => false, _ => null },   // "auto" = koşullu
            Blc = Bool(Text(root, "BLC", "enabled")),
            Hlc = Bool(Text(root, "HLC", "enabled")),
            NoiseReductionMode = nrMode,
            // Kareler arası seviye yalnız "advanced" kipte etkindir; kapalı/genel kipte o alan eski bir değer taşıyabilir.
            TemporalNoiseReductionLevel = string.Equals(nrMode, "advanced", StringComparison.OrdinalIgnoreCase)
                ? Int(Text(root, "NoiseReduce", "AdvancedMode", "InterFrameNoiseReduceLevel")) : null,
            DayNightMode = Text(root, "IrcutFilter", "IrcutFilterType"),
            FocusMode = Text(root, "FocusConfiguration", "focusStyle"),
            Illumination = Text(root, "SupplementLight", "supplementLightMode"),
            ShutterOptionsSeconds = caps != null
                ? Options(Find(caps, "Shutter", "ShutterLevel"))?.Select(ParseShutter).OfType<double>().ToArray()
                : null,
        };
    }

    /// <summary>Akış ayarları; yanıt bir ISAPI <c>StreamingChannel</c> belgesi değilse <c>null</c> (sorun yazılır).</summary>
    public static CameraStreamSettings? ParseStream(string xml, string? capsXml, List<string>? problems = null)
    {
        var root = Load(xml, "Streaming", "StreamingChannel", problems);
        var caps = capsXml != null ? Load(capsXml, "Streaming/capabilities", "StreamingChannel", problems) : null;
        if (root == null) return null;
        string? mode = Text(root, "Video", "videoQualityControlType");
        int? rate = string.Equals(mode, "CBR", StringComparison.OrdinalIgnoreCase)
            ? Int(Text(root, "Video", "constantBitRate"))
            : Int(Text(root, "Video", "vbrUpperCap")) ?? Int(Text(root, "Video", "constantBitRate"));
        return new CameraStreamSettings
        {
            Codec = Text(root, "Video", "videoCodecType"),
            Width = Int(Text(root, "Video", "videoResolutionWidth")),
            Height = Int(Text(root, "Video", "videoResolutionHeight")),
            Fps = Int(Text(root, "Video", "maxFrameRate")) is { } f ? f / 100.0 : null,   // ISAPI: fps × 100
            BitrateKbps = rate,
            BitrateMode = mode,
            GopFrames = Int(Text(root, "Video", "GovLength")),
            SmartCodec = Bool(Text(root, "Video", "SmartCodec", "enabled")),
            FpsOptions = caps != null
                ? Options(Find(caps, "Video", "maxFrameRate"))?.Select(Int).OfType<int>().Select(v => v / 100.0).ToArray()
                : null,
            MaxBitrateKbps = caps != null ? Int(Find(caps, "Video", "vbrUpperCap")?.Attribute("max")?.Value
                                                ?? Find(caps, "Video", "constantBitRate")?.Attribute("max")?.Value) : null,
            CodecOptions = caps != null ? Options(Find(caps, "Video", "videoCodecType")) : null,
        };
    }

    /// <summary>"1/150" → 0,00667 s; "1/1" → 1 s; tanınmayan → null.</summary>
    public static double? ParseShutter(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double den) && num > 0 && den > 0)
            return num / den;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : null;
    }

    // Kök öğe denetimi: 200 dönen ama ISAPI olmayan bir yanıt (ör. HTML giriş sayfası, başka cihaz) "her alanı boş ayar" diye
    // sessizce kabul edilmesin — kaynak yanlışlıkla "hikvision-isapi" görünür ve "görüntü ayarı yok" uyarısı bastırılırdı.
    private static XElement? Load(string xml, string what, string expectedRoot, List<string>? problems)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root?.Name.LocalName == expectedRoot) return root;
            problems?.Add($"{what}: yanıt beklenen ISAPI belgesi değil (kök '{root?.Name.LocalName}'); bu uç kullanılmadı.");
            return null;
        }
        catch (System.Xml.XmlException) { problems?.Add($"{what}: yanıt XML olarak okunamadı."); return null; }
    }

    private static XElement? Find(XElement root, params string[] path)
    {
        XElement? cur = root;
        foreach (var name in path)
        {
            cur = cur?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (cur == null) return null;
        }
        return cur;
    }

    private static string? Text(XElement root, params string[] path) =>
        Find(root, path)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static IReadOnlyList<string>? Options(XElement? e) =>
        e?.Attribute("opt")?.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private static bool? Bool(string? s) => bool.TryParse(s, out bool v) ? v : null;
}
