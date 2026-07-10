# Uygulama Planı: Kırpıntı-Seviyesi Görüntü İşleme ("İkinci-Şans Şeridi")

Spec: `docs/superpowers/specs/2026-07-10-kirpinti-goruntu-isleme-design.md` (onaylı)
İlke: her faz ayrı commit + ölçüm + gap-analizi güncellemesi; kazanmayan aşama elenir ve geri alınır.
Bayrakların TÜMÜ varsayılan KAPALI doğar; yalnız kapıyı geçen aşama appsettings'te açılır.

## Faz 0 — Ölçüm altyapısı (dilimli setler + A/B koşum düzeni)

**0.1 Dilimleme betiği** — `scripts/make_slices.py` (.venv-train python'u; onnxruntime + PIL):
- Girdi: `dataset/finetune/val.csv` (500 etiketli kırpıntı) + `night_all.csv` (125).
- Her kırpıntı için üç ölçü hesapla ve `dataset/slices/slices.csv`'ye yaz:
  `blur` (gri Laplacian varyansı), `exposure` (histogram %2/%98 uçları), `skew`
  (WPOD `models/wpod_lpd.onnx` quad'ından üst-kenar açısı + kenar-oran perspektif şiddeti),
  `width_px`.
- Dilim tanımları (CSV kolonu): `açılı` = skew üst %25'lik dilim; `karanlık` = exposure alt bandı;
  `küçük` = width<100 px; `temiz` = hiçbiri.
- Doğrulama: `wc -l dataset/slices/slices.csv` ≈ 626; her dilimden 5 örnek gözle kontrol
  (Read ile aç — açılı diye etiketlenen gerçekten açılı mı).

**0.2 Python A/B koşucusu** — `scripts/enhance_ab.py`:
- Verilen dönüşüm zincirini (`--rectify/--night/--sr`) dilim filtreli sete uygular, iki modeli değil
  TEK modeli (cct_xs_v2_erz) ham-vs-zenginleştirilmiş kırpıntıyla karşılaştırır; dilim başına
  exact ve ortalama güven raporlar.
- Doğrulama: `--rectify` olmadan koşum = croptest.py ile aynı sayılar (taban tutarlılığı).

**0.3 C# bench bayrağı** — `Anpr.Bench` + `camera_compare.py`'ye `--enhance` (aşama bit'leri)
geçişi; emitsim yolunda CropEnhancer'ı etkinleştirir. Doğrulama: bayraksız diag4 = mevcut referans
(22/26, gürültü 11).

## Faz 1 — Kalite kapısı (INLINE, ~0.1 ms)

**1.1** `Anpr.Core/Imaging/CropQualityScorer.cs`: `Score(SKBitmap) -> (float Blur, float DarkPct,
float BrightPct)` — gri dönüşüm + 3×3 Laplacian varyansı + histogram uç yüzdeleri. Saf statik sınıf.

**1.2 Birim testler** (KVKK: fixture'lar SENTETİK — plaka görüntüsü repoya girmez):
programatik üretilmiş keskin damalı desen vs Gauss-bulanık kopyası → Blur sıralaması; siyah/beyaz
ağırlıklı görüntüler → uç yüzdeleri. `dotnet test` 61→64+.

**1.3 Eşik taraması (offline)**: `enhance_ab.py --quality-sweep` — val+gece setinde blur eşiği
süpürülür; "atılan kırpıntıların exact'i" vs "kalanların exact'i" tablosu → `QualityBlurMin` seçimi.
Kural: doğru okunan kırpıntıların <%2'sini atan en agresif eşik.

**1.4 Boru hattına bağlama**: `AnprOptions.CropQualityGate` (vars. false) + `RecognizeOne` başında
skor; eşik altı kırpıntı OCR'a girmez, sayaç artar (`QualityDropped` health'e). Loglama: dakikada
en fazla 1 özet satır.

**1.5 Kapı ölçümü**: diag4 + live1h, kapı açık/kapalı. Kabul: yanlış-emit ↓, recall SABİT.
Geçerse appsettings'te aç + gap-analizi 5g + commit/push. Geçmezse elenenler listesine.

## Faz 2 — WPOD rektifikasyonu (iz-içi, iz başına 1 kez)

**2.1** `Anpr.Core/Imaging/PlateRectifier2.cs`:
- WPOD oturumu (`WpodModelPath`, IR7 doğrulandı): girdi NHWC BGR float/255, dinamik boyut
  (16'nın katına yuvarla, kırpıntıyı 0.5-1.5× marjla besle — ort-alpr canlı testiyle doğrulanmış reçete).
- Decode portu (ort-alpr `infer_utils.py reconstruct`): stride-16 hücre olasılıkları + 2×3 affine
  → kanonik kare → quad; eşik `WpodProbMin=0.5`.
- `SolveHomography(quad, hedefDikdörtgen)`: 8×8 Gauss eliminasyonu (~40 satır, OpenCV YOK).
- SkiaSharp warp: hedef 240×80 + her kenara %10 marj; bilinear ters-örnekleme.
- Dönüş: `(SKBitmap? Rectified, float SkewDeg, float PerspRatio)` — quad yoksa null (aşama atlanır).

**2.2 Birim testler**: sentetik quad (bilinen açıyla döndürülmüş dikdörtgen köşeleri) →
SolveHomography + warp → köşe koordinatları beklenen yerde (±1 px). Model gerektiren entegrasyon
testi `[SkippableFact]`: `models/wpod_lpd.onnx` varsa gerçek eğik kanıt kırpıntısında quad bulunur.

**2.3** `Anpr.Core/Imaging/CropEnhancer.cs`: aşama bayrakları + `Stopwatch` bütçesi
(`EnhanceBudgetMs=150`) + sıra: rectify → night → sr; her aşama koşul tetikli (skew/exposure/width).
Çıktı: zenginleştirilmiş bitmap listesi (en fazla 2: rectify'lı ve rectify+night'lı) + tanı meta.

**2.4** `PlateConfirmer.InjectObservation(trackKey, PlateReading)`: mevcut ize SlotSum ekler,
Variants'a katar, `Count` artırır; `Emitted` durumunu DEĞİŞTİRMEZ. Birim test: inject sonrası
uzlaşı düzelir, emit yine TEK.

**2.5 CameraWorker kancası**: DrainReady ÖNCESİ — onaylı ama uzlaşı <`EnhanceTriggerOcr` ve
`!track.Enhanced` izler için: CropBucket'tan en iyi 2 kırpıntı → CropEnhancer → OCR →
InjectObservation; `track.Enhanced=true`. Track'e Enhanced alanı için PlateConfirmer'a
`TryGetPendingLowConf(...)` erişimi eklenir (kapsülleme korunur).

**2.6 Ölçüm**: (a) offline `enhance_ab.py --rectify` açılı dilimde exact Δ; (b) temiz dilimde
regresyon 0; (c) diag4+live1h `--enhance rectify`; (d) canlı 1 gün: `enhance` loglarından
kurtarılan-iz sayısı + örneklem gözle. Kabul kapısı spec'teki gibi. Commit/push + gap-analizi.

## Faz 3 — Gece normalizasyonu (1-3 ms)

**3.1** `Anpr.Core/Imaging/NightNormalizer.cs`: gri kanalda %2-%98 histogram germe + gamma
(hedef ortalama ~120); yalnız `exposure` karanlık banttaysa.
**3.2** Birim test: sentetik karanlık gradyan → çıktı ortalaması hedef bantta.
**3.3** Ölçüm: `night_all.csv` (%84 taban) + karanlık dilim; kabul: gece exact ↑, gündüz regresyon 0.
CropEnhancer'a 3. aşama olarak bağla; commit/push.

## Faz 4 — ESPCN ×2 süper çözünürlük (koşullu)

**4.1 Model tedariki**: `.venv-train` içinde torch ESPCN (r=2) tanımla, rastgele değil —
önceden eğitilmiş ağırlık bul (torch hub / süper-çözünürlük repoları) ya da DIV2K-mini ile hızlı
eğit; ONNX export (opset 17; yalnız Conv+DepthToSpace — ConvInteger YOK doğrula:
`python -c "print(set(n.op_type for n in onnx.load(...).graph.node))"`).
**4.2** C# ORT yükleme + `PlateSuperResolver.cs`; genişlik<100 px tetiği.
**4.3** Ölçüm: küçük dilimde exact Δ; CPU süresi ölç (hedef ≤40 ms, aşarsa ELENİR).
Model bulunamaz/kapıyı geçemezse faz iptal → elenenler listesine (spec bunu öngörüyor).

## Kesişen işler

- Her fazda: `dotnet test` yeşil + diag4 referans korunur + README "elenenler/özellikler" güncellenir.
- Sonda: `docs/2026-07-06-...gap-analizi` 5g bölümü — dilim tabloları, kabul kararları,
  kurtarılan-iz istatistiği; memory güncellemesi; GitHub push.

## Riskler

| Risk | Azaltım |
|---|---|
| WPOD zayıf Intel'de 80 ms'yi aşar | bütçe sözleşmesi aşamayı atlar; hedef-PC pilotunda yeniden ölç |
| WPOD kırpıntıda quad bulamaz (arac-kadraj dağılımıyla eğitildi) | marjlı besleme reçetesi (canlı test edildi); bulamazsa null → zarif atlama |
| Kalite kapısı gece recall'ünü yer | eşik taraması gece setini ayrı raporlar; gece için ayrı eşik opsiyonu |
| SR modeli tedarik edilemez | faz koşullu — iptal yolu spec'te tanımlı |
