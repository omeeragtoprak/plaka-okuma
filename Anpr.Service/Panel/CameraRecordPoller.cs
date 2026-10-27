using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Options;

namespace Anpr.Service.Panel;

/// <summary>
/// Hikvision'ın KENDİ ANPR kayıtlarını panel için canlı akıtır: her 15 sn'de bir kameranın son-20
/// kaydını SALT-OKUMA sorgular (hiçbir ayar değiştirilmez), yeni olanları SSE'ye iter.
/// Kamera saati sapması periyodik ölçülür ve kayıt zamanları yerel saate düzeltilir.
/// İlk turda mevcut (eski) kayıtlar sessizce "görüldü" sayılır — panele tarih dökülmez.
/// </summary>
public sealed partial class CameraRecordPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ClockRefresh = TimeSpan.FromMinutes(10);

    private readonly AnprServiceOptions _options;
    private readonly PanelHub _hub;
    private readonly ILogger<CameraRecordPoller> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CamEvent>> _recent = new();

    // ⚠️ LaneNo/Direction/Country 2026-08-21'de EKLENDİ ve JSON'a yazılır. Eklemeli değişiklik:
    // eski satırlarda anahtar YOKTUR, yeni satırlarda null olabilir. Bu alanlarla yapılacak her
    // ölçüm alanın var olduğu döneme KISITLANMALIDIR (§P.4 (285) sansür yanlılığı; tarama aracı
    // `scripts/field_coverage_audit.py`). Gerekçe: yakalama oranının paydası "kameranın gördüğü
    // her araç" değil "ROI'mizden geçen araç" olmalı; ISAPI kutu koordinatı VERMEZ, bu üç alan
    // en yakın vekildir.
    public sealed record CamEvent(string CameraId, string Plate, string TimeIso,
                                  string? LaneNo = null, string? Direction = null, string? Country = null);

    /// <summary>Panelin sayfa-açılış dolumu için son kayıtlar (en yeni sonda).</summary>
    public IReadOnlyList<CamEvent> Recent(string cameraId)
        => _recent.TryGetValue(cameraId, out var q) ? q.ToArray() : Array.Empty<CamEvent>();

    // camera-records.jsonl: kameranın kendi ANPR kayıtlarının kalıcı izi (saat-düzeltilmiş).
    // Haftalık gölge-eşleşme raporu ve gece-parite ölçümleri bu dosyadan retrospektif üretilir.
    private static readonly object _persistLock = new();
    private static void PersistCamera(CamEvent e)
    {
        try
        {
            lock (_persistLock)
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "camera-records.jsonl"),
                    System.Text.Json.JsonSerializer.Serialize(e) + Environment.NewLine);
        }
        catch { /* disk hatası kaydı düşürmesin */ }
    }

    // KVKK saklama (M9): events.jsonl/evidence gibi camera-records.jsonl de EventRetentionDays'ten eski
    // plaka PII'sini açılışta budar (sınırsız birikme bitti). Atomik temp+move (çökme-güvenli). Açılışta
    // çağrılır → henüz poll/append yok; yine de _persistLock alınır. 0/negatif = sınırsız (davranış değişmez).
    // NOT: corrections.jsonl (denetim izi, daha uzun yasal saklama) + dataset-corrections/ (fine-tune verisi)
    // KASITLI dışarıda — bunlar ayrı bir saklama-politikası kararı gerektirir.
    private void PruneCameraRecords()
    {
        int days = _options.EventRetentionDays;
        if (days <= 0) return;
        string path = Path.Combine(AppContext.BaseDirectory, "camera-records.jsonl");
        try
        {
            if (!File.Exists(path)) return;
            var cutoff = DateTimeOffset.Now.AddDays(-days);
            var kept = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    var e = System.Text.Json.JsonSerializer.Deserialize<CamEvent>(line);
                    if (e != null && DateTimeOffset.TryParse(e.TimeIso, out var t) && t >= cutoff) kept.Add(line);
                }
                catch { /* bozuk satır atlanır */ }
            }
            lock (_persistLock)
            {
                var tmp = path + ".tmp";
                File.WriteAllLines(tmp, kept);
                File.Move(tmp, path, overwrite: true);
            }
            _logger.LogInformation("camera-records KVKK budama: {N} kayıt tutuldu ({Days} günden eski atıldı)", kept.Count, days);
        }
        catch { /* bozuk dosya: dokunma */ }
    }

    private void Remember(CamEvent e)
    {
        var q = _recent.GetOrAdd(e.CameraId, _ => new ConcurrentQueue<CamEvent>());
        q.Enqueue(e);
        while (q.Count > 100 && q.TryDequeue(out _)) { }
    }

    public CameraRecordPoller(IOptions<AnprServiceOptions> options, PanelHub hub, ILogger<CameraRecordPoller> logger)
    {
        _options = options.Value;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        PruneCameraRecords();   // M9: açılışta KVKK budama (events.jsonl deseniyle)
        var cams = _options.Cameras.Where(c => !string.IsNullOrWhiteSpace(c.Ip)).ToList();
        if (cams.Count == 0) { _logger.LogInformation("panel poller: IP'li kamera yok, kapalı"); return; }

        var states = cams.Select(c => new CamState(c)).ToList();
        while (!ct.IsCancellationRequested)
        {
            foreach (var st in states)
            {
                // İlk tur HER ZAMAN çalışır (saat farkı + sayfa-dolumu tamponu tohumlanır);
                // sonrasında panel istemcisi yokken kamerayı yoklamak gereksiz — abone varsa çalış.
                if (_hub.SubscriberCount == 0 && !st.FirstPoll) continue;
                try { await PollOnce(st, ct); st.Ok = true; }
                catch (Exception ex)
                {
                    if (st.Ok) _logger.LogWarning("[panel] {Cam} poll hatası: {Msg}", st.Cam.Id, ex.Message);
                    st.Ok = false;
                }
                _hub.Publish(new { source = "status", cameraId = st.Cam.Id, cameraPollOk = st.Ok });
            }
            try { await Task.Delay(PollInterval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnce(CamState st, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - st.ClockCheckedAt > ClockRefresh)
        {
            string t = await st.GetAsync("/ISAPI/System/time", ct);
            var m = Regex.Match(t, @"<localTime>([^<]+)</localTime>");
            if (m.Success && DateTimeOffset.TryParse(m.Groups[1].Value, out var camNow))
                st.ClockOffset = camNow - DateTimeOffset.Now;
            st.ClockCheckedAt = DateTimeOffset.UtcNow;
        }

        const string body = "<VehicleDetectPlateCond version=\"2.0\"><searchID>1</searchID><maxResult>20</maxResult>" +
                            "<searchResultPosition>0</searchResultPosition></VehicleDetectPlateCond>";
        string xml = await st.PostAsync("/ISAPI/Traffic/channels/1/vehicleDetect/plates", body, ct);

        foreach (var rec in ParseRecords(xml))
        {
            string key = rec.Plate + "|" + rec.CaptureTimeRaw;
            if (!st.Seen.Add(key)) continue;
            st.SeenOrder.Enqueue(key);
            while (st.SeenOrder.Count > 1000 && st.SeenOrder.TryDequeue(out var old)) st.Seen.Remove(old);

            var localTime = rec.CaptureTime - st.ClockOffset;
            var ce = new CamEvent(st.Cam.Id, rec.Plate, localTime.ToString("o"),
                                  rec.LaneNo, rec.Direction, rec.Country);
            Remember(ce); // sayfa-dolumu tamponu (ilk turdaki tarih dahil)
            PersistCamera(ce); // kalıcı iz: gölge-raporu ve gece-parite retrospektif analizleri için
            if (st.FirstPoll) continue; // tarihi CANLI akışa basma
            _hub.Publish(new { source = "camera", cameraId = ce.CameraId, plate = ce.Plate, compact = ce.Plate, timeIso = ce.TimeIso });
        }
        st.FirstPoll = false;
    }

    /// <summary>ISAPI yanıtından kayıtlar — test edilebilir saf fonksiyon.</summary>
    /// <remarks>
    /// ⚠️ <b>BLOK BAZLI ayrıştırılır, İNDEKS EŞLEMESİYLE DEĞİL</b> (2026-08-21'de düzeltildi).
    /// Eski sürüm <c>plateNumber</c> ve <c>captureTime</c> etiketlerini AYRI AYRI toplayıp
    /// indeksle eşliyordu; bir <c>&lt;Plate&gt;</c> bloğunda etiketlerden biri eksikse eşleşme
    /// KAYIYOR ve sonraki her kayda YANLIŞ zaman yazılıyordu — sessizce, hata üretmeden.
    /// Kırmızı-yeşil sınandı (<c>Poller_ParseRecords_does_not_misalign_when_a_block_lacks_a_tag</c>).
    ///
    /// 📊 <b>laneNo / direction / country neden tutuluyor:</b> yakalama oranının (capture rate)
    /// paydası "kameranın gördüğü her araç"tır, ama kameranın gördüğü her araç bizim ROI'mizden
    /// geçen bir araç DEĞİLDİR. Bu üç alan paydayı daraltmanın tek yoludur; ISAPI onları zaten
    /// gönderiyordu ve biz ATIYORDUK (§P.4 (331)). Kutu koordinatı ISAPI'de YOKTUR — bu alanlar
    /// ROI'nin yerine geçmez, yalnız en yakın vekildir.
    /// ⚠️ Alanlar külliyatın ORTASINDA eklendi → bunlarla yapılacak her ölçüm, alanın var olduğu
    /// döneme KISITLANMALIDIR (§P.4 (285) sansür yanlılığı).
    /// </remarks>
    public static IReadOnlyList<PlateRecord> ParseRecords(string xml)
    {
        var list = new List<PlateRecord>();
        foreach (Match block in Regex.Matches(xml, "<Plate>(.*?)</Plate>", RegexOptions.Singleline))
        {
            string b = block.Groups[1].Value;
            string plate = Field(b, "plateNumber")?.ToUpperInvariant() ?? "";
            if (plate.Length == 0 || plate == "UNKNOWN") continue;
            string raw = Field(b, "captureTime") ?? "";
            var m = Regex.Match(raw, @"^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})([+-]\d{2})(\d{2})?$");
            if (!m.Success) continue;
            var off = new TimeSpan(int.Parse(m.Groups[7].Value), m.Groups[8].Success ? int.Parse(m.Groups[8].Value) : 0, 0);
            var dt = new DateTimeOffset(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), off);
            list.Add(new PlateRecord(plate, raw, dt,
                Field(b, "laneNo"), Field(b, "direction"), Field(b, "country")));
        }
        return list;
    }

    /// <summary>Bir &lt;Plate&gt; bloğundan tek alan; yoksa null (eski firmware sessizce kırılmasın).</summary>
    private static string? Field(string block, string tag)
    {
        var m = Regex.Match(block, $"<{tag}>([^<]*)</{tag}>");
        if (!m.Success) return null;
        string v = m.Groups[1].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    public sealed record PlateRecord(string Plate, string CaptureTimeRaw, DateTimeOffset CaptureTime,
                                     string? LaneNo = null, string? Direction = null, string? Country = null);

    private sealed class CamState
    {
        public CameraOptions Cam { get; }
        public HashSet<string> Seen { get; } = new();
        public Queue<string> SeenOrder { get; } = new();
        public bool FirstPoll = true;
        public bool Ok = true;
        public TimeSpan ClockOffset = TimeSpan.Zero;
        public DateTimeOffset ClockCheckedAt = DateTimeOffset.MinValue;
        private readonly HttpClient _http;

        public CamState(CameraOptions cam)
        {
            Cam = cam;
            var cache = new CredentialCache
            {
                { new Uri($"http://{cam.Ip}"), "Digest", new NetworkCredential(cam.Username, cam.Password) },
            };
            _http = new HttpClient(new HttpClientHandler { Credentials = cache }) { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<string> GetAsync(string path, CancellationToken ct)
            => await _http.GetStringAsync($"http://{Cam.Ip}{path}", ct);

        public async Task<string> PostAsync(string path, string body, CancellationToken ct)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/xml");
            using var resp = await _http.PostAsync($"http://{Cam.Ip}{path}", content, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }
}
