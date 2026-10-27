# models/arsiv — ölçülüp elenmiş model denemeleri

Bu klasördeki dosyaların **hiçbiri** ürünün (NuGet paketi `Anpr.Sdk`) ya da testlerin kullandığı model değildir.
Silinmediler: her biri bir ölçümün kanıtıdır ve "bunu denedik mi?" sorusunun cevabı burada durur
(§P.3: elenen teknikler arşivlenir, silinmez). Yeniden denemeden önce `AGENTS.md` §P.4 "KALICI KISITLAR"a bakın.

## Kullanılanlar (arşivde DEĞİL, `models/` kökünde)

| dosya | görevi |
|---|---|
| `yolo-v9-t-384-license-plates-end2end.onnx` | plaka bulucu (dedektör) — pakette |
| `cct_xs_v2_erz.onnx` + yaml | plaka okuyucu (OCR), Erzurum ince ayarı — pakette, varsayılan |
| `cct_s_v2_global.onnx` + yaml | isteğe bağlı "doğruluk profili" OCR (~4× CPU) — pakette, varsayılan DEĞİL |
| `fsrcnn_x2.onnx` | küçük plakayı büyütme (süper çözünürlük) — pakette |
| `iwpod_net.onnx` | eğik plaka düzeltme (IWPOD-NET, MIT) — pakette, 2.2.0'dan beri varsayılan |
| `wpod_lpd.onnx` | ESKİ eğik plaka düzeltme (GPL-3.0) — pakete GİRMEZ; yalnız açıkça seçilirse |
| `cct_xs_v1_global.onnx` + yaml, `sample_car.jpg` | yalnız birim testleri (örnek "UD 1234" görseli) |
| `train/` | ince ayarın başlangıç noktası (Keras) — yeniden eğitim için gerekli |

## Arşivdekiler

| dosya | ne denendi | sonuç |
|---|---|---|
| `cct_s_v1_global.onnx` + yaml | AlprNet'in özgün OCR'ı (s v1) | v2 ailesiyle değiştirildi (2026-06) |
| `cct_xs_v2_global.onnx` + yaml | Erzurum ince ayarından ÖNCEKİ genel xs v2 | ince ayar (`erz`) yerini aldı; ölçüm betikleri referans olarak kullanır |
| `cct_xs_v2_global_int8.onnx` + yaml | tam dinamik INT8 nicemleme | C#'ta çalışmaz: ORT CPU'da `ConvInteger` yok (0 okuma) — elendi (2026-07-03) |
| `cct_xs_v2_global_mm8.onnx` + yaml | yalnız MatMul INT8 | doğruluk aynı, Mac'te hız kazancı yok (35,5 → 35,6 ms) — elendi (2026-07-03) |
| `cct_xs_v2_erz_qdq.onnx` + yaml | erz modelinin QDQ nicemlemesi | doğruluk dengi, hız kazancı yok; hedef-PC yedeği olarak tutuldu (2026-07-11) |
| `cct_xs_v3_erz.onnx` + yaml | perspektif/kesme artırmalı ince ayar | val %76,2 vs %76,4 — berabere, elendi (2026-07-10) |
| `cct_xs_v4_erz.onnx` | düşük çözünürlük bozulma artırmalı ince ayar | küçük 27→26, karanlık 96→90 — elendi (2026-07-11) |
| `yolo-v9-t-256-…onnx` | daha küçük dedektör girişi | hız +%37 ama yakalama çöktü (14/26) — elendi (2026-07-03) |
| `yolo-v9-t-416-…onnx` | ara boy dedektör | birebir doğru %85 → %73 — elendi (2026-07-08) |
| `yolo-v9-t-512-…onnx` | daha büyük dedektör girişi | kazanç yok, CPU artar — elendi (2026-07-03) |
| `yolo-v9-t-384-int8.onnx` | dedektör dinamik INT8 | aynı `ConvInteger` engeli — elendi (2026-07-03) |
| `yolo-v9-t-384-qdq.onnx` | dedektör statik QDQ | boyut 7,6 → 2,6 MB ama birebir doğru 22 → 21 — elendi (2026-07-11) |
