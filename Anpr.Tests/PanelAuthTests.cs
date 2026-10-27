using Anpr.Service;

namespace Anpr.Tests;

/// <summary>
/// Panel/API erişim koruması (G3). Bu testler §P.5'teki "endpoint testi altyapısı YOK" boşluğunun
/// GÜVENLİK açısından kritik yarısını kapatır: uçları barındırmadan, kararın kendisini sınar.
/// <para>
/// Asıl regresyon hedefi <b>2026-07-28 CRITICAL</b>: yol karşılaştırması büyük/küçük harf DUYARLI
/// yapılmıştı, ASP.NET yönlendirmesi ise DUYARSIZ — <c>/API/analytics</c> korumayı tamamen
/// atlıyordu. Kusur düzeltildi ama hiçbir test onu tutmuyordu; aşağıdaki
/// <c>Uppercase_paths_are_still_protected</c> tam olarak o kusuru kırmızı-yeşil yakalar.
/// </para>
/// </summary>
public class PanelAuthTests
{
    private const string Tok = "s3cret";

    // --- Hangi yollar korumalı? ---

    [Theory]
    [InlineData("/panel")]
    [InlineData("/analiz")]
    [InlineData("/dogrulama")]
    [InlineData("/kamera")]
    [InlineData("/api/camera/settings/giris")]   // kamera ayarları: token-muaf /health'e konmaz
    [InlineData("/api")]
    [InlineData("/api/analytics")]
    [InlineData("/api/absorbed")]        // H1 teşhisi: plaka ÇİFTLERİ taşır, korunmalı
    [InlineData("/events")]
    [InlineData("/events/recent")]
    [InlineData("/evidence/20260802-101010_giris_25ABC123.jpg")]
    public void Plate_and_camera_surfaces_are_protected(string path)
        => Assert.True(PanelAuth.IsProtected(path));

    [Theory]
    [InlineData("/API/analytics")]      // ← 2026-07-28 CRITICAL: bu üçü korumayı ATLIYORDU
    [InlineData("/EVENTS/recent")]
    [InlineData("/Evidence/x.jpg")]
    [InlineData("/PaNeL")]
    public void Uppercase_paths_are_still_protected(string path)
        => Assert.True(PanelAuth.IsProtected(path));

    [Theory]
    [InlineData("/health")]             // izleme sistemleri için BİLEREK token-muaf
    [InlineData("/")]
    [InlineData("/apixyz")]             // segment sınırı: "/api" ile başlıyor ama ayrı bir yol
    [InlineData("/eventsomething")]
    [InlineData("")]
    [InlineData(null)]
    public void Unprotected_paths_stay_open(string? path)
        => Assert.False(PanelAuth.IsProtected(path));

    // --- Belirteç kararı ---

    [Fact]
    public void Protected_path_without_token_is_rejected()
        => Assert.False(PanelAuth.IsAuthorized("/events/recent", Tok, null, null));

    [Fact]
    public void Protected_path_with_wrong_token_is_rejected()
        => Assert.False(PanelAuth.IsAuthorized("/events/recent", Tok, "nope", null));

    [Fact]
    public void Query_token_and_header_token_both_work()
    {
        Assert.True(PanelAuth.IsAuthorized("/events/recent", Tok, Tok, null));
        Assert.True(PanelAuth.IsAuthorized("/events/recent", Tok, null, Tok));
    }

    [Fact]
    public void Token_comparison_is_case_sensitive()
    {
        // Yol eşleştirmesi DUYARSIZ, sır karşılaştırması DUYARLI. İkisi bilerek farklıdır;
        // karıştırılırsa ya koruma delinir ya da geçerli belirteç reddedilir.
        Assert.False(PanelAuth.IsAuthorized("/events/recent", Tok, "S3CRET", null));
        Assert.True(PanelAuth.IsAuthorized("/events/recent", Tok, Tok, null));
    }

    [Fact]
    public void Empty_supplied_token_never_passes_a_configured_gate()
    {
        Assert.False(PanelAuth.IsAuthorized("/panel", Tok, "", null));
        Assert.False(PanelAuth.IsAuthorized("/panel", Tok, "", ""));
    }

