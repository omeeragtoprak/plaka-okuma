# Hedef PC Dağıtım Kontrol Listesi (zayıf ofis PC + otopark exe entegrasyonu)

## 1. Kopyalanacaklar
- `Anpr.Service` publish çıktısı (veya DLL entegrasyonu için `Anpr.Core` + `models/`).
- `models/`: `yolo-v9-t-384-...onnx`, **`cct_xs_v2_erz.onnx` + `cct_xs_v2_erz_plate_config.yaml`**
  (varsayılan; yedek: `cct_xs_v2_global.onnx`). `ffmpeg` PATH'te olmalı.
- `appsettings.json`: gerçek kamera IP/kimlikleri (repo'da YOK, gitignore'lu) — şablon: `appsettings.example.json`.

## 2. Kurulum sonrası İLK ölçümler (sıra önemli; hepsi Anpr.Bench ile)
1. **ms/kare**: kısa yakalama + `evaluate` → gerçek kare maliyeti. Mac 27.6 ms idi; zayıf x86'da 3-6x
   bekleniyor (TAHMİN — ölç!).
2. **Fps kararı**: ölçülen maliyetle `Fps=2` doğrula (2 kamera × 2 fps CPU bütçesine sığıyor mu;
   sığmıyorsa `Fps=1` — recall bariyer beklemesi sayesinde dayanıklı).
3. **INT8 yeniden-testi**: `cct_xs_v2_global_mm8.onnx` (Mac'te hız kazancı yoktu; VNNI'siz eski x86'da
   yavaş bile olabilir — ölçmeden kullanma). Kazanırsa erz modelin mm8'i de üretilebilir.
4. **OnnxAllowSpinning=false** denemesi: 2-çekirdekte boşta CPU farkını ölç (Mac'te fark yoktu).
5. **MinPlateWidthRatio**: `widthstats` ile saha geometrisine göre (~0.03-0.04) — yanlış-emit/recall dengesi.

## 3. Kamera tarafı (AYAR DEĞİŞTİRME YOK — yalnız okuma)
- ISAPI plaka sorgusu son ~20 kaydı döner (pozisyon/zaman filtresi çalışmaz) → gerçek-değer POLL ile.
- Kamera saati PC'den farklı olabilir (bizde +61-63 sn) → `scripts/camera_compare.py` clock-offset'li.
- Kabul testi: 15-30 dk `camera_compare` → hedef: birebir ≥%80 (gündüz), yakalama ≥%88.

## 4. Exe entegrasyonu
- HTTP: `GET /events/recent?count=N` (JSON; plate/plateCompact/timestamp/cameraId/confidence) veya
  `WebhookUrl` ile push. `/health` izleme.
- Olay anlamı: `fuzzy=1 + köprü-birleştirme + oylama` sonrası TEK uzlaşı/araç; dedup 8 sn.
- Kapsam: kamera şerit-tetikli, biz tüm kareyi okuruz → exe tarafında bariyer kararı için gerekirse
  `MinPlateWidthRatio` ile daralt (yoksa park halindeki araçlar da olay üretir — yanlış değil, kapsam farkı).

## 5. Bellek beklentisi (ölçüldü, 10 saatlik soak)
- RSS 150-300MB arasında salınır (GC+ORT arena; trafikle artar, geri iner) — **sızıntı yok** (10 saat,
  monoton büyüme yok, max 295MB). Kalıcı olarak >500MB görülürse araştırın.
- 24+ saat toplam canlı soak: **0 restart** (Mac; hedef PC'de ilk gün izleyin).

## 6. Geri dönüş / bilinenler
- Model geri dönüşü: appsettings'te `OcrModelPath` → `cct_xs_v2_global.onnx` (tek satır).
- Gece/IR en zayıf koşul (fine-tune ile %84'e çıktı); kötüleşirse gece hasadı + yeniden fine-tune
  (`docs/finetune-runbook.md`, tamamı bu Mac'te dakikalar sürüyor).
- Tekrarlayan zor plaka örneği: `25ACV952` (kirli/eğik olabilir) — fiziksel kontrol değerli.
- Sürekli iyileştirme: hasat döngüsü (`harvest`+`autolabel`) hedef PC'de de çalışır; veri büyüdükçe
  periyodik yeniden fine-tune önerilir.
