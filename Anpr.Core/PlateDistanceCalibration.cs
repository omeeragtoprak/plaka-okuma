namespace Anpr.Core;

/// <summary>
/// Tek referans ölçümüyle plaka mesafesi kalibrasyonu (iğne deliği / pinhole modeli).
/// <para>Model: <c>Z = f · W / w</c> (Z metre, f odak px, W plakanın gerçek genişliği, w görüntüdeki genişlik px).
/// <c>f · W</c> çarpımı tek bir referans ölçümünden elde edilir: plaka şerit metreyle ölçülmüş <c>Z_ref</c>
/// mesafedeyken dedektör kutusu <c>w_ref</c> px ise <c>f · W = Z_ref · w_ref</c>. Böylece ne odak uzaklığı ne de
/// sensör bilgisi gerekir.</para>
/// <para>⚠️ <b>Kalibrasyon olmadan mutlak mesafe SÖYLENEMEZ</b> — bu sınıf geçersizken hiçbir tahmin üretmez.</para>
/// <para>⚠️ <b>Varsayımlar ve sınırlar (sahada şerit metreyle doğrulanmadan "doğru" sayılmaz):</b>
/// <list type="bullet">
/// <item>Plaka biçimi referanstakiyle aynı olmalı. Referans tek satır 520 mm plakayla alındıysa çift satır
/// (320 mm) bir plaka ~1,6× UZAKTA görünür.</item>
/// <item>Görüş açısı referanstakine yakın olmalı. Yatay açı plakayı daraltır (30°'de genişlik %13 azalır →
/// mesafe %15 fazla); referans sahanın tipik açısında alınırsa sistematik kısım kalibrasyona gömülür, şerit
/// içi konum farkı kalır.</item>
/// <item>Zoom/odak değişirse kalibrasyon GEÇERSİZDİR (yeniden ölçülmeli). Yalnız çözünürlük değişirse
/// <see cref="ReferenceFrameWidthPx"/> sayesinde ölçek düzeltilir.</item>
/// <item>Kutu titremesi: 150 px'te ±2 px → ±%1,3; 60 px'te ±%3,3. Küçük plakada tahmin gürültülüdür.</item>
/// </list></para>
/// </summary>
public sealed record PlateDistanceCalibration
{
    /// <summary>Referans ölçümünde plakanın kameraya uzaklığı (metre, şerit metreyle ölçülmüş).</summary>
    public double ReferenceDistanceMeters { get; init; }

    /// <summary>Aynı anda DEDEKTÖR kutusunun genişliği (px, dolgusuz) — olaydaki <c>PlateWidthPx</c> değeri.</summary>
    public double ReferencePlateWidthPx { get; init; }

    /// <summary>Referans ölçümündeki işlenen kare genişliği (px). Kare genişliği değişirse ölçek buna göre düzeltilir.</summary>
    public int ReferenceFrameWidthPx { get; init; }

    /// <summary>Üç değer de sonlu ve pozitif, plaka kareden dar mı.</summary>
    public bool IsValid =>
        double.IsFinite(ReferenceDistanceMeters) && double.IsFinite(ReferencePlateWidthPx) &&
        ReferenceDistanceMeters > 0 && ReferencePlateWidthPx > 0 && ReferenceFrameWidthPx > 0 &&
        ReferencePlateWidthPx < ReferenceFrameWidthPx;

    // f·W (px·m) — bu kare genişliğinde. Aynı lenste çözünürlük ölçeklenirse piksel genişlikleri de doğrusal ölçeklenir.
    private double Constant(int frameWidthPx) =>
        ReferenceDistanceMeters * ReferencePlateWidthPx * ((double)frameWidthPx / ReferenceFrameWidthPx);

    /// <summary>Tahmini mesafe (metre). Kalibrasyon geçersizse ya da girdiler pozitif değilse <c>null</c>.</summary>
    public double? EstimateMeters(int plateWidthPx, int frameWidthPx) =>
        IsValid && plateWidthPx > 0 && frameWidthPx > 0 ? Constant(frameWidthPx) / plateWidthPx : null;

    /// <summary>Plaka bu mesafedeyken beklenen dedektör kutusu genişliği (px). Mesafe sınırını piksel sınırına çevirir.</summary>
    public double WidthPxAt(double meters, int frameWidthPx) => Constant(frameWidthPx) / meters;
}
