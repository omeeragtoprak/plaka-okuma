namespace Anpr.Core;

/// <summary>
/// Eksenleri görüntü piksel uzayında olan sınırlayıcı kutu (sol-üst köşe + genişlik/yükseklik).
/// </summary>
public readonly record struct PlateBox(int X, int Y, int Width, int Height);


/// <summary>
/// Tek bir plaka okuması. Anpr.Core'un dışarıya verdiği temel sonuç tipidir.
/// </summary>
public sealed record PlateReading
{
    /// <summary>Normalize edilmiş plaka metni (ör. "25 ABC 123"). TR doğrulaması geçmediyse ham okuma döner.</summary>
    public required string Text { get; init; }

    /// <summary>OCR modelinin ürettiği ham metin (normalize/düzeltme öncesi).</summary>
    public required string RawText { get; init; }

    /// <summary>Birleşik güven skoru (dedektör skoru * OCR skoru), 0..1.</summary>
    public required float Confidence { get; init; }

    /// <summary>Dedektörün plaka kutusu için güven skoru, 0..1.</summary>
    public required float DetectorConfidence { get; init; }

    /// <summary>OCR'ın ortalama karakter güveni, 0..1.</summary>
    public required float OcrConfidence { get; init; }

    /// <summary>Plakanın görüntüdeki konumu.</summary>
    public required PlateBox Box { get; init; }

    /// <summary>Türkiye plaka formatına uyup uymadığı.</summary>
    public required bool IsValidTr { get; init; }

    /// <summary>
    /// OCR'ın slot-bazlı TAM olasılık dağılımı (uzunluk = slot × vocab, satır-öncelikli: slot sonra vocab).
    /// Karakter-bazlı oylama + gramer-kısıtlı çözümleme için. Bilinmiyorsa null.
    /// </summary>
    public float[]? SlotProbs { get; init; }

    /// <summary>OCR alfabesi (SlotProbs'taki vocab sırası). SlotProbs doluysa dolu olur.</summary>
    public string? OcrAlphabet { get; init; }

    /// <summary>
    /// Bu okuma <c>EarlyEmitMinOcr</c> tarafından en az bir kez TUTULDUYSA, <b>ilk tutulma anındaki</b>
    /// OCR güveni; hiç tutulmadıysa <c>null</c>. Kazanç = <see cref="OcrConfidence"/> − bu değer.
    /// <para>
    /// <b>Neden var (#70, 2026-08-07):</b> tutma bir <b>gecikme–doğruluk takasıdır</b> ve bugüne dek
    /// yalnız SÜREÇ düzeyinde sayaçlarla (<c>EarlyEmitHolds</c>/<c>...Resolved</c>/<c>AvgGain</c>)
    /// izleniyordu. O sayaçlar süreç yeniden başlayınca SIFIRLANIR ve daha önemlisi <b>hangi olayın
    /// tutulduğunu söylemezler</b> — dolayısıyla "tutmak YANLIŞ bir okumayı DOĞRUYA çevirdi mi?"
    /// sorusu, yani takasın asıl kazanç tarafı, yer-gerçeğiyle eşleştirilemiyordu. Bu alan olayın
    /// KENDİSİNE iliştiği için o eşleştirme mümkün olur.
    /// </para>
    /// <para>
    /// ⚠️ Güven KAZANCI bir vekildir, SONUÇ değildir: güvenin artması okumanın doğrulandığı anlamına
    /// gelmez. Karar yalnız yer-gerçeğiyle eşleştirilmiş sonuç üzerinden verilmelidir.
    /// </para>
    /// </summary>
    public float? HeldAtOcr { get; init; }

    /// <summary>
    /// Tutma anında izin okuduğu METİN — yani <b>tutma olmasaydı YAYINLANACAK olan dizgi</b>
    /// (2026-08-21). <c>null</c> = iz hiç tutulmadı.
    /// <para>
    /// 🔑 <b>NEDEN VAR:</b> <see cref="HeldAtOcr"/> tek başına #70'i YANITLAYAMAZ. O alan
    /// *"güven ne kadar arttı"* der; #70'in sorusu ise *"tutmak YANLIŞ bir okumayı DOĞRUYA
    /// çevirdi mi?"*. Güven artışı bir VEKİLDİR — okuma değişmeden de artabilir, ve okuma
    /// değişip yine de yanlış kalabilir. Karşı-olgusal metin kaydedilmeden takasın kazanç
    /// tarafı ölçülemez; metin tutma anında <c>BuildResult</c> içinde ZATEN mevcuttu ve
    /// atılıyordu (§P.4 (335) — "yarım enstrüman" sınıfı).
    /// </para>
    /// <para>
    /// ⚠️ <b>Geriye dönük doldurulamaz.</b> 2026-08-21 öncesi tutmalarda bu alan yoktur ve
    /// o olaylar için #70 sorusu KALICI olarak yanıtsızdır. Alanla yapılacak her ölçüm,
    /// alanın açılış anından SONRAYA kısıtlanmalıdır (§P.4 (285) sansür yanlılığı).
    /// </para>
    /// <para>
    /// ⚠️ Alanın DOLU ve yayınlananla AYNI olması, *"tutma sonucu değiştirmedi"* demektir —
    /// bu, *"bilmiyoruz"*tan farklı ve karar için gerekli bir bilgidir. İkisini karıştırmayın.
    /// </para>
    /// </summary>
    public string? HeldAtText { get; init; }

