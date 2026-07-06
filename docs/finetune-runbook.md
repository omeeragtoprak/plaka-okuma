# TR Fine-Tune Runbook — fast-plate-ocr'ı kendi geçit verimizle eğitme

Amaç: OCR'ı **kendi sahamızın** koşullarına (akşam/gece ışığı, kamera açısı, Erzurum plakaları)
uyarlamak. Mevcut `cct_xs_v2_global` zaten iyi (kamera-referanslı birebir ~%81); fine-tune kalan
zor vakaları (karanlık/parlama/açı) hedefler. **Mimari aynı kalır → zayıf-CPU maliyeti değişmez.**

## 1. Veri seti (otomatik, kamera etiketli)

Kameranın yerleşik ANPR'ı **bedava etiketleyici** olarak kullanılır:

```bash
# 1) Yoğun kare yakala (5 fps) — örn. 6 dk:
ffmpeg -rtsp_transport tcp -i "rtsp://USER:PASS@IP:554/Streaming/Channels/101" -vf fps=5 -t 360 -q:v 4 frames/f_%06d.jpg
# 2) Kırpıntıları hasat et (kaynak çözünürlükte plaka kırpıntıları):
dotnet run --project Anpr.Bench -- harvest frames/ crops/ --ocr cct_xs_v2_global.onnx --minocr 0.5
# 3) Kamera gerçek-değeriyle otomatik etiketle (poll .raw dosyaları + saat farkı):
python3 scripts/autolabel.py --crops-dir crops/ --t0 "<yakalama-başlangıcı-ISO>" \
  --poll-files poll_giris.raw --clock-file clock.txt --out dataset/finetune
```

- `autolabel` yalnız **belirsiz olmayan** eşleşmeleri alır (±12 sn içinde tek aday + edit-mesafe ≤2).
- Çıktı: `dataset/finetune/<PLAKA>/*.png` + `annotations.csv` (`image_path,plate_text`).
- Kamera ISAPI arama uç noktası **son ~20 kaydı döndürür** (pozisyon/zaman filtresi yok) →
  gerçek-değer ≤60 sn arayla POLL edilerek biriktirilmelidir (`scripts/camera_compare.py` bkz).
- Kamera saati Mac'ten ~+61 sn ileri (clock.txt: `kamera=<ISO> mac=<ISO>`).

**Yeterlilik eşiği:** ≥100 benzersiz plaka + ≥1500 kırpıntı olmadan fine-tune DENEME (26 plakayla
aşırı-öğrenme garantisi). Val bölmesi **plaka bazında** ayrılmalı (kare bazında değil — sızıntı).

## 2. Eğitim (fast-plate-ocr resmi akışı)

Config `models/cct_xs_v2_global_plate_config.yaml` ile **birebir aynı** kalmalı:
`max_plate_slots: 10`, `alphabet: '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_'`, `pad_char: '_'`,
`img_height: 64`, `img_width: 128`, `image_color_mode: rgb` (+ region başlığı: Turkey dahil 66 bölge).

Önceden eğitilmiş ağırlıklar + mimari config İNDİRİLDİ → `models/train/`:
`cct_xs_v2_global.keras` (10.4MB) ve `cct_xs_v2_global_model_config.yaml`
(kaynak: github.com/ankandrew/cnn-ocr-lp → release `arg-plates`; tüm .keras ağırlıkları orada).

```bash
pip install 'fast-plate-ocr[train]' 'fast-plate-ocr[onnx]' torch   # Keras 3 (torch backend) — CLI DOĞRULANDI
# Bölme (sızıntısız, plaka-bazlı, deterministik):
python3 scripts/make_split.py --annotations dataset/finetune/annotations.csv --out-dir dataset/finetune
# Veri kontrolü: fast-plate-ocr validate-dataset ...
KERAS_BACKEND=torch fast-plate-ocr train \
  --model-config-file models/train/cct_xs_v2_global_model_config.yaml \
  --plate-config-file models/cct_xs_v2_global_plate_config.yaml \
  --weights-path models/train/cct_xs_v2_global.keras \
  --annotations dataset/finetune/train.csv \
  --val-annotations dataset/finetune/val.csv \
  --epochs 30 --batch-size 32 --lr 0.0002 --output-dir trained/
# ONNX export (alt komut adı `export`; uint8 girdi + channels_last, mevcut C# ile aynı format):
KERAS_BACKEND=torch fast-plate-ocr export ... --plate-config-file models/cct_xs_v2_global_plate_config.yaml \
  --onnx-opset-version 17 --onnx-input-dtype uint8 --onnx-data-format channels_last --save-dir trained/
# DİKKAT: py3.13'te onnx<=1.16 derlenmiyor → güncel onnx ile export edip ir_version'u post-patch et
# (C# ORT 1.20 en fazla IR 10 yükler):
python3 -c "import onnx; m=onnx.load('trained/model.onnx'); m.ir_version=10; onnx.save(m,'trained/model_ir10.onnx')"
```