    [Fact]
    public void Health_is_reachable_without_a_token_even_when_protection_is_on()
    {
        // Bu, bir kolaylık değil TASARIM: /health token-muaf olduğu için oraya plaka metni
        // KOYULMAZ (bkz. Program.cs — lastPlate kasıtlı dışarıda, absorbedAfterEmit yalnız sayaç).
        Assert.True(PanelAuth.IsAuthorized("/health", Tok, null, null));
        Assert.True(PanelAuth.IsAuthorized("/", Tok, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_configured_token_disables_protection(string? configured)
    {
        // Geliştirme varsayılanı: koruma kapalı. Açılışta SecurityChecks GÜRÜLTÜLÜ uyarır
        // (bind loopback dışındaysa) — sessizce açık kalmaz.
        Assert.True(PanelAuth.IsAuthorized("/events/recent", configured, null, null));
        Assert.True(PanelAuth.IsAuthorized("/evidence/x.jpg", configured, null, null));
    }
}

/// <summary>
/// KABLOLAMA KORUMASI. <see cref="PanelAuthTests"/> KARARI sınar; bu sınıf kararın
/// <b>gerçekten uygulandığını</b> sınar. İkisi ayrı şeydir: <c>app.Use(...)</c> satırı silinse ya da
/// uç eşlemelerinden SONRAYA taşınsa, yukarıdaki 27 testin hepsi yeşil kalır ve plaka verisi
/// kimlik doğrulamasız açılır.
/// <para>
/// Kaynak metni okuyan bir test kırılgandır — bilerek kabul edildi: koruduğu şey KVKK ihlali.
/// Kırılırsa bakılacak yer <c>Anpr.Service/Program.cs</c>'deki middleware kaydıdır.
/// </para>
/// </summary>
public class PanelAuthWiringTests
{
    /// <summary>
    /// Program.cs — <b>yorumlar ÇIKARILMIŞ</b> hâlde. İkisi de bu yüzden gerekli:
    /// (a) yorum içindeki <c>PanelAuth.IsAuthorized</c>, middleware YORUMA ALINMIŞ olsa bile
    /// "kayıtlı" görünürdü; (b) bir şeyin NEDEN yok olduğunu anlatan yorum, o şeyin varlığı
    /// sanılırdı (ilk sürüm tam bu yüzden yanlış pozitif verdi).
    /// </summary>
    private static string ProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OtoparkAnpr.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Depo kökü bulunamadı (OtoparkAnpr.sln) — test ortamı beklenmedik.");
        var path = Path.Combine(dir!.FullName, "Anpr.Service", "Program.cs");
        Assert.True(File.Exists(path), $"Program.cs yok: {path}");
        return string.Join('\n', File.ReadAllLines(path)
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? l[..i] : l; }));
    }

    [Fact]
    public void Auth_middleware_is_registered_and_runs_before_any_endpoint()
    {
        var src = ProgramSource();

        int gate = src.IndexOf("PanelAuth.IsAuthorized", StringComparison.Ordinal);
        Assert.True(gate >= 0,
            "Program.cs artık PanelAuth.IsAuthorized çağırmıyor — erişim koruması KALKMIŞ olabilir.");

        int firstEndpoint = src.IndexOf("app.MapGet", StringComparison.Ordinal);
        Assert.True(firstEndpoint >= 0, "app.MapGet bulunamadı — Program.cs beklenmedik biçimde değişmiş.");

        Assert.True(gate < firstEndpoint,
            "Erişim koruması uç eşlemelerinden SONRA kaydedilmiş; middleware sırası korumayı etkisiz kılar.");
    }

    [Fact]
    public void Health_body_still_carries_no_plate_text()
    {
        // /health token-MUAFtır (PanelAuth.IsProtected onu dışarıda bırakır). Bu ancak gövdesinde
        // plaka metni YOKSA güvenlidir. 2026-08-02'de absorbedAfterEmit eklenirken eşlik eden
        // plaka çiftleri (RecentAbsorbed) bilerek dışarıda bırakıldı — bu test o kararı kilitler.
        var src = ProgramSource();
        int start = src.IndexOf("app.MapGet(\"/health\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "/health ucu bulunamadı.");
        int end = src.IndexOf("app.MapGet", start + 10, StringComparison.Ordinal);
        var body = end > start ? src[start..end] : src[start..];

        foreach (var leak in new[] { "RecentAbsorbed", "lastPlate =", "PlateCompact" })
            Assert.False(body.Contains(leak, StringComparison.Ordinal),
                $"/health gövdesine plaka verisi sızmış görünüyor: '{leak}' (token-muaf uç, KVKK).");
    }
}