    /// <summary>
    /// Tespit merkezi ROI içinde mi? ROI tanımsızsa daima true. false = "gözlem" okuması:
    /// kapsam dışı (cadde/uzak) trafik — bariyer kararına girmez, karşılaştırma/izleme için yayınlanır.
    /// </summary>
    /// <summary>
    /// SNAP uygulandıysa: snap hedefinin, snap ÖNCESİ aday listesindeki <b>1-tabanlı sırası</b>;
    /// hedef modelin ilk-K adayında hiç yoksa <c>null</c>. Snap yoksa <c>null</c>.
    /// <para>🔑 <b>NEDEN VAR (2026-08-22, §P.4 (365)/(366)).</b> Snap, aday listesini yeniden yazıp
    /// hedefi başa koyar ve hedefe EŞİT olan özgün adayı <b>eler</b>. Bu, belgelenmiş bir sözleşme
    /// (<c>Candidates[0] == PlateCompact</c>) ama bir ÖLÇÜM BOŞLUĞU doğuruyordu: *"OCR'ın kendisi bu
    /// düzeltmeyi destekliyor muydu?"* sorusu olaydan artık yanıtlanamıyordu.
    /// Ölçüldü: 536 canlı snap olayının <b>536'sında</b> hedef 1., kaynak 2. sırada — yani liste snap
    /// SONRASI durumu yansıtıyor ve snap kararı için <b>bağımsız kanıt değil</b>.</para>
    /// <para>⚠️ <c>null</c> "model buna sıfır olasılık verdi" DEMEK DEĞİLDİR — yalnız *"ilk-K içinde
    /// değildi"* demektir (K = <c>CandidateCount</c>). Yokluk kanıtı değil, kapsam sınırıdır.</para>
    /// </summary>
    public int? SnapTargetRank { get; init; }

    /// <summary>
    /// SNAP uygulandıysa: snap hedefine <b>MODELİN KENDİ</b> verdiği güven (snap öncesi aday
    /// listesinden). Hedef ilk-K'da yoksa <c>null</c>.
    /// <para>⚠️ Bu, <see cref="OcrConfidence"/> ile <b>karıştırılmamalıdır</b>: snap sonrası
    /// <c>OcrConfidence</c> hâlâ <b>snap ÖNCESİ dizginin</b> skorudur (ödünç). Bu alan ise hedefin
    /// kendi skorudur. İkisinin FARKI, snap'in modele ne kadar aykırı olduğunu ölçer.</para>
    /// </summary>
    public float? SnapTargetOcr { get; init; }

    /// <summary>
    /// Bu izi ONAYLAYAN kare sayısı (<c>Track.Count</c>) — yani plaka bu geçişte kaç kez görüldü.
    /// <para><b>Neden var (§P.4 (425)):</b> *"geçiş başına kaç kare işleniyor?"* sorusu
    /// <c>docs/saha-fiziksel-gercekler.md</c> §4'te <b>ölçülmemiş boşluk</b> olarak duruyordu ve
    /// #69'un kadraj kararını doğrudan etkiliyor: dar kadraj aracın görüş süresini kısaltır, kare
    /// bütçesi 20 km/s'te 3,7'ye iner (<c>2026-08-09-69-gece-kolu-yerellestirme.md</c>) — ama o
    /// tablo <b>varsayılan hızlarla</b> hesaplandı, sahadan ölçülmedi. Sayı zaten
    /// <c>PlateConfirmer.Track.Count</c> içinde tutuluyordu; yalnız olaya yazılmıyordu.</para>
    /// <para>⚠️ Bu, işlenen TÜM karelerin sayısı DEĞİL — plakanın <b>bulunup okunduğu</b> kare
    /// sayısıdır. Dedektörün kaçırdığı kareler burada görünmez, yani bir <b>ALT SINIR</b>dır.</para>
    /// </summary>
    public int? TrackFrames { get; init; }

    /// <summary>
    /// Bu ize SOĞURULAN (aynı araç sayılıp birleştirilen) diğer okuma sayısı
    /// (<c>Track.AbsorbCount</c>). <see cref="TrackFrames"/> ile birlikte, uzlaşının ne kadar
    /// veriyle kurulduğunu gösterir.
    /// </summary>
    public int? TrackAbsorbed { get; init; }

    public bool InZone { get; init; } = true;

    /// <summary>
    /// Uzlaşı çözümlemesinin en iyi K GEÇERLİ adayı (skora göre; ilki Text ile aynı). Whitelist
    /// toleransı + emit-sonrası varyant bastırma için. Yalnız kısıtlı-çözümlemeli uzlaşı emitlerinde dolu.
    /// </summary>
    public IReadOnlyList<PlateCandidate>? Candidates { get; init; }

    public override string ToString() =>
        $"{Text} (conf={Confidence:F2}, det={DetectorConfidence:F2}, ocr={OcrConfidence:F2}, tr={IsValidTr})";
}

/// <summary>Alternatif plaka okuması adayı (boşluksuz metin + güven).</summary>
public sealed record PlateCandidate(string Compact, float Confidence)
{
    public override string ToString() => $"{Compact}:{Confidence:F2}";
}