Referans: resmi fine-tune not defteri `examples/fine_tune_workflow.ipynb`
(github.com/ankandrew/fast-plate-ocr) ve docs → Training.

## 3. Doğrulama (KATI KURAL: ölçmeden değiştirme)

1. `scripts/camera_compare.py --ocr cct_xs_v2_erz.onnx` → diag4 + yeni pencerede kamera-referanslı
   birebir/yakalama/gürültü; **mevcut %81'i geçmeli** (GA'lar örtüşüyorsa daha fazla veriyle tekrar).
2. `Anpr.Bench evaluate` eski etiketli setlerde (s1, live1h) **regresyon** kontrolü.
3. ms/kare aynı kalmalı (mimari aynı) — yine de ölç.
4. Geçerse `appsettings.json` → `OcrModelPath` yeni model; eskisi dosyada kalır (geri dönüş).

## SONUÇ (2026-07-03 akşamı): FINE-TUNE KAZANDI — model üretildi: `models/cct_xs_v2_erz.onnx`

1 günlük otomatik-etiketli veri (1071 kırpıntı / 96 plaka; sabah+gündüz+az gece) + 30 epoch (Mac CPU,
dakikalar) + TF-backend export (`--no-simplify`, statik batch=1, uint8 NHWC, IR 8):

| Ölçüm | Orijinal | Fine-tune |
|---|---|---|
| val kırpıntı-exact (python) | %67.0 | **%75.0** |
| diag4 kamera-referans birebir (C#) | 21/26 | **22/26** |
| live1h recall / yanlış-emit (C#) | 86/101 / 42 | **87/101 / 31** |
| ms/kare | 27.6 | 27.6 (aynı mimari) |

**Varsayılan HENÜZ değiştirilmedi** (bilinçli): eğitim verisi ağırlıkla gündüz; GECE performansı
doğrulanmadan swap riskli (gece zaten en zayıf koşul). Öneri: birkaç gece hasadı + gece A/B → kazanırsa
`OcrModelPath: models/cct_xs_v2_erz.onnx` yap (eski model dosyada, anında geri dönülebilir).

Export tuzakları (ölçüldü): torch-backend export bu mimaride ÇALIŞMAZ ("kernel of unknown shape");
**TF backend + `--no-simplify` şart** (onnxslim MultiHeadAttention Gemm bias'ını bozuyor);
`--no-dynamic-batch` (C# zaten batch=1); tf2onnx IR 8 üretiyor (patch gerekmedi).

## Pilot doğrulaması (2026-07-03, hat uçtan uca ÇALIŞIYOR)

- 633 etiket / 67 plaka ile 2-epoch pilot: eğitim Mac CPU'da ~10-40 sn/epoch (30 epoch = dakikalar).
- **Baseline (orijinal cct_xs_v2_global, bizim val setinde, kendi python ölçümümüz): kırpıntı-exact
  %62.1, char %95.0.** Pilot 2 epoch'ta val_acc %63.3 (yükseliyor) → gerçek kazanç alanı var.
- Yakalanan tuzaklar: (1) CSV yolları CSV'nin KLASÖRÜNE göreli çözülür (make_split.py göreli yazar);
  (2) orijinal iki-başlı (region'lı) modelde `valid` bölge etiketi ister → baseline'ı
  `scratchpad/eval_onnx_val.py` benzeri düz-ONNX ölçümüyle al; (3) py3.13'te onnx 1.16 kurulmuyor →
  export sonrası ir_version=10 post-patch.
- Nihai kabul ölçütü python val DEĞİL: **C# `camera_compare` (kamera-referans) + live1h regresyonu.**

## Durum (2026-07-02 gecesi)

- `harvest` bench modu + `scripts/autolabel.py` eklendi; gece döngüsü `dataset/finetune`'ü dolduruyor.
- INT8 not: tam dinamik quantization C#'ta ÇALIŞMAZ (ORT 1.20 CPU'da ConvInteger yok);
  MatMul-only (`cct_xs_v2_global_mm8.onnx`) çalışıyor ve doğruluğu FP32 ile aynı (21/26).
  Statik QDQ (kalibrasyonlu) denenecek asıl yol; hız kararı HEDEF PC ölçümüyle verilir.
