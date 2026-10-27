namespace Anpr.Service;

/// <summary>
/// Panel/API erişim korumasının KARARI (G3). Saf ve test-edilebilir tutulur — çünkü bu projedeki
/// en ciddi güvenlik kusuru tam olarak burada, bir <b>karşılaştırma ayrıntısında</b> yaşandı.
/// <para>
/// <b>2026-07-28 CRITICAL:</b> koruma <c>string.StartsWith</c> ile yapılıyordu; o metot ORDINAL ve
/// BÜYÜK/KÜÇÜK HARF DUYARLIDIR, oysa ASP.NET yönlendirmesi DUYARSIZDIR. Sonuç: <c>/API/analytics</c>,
/// <c>/EVENTS/recent</c>, <c>/EVIDENCE/x.jpg</c> kontrolü tamamen atlıyordu — tüm plaka geçmişi,
/// kanıt görselleri, canlı kamera akışı ve <b>bariyer tetik bölgesini DEĞİŞTİREN yazma ucu</b>
/// kimlik doğrulamasız erişilebiliyordu (KVKK + işletme riski).
/// </para>
/// <para>
/// Kusur düzeltildi ama <b>hiçbir testle korunmuyordu</b> (§P.5: "no endpoint-hosting test infra").
/// Karar buraya taşındı ki aynı sınıf bir daha sessizce geri gelmesin. Middleware artık yalnız
/// "kararı uygula" işini yapar; karar burada ve testlidir.
/// </para>
/// </summary>
public static class PanelAuth
{
    /// <summary>
    /// Korumalı yol önekleri. <b>Yeni bir plaka/kanıt/kamera ucu eklerken buraya da ekleyin</b> —
    /// aksi hâlde uç kimlik doğrulamasız açılır ve bunu kimse fark etmez.
    /// <c>/health</c> BİLEREK yoktur: izleme sistemleri için token-muaftır ve bu yüzden
    /// plaka metni ASLA oraya konmaz (bkz. <c>Program.cs</c> health gövdesi).
    /// </summary>
    private static readonly string[] Protected =
        ["/panel", "/analiz", "/dogrulama", "/kamera", "/api", "/events", "/evidence"];

    /// <summary>
    /// Bu yol token ister mi? Karşılaştırma <b>büyük/küçük harf DUYARSIZ</b> ve <b>segment sınırına
    /// saygılı</b>dır: <c>/API/analytics</c> korumalıdır, <c>/apixyz</c> değildir.
    /// </summary>
    public static bool IsProtected(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var p in Protected)
        {
            if (!path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            // Segment sınırı: tam eşleşme ya da hemen ardından '/' gelmeli.
            if (path.Length == p.Length || path[p.Length] == '/') return true;
        }
        return false;
    }

    /// <summary>
    /// İstek geçebilir mi? <paramref name="configured"/> boş/yoksa koruma KAPALIDIR (geliştirme modu)
    /// ve her istek geçer — bu bilinçli bir varsayılandır, açılışta <see cref="SecurityChecks"/>
    /// gürültülü uyarır.
    /// <para>
    /// Belirteç karşılaştırması <b>ORDINAL ve harf DUYARLI</b>dır (sır karşılaştırması; yol
    /// eşleştirmesinin tersi). İkisinin farklı olması kasıtlıdır ve karıştırılırsa ya koruma
    /// delinir ya da geçerli belirteç reddedilir.
    /// </para>
    /// </summary>
    public static bool IsAuthorized(string? path, string? configured, string? queryToken, string? headerToken)
    {
        if (string.IsNullOrWhiteSpace(configured)) return true;   // koruma kapalı
        if (!IsProtected(path)) return true;                       // korumasız uç (/health, /)
        var supplied = !string.IsNullOrEmpty(queryToken) ? queryToken : headerToken;
        return string.Equals(supplied, configured, StringComparison.Ordinal);
    }
}
