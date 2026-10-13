# Üçüncü taraf bildirimleri (Third-party notices)

`Anpr.Sdk` ve `Anpr.Core` paketlerinin **kendi kodu MIT lisanslıdır** (bkz. `LICENSE`). Paketin bağımlı
olduğu ya da içinde taşıdığı üçüncü taraf bileşenler **kendi lisanslarına** tabidir:

## NuGet bağımlılıkları (pakete gömülü değildir; bağımlılık olarak çözülür)

| Bileşen | Lisans |
|---|---|
| Microsoft.ML.OnnxRuntime | MIT |
| SkiaSharp (Skia: BSD-3-Clause) | MIT |
| YamlDotNet | MIT |
| Microsoft.Extensions.* | MIT |

## Pakete gömülü modeller (`assets/models`)

| Dosya | Kaynak | Lisans | Değişiklik |
|---|---|---|---|
| `yolo-v9-t-384-license-plates-end2end.onnx` | [ankandrew/open-image-models](https://github.com/ankandrew/open-image-models) | MIT (üst kaynağın proje lisansı) | yok — yayınlandığı hâliyle |
| `cct_xs_v2_erz.onnx` + yaml | [ankandrew/fast-plate-ocr](https://github.com/ankandrew/fast-plate-ocr) `cct-xs-v2-global` modelinin bu projede yapılmış ince ayarı | MIT | ince ayar (eğitim verisi pakette yoktur) |
| `cct_s_v2_global.onnx` + yaml | [ankandrew/fast-plate-ocr](https://github.com/ankandrew/fast-plate-ocr) | MIT | yok |
| `fsrcnn_x2.onnx` | [Saafke/FSRCNN_Tensorflow](https://github.com/Saafke/FSRCNN_Tensorflow) `FSRCNN_x2.pb` | Apache-2.0 | yalnız biçim dönüşümü (TensorFlow → ONNX) |

**YOLOv9 notu.** Dedektör, YOLOv9-t mimarisidir (Wang & Liao, *YOLOv9: Learning What You Want to Learn Using
Programmable Gradient Information*, 2024). Üst kaynak (open-image-models) ağırlıkları MIT lisanslı projesinin
parçası olarak yayınlar; ağırlıkların hangi YOLOv9 uygulamasıyla eğitildiğini ise belgelememiştir
(open-image-models, sorun #28). Ağırlıklar burada üst kaynağın yayınladığı hâliyle, değiştirilmeden dağıtılır.
Bu sizin kullanımınız için belirleyiciyse üst kaynağa danışın ya da `DetectorModelPath` ile kendi modelinizi verin.

## Pakete DÂHİL OLMAYAN isteğe bağlı model: WPOD-NET (rektifikasyon)

`EnhanceRectify` aşamasının kullandığı `wpod_lpd.onnx`, [WPOD-NET](https://github.com/sergiomsilva/alpr-unconstrained)
ağırlıklarından türemiştir ve o depo **GPL-3.0** lisanslıdır. MIT lisanslı bu pakete **konmamıştır** (2.0.0'dan
itibaren). Model dosyası yoksa aşama kendiliğinden kapalı kalır; tanıma hattının geri kalanı etkilenmez.
Aşamayı kullanmak isteyen, modeli kendi lisans değerlendirmesiyle temin edip `WpodModelPath` ile gösterir.

## ffmpeg

Paket ffmpeg **içermez**; kurulu `ffmpeg` ikilisini ayrı bir süreç olarak çalıştırır. ffmpeg'in lisansı
(derlemeye göre LGPL ya da GPL) kurduğunuz ikili için geçerlidir.
