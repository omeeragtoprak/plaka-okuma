using System.Text.RegularExpressions;

namespace Anpr.Sdk.Internal;

/// <summary>
/// Kimlik bilgisi maskeleme. Kamera RTSP adresi şifreyi URL içinde taşır; bu metin loglara, hata
/// mesajlarına ve alt süreç çıktısına sızabilir.
/// <para>2026-07-28 denetimi: maskeleyici zaten vardı ama YALNIZ bağlantı kurulum logunda kullanılıyordu.
/// ffmpeg'in kendi stderr satırları maskelenmeden dosya loguna yazılıyordu ve ffmpeg hata satırında TAM
/// URL'yi basıyor — canlı ölçüm: tek günün logunda 20 satırda açık kamera admin şifresi. Sızıntı tam da
/// şifrenin en çok paylaşıldığı anda oluşuyor ("kamera bağlanmıyor" vakasında ilk gönderilen dosya logtur).</para>
/// </summary>
internal static partial class UrlMasking
{
    /// <summary>"scheme://kullanici:SIFRE@host" kalıbındaki şifreyi maskeler (metnin herhangi bir yerinde).</summary>
    internal static string MaskCredentials(string? text)
        => string.IsNullOrEmpty(text) ? text ?? "" : CredentialRegex().Replace(text, "$1****$3");

    /// <summary>
    /// Ek savunma: bilinen şifreyi metinde nerede geçerse geçsin siler. ffmpeg her sürümde URL'yi aynı
    /// biçimde basmayabilir; regex kaçarsa bu yakalar. Boş/çok kısa şifrede devre dışıdır (yanlış eşleşme).
    /// </summary>
    internal static string MaskKnownSecret(string text, string? secret)
        => string.IsNullOrEmpty(secret) || secret.Length < 4 || string.IsNullOrEmpty(text)
            ? text
            : text.Replace(secret, "****", StringComparison.Ordinal);

    [GeneratedRegex(@"(://[^:/@\s]+:)([^@\s]+)(@)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialRegex();
}
