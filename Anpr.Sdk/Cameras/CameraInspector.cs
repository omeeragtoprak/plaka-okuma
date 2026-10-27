using System.Text.RegularExpressions;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Workers;

namespace Anpr.Sdk.Cameras;

/// <summary>Bir kameranın salt-okunur incelemesi: okunan ayarlar, bu kameranın ölçümü ve öneriler.</summary>
public sealed record CameraInspection
{
    public required string CameraId { get; init; }
    public required DateTimeOffset CapturedUtc { get; init; }
    /// <summary>Birleşik akış bilgisi: üretici API'si okunduysa onun değerleri, eksikler RTSP yoklamasından.</summary>
    public CameraStreamSettings? Stream { get; init; }
    /// <summary>Yalnız üretici API'siyle okunabilen görüntü ayarları (yoksa <c>null</c>).</summary>
    public CameraImageSettings? Image { get; init; }
    /// <summary>RTSP akışının <c>ffprobe</c> ile doğrudan yoklaması (satıcıdan bağımsız; her kamerada).</summary>
    public CameraStreamSettings? RtspProbe { get; init; }
    /// <summary>Üretici API'si okuması (denenmediyse <c>null</c>).</summary>
    public CameraSettingsSnapshot? Vendor { get; init; }
    public PlateWidthStats? PlateWidths { get; init; }
    public IReadOnlyList<CameraAdvice> Advice { get; init; } = Array.Empty<CameraAdvice>();
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Kamera ayarlarını <b>SALT-OKUNUR</b> inceler ve ANPR önerisi üretir. Kameraya hiçbir şey YAZMAZ.
/// <list type="number">
/// <item>RTSP akışı <c>ffprobe</c> ile yoklanır — her IP kamerada çalışır (kodek, çözünürlük, kare hızı).</item>
/// <item>Kamerada <c>Ip</c> + <c>Username</c> varsa ve <c>readVendorApi</c> açıksa Hikvision ISAPI'den yalnız GET ile
/// görüntü ayarları (deklanşör, kazanç, WDR…) ve yetenek aralıkları okunur. Kimlik reddedilirse okuma hemen durur.</item>
/// <item><see cref="CameraAdvisor"/> öneri listesini üretir.</item>
/// </list>
/// </summary>
public static partial class CameraInspector
{
    public static async Task<CameraInspection> InspectAsync(CameraOptions camera, AnprServiceOptions options,
        ICameraStatus? status = null, bool readVendorApi = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();

        string url = AnprCameraSupervisor.BuildRtspUrl(camera);
        var (rtsp, probeProblem) = await RtspStreamProbe.ProbeAsync(
            RtspStreamProbe.DeriveFfprobePath(options.Ffmpeg.Path), url, options.Ffmpeg.RtspTransport, ct: ct);
        if (probeProblem != null) problems.Add("RTSP yoklaması: " + probeProblem);

        CameraSettingsSnapshot? vendor = null;
        if (readVendorApi && !string.IsNullOrWhiteSpace(camera.Ip) && !string.IsNullOrWhiteSpace(camera.Username))
        {
            HikvisionIsapiReader? reader = null;
            try { reader = new HikvisionIsapiReader(camera.Ip, camera.Username, camera.Password); }
            catch (ArgumentException ex) { problems.Add("ISAPI: " + ex.Message.Split(" (Parameter")[0]); }
            if (reader != null)
                using (reader)
                {
                    vendor = await reader.ReadAsync(camera.Id, StreamChannelOf(url), ct);
                    problems.AddRange(vendor.Problems.Select(p => "ISAPI: " + p));
                }
        }

        var stream = Merge(vendor?.Stream, rtsp);
        var widths = status?.RecentPlateWidths;
        return new CameraInspection
        {
            CameraId = camera.Id,
            CapturedUtc = DateTimeOffset.UtcNow,
            Stream = stream,
            Image = vendor?.Image,
            RtspProbe = rtsp,
            Vendor = vendor,
            PlateWidths = widths,
            Advice = CameraAdvisor.Evaluate(stream, vendor?.Image, widths, options.ResolveSourceFps()),
            Problems = problems,
        };
    }

    /// <summary>Üretici değeri öncelikli; eksik alanlar RTSP yoklamasından tamamlanır.</summary>
    internal static CameraStreamSettings? Merge(CameraStreamSettings? vendor, CameraStreamSettings? rtsp)
    {
        if (vendor == null) return rtsp;
        if (rtsp == null) return vendor;
        return vendor with
        {
            Codec = vendor.Codec ?? rtsp.Codec,
            Width = vendor.Width ?? rtsp.Width,
            Height = vendor.Height ?? rtsp.Height,
            Fps = vendor.Fps ?? rtsp.Fps,
            BitrateKbps = vendor.BitrateKbps ?? rtsp.BitrateKbps,
        };
    }

    /// <summary>Hikvision RTSP yolundan akış kanalı ("…/Streaming/Channels/102" → 102); bulunamazsa 101.</summary>
    internal static int StreamChannelOf(string rtspUrl)
    {
        var m = ChannelRegex().Match(rtspUrl);
        return m.Success && int.TryParse(m.Groups[1].Value, out int ch) ? ch : 101;
    }

    [GeneratedRegex(@"/Streaming/Channels/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();
}
