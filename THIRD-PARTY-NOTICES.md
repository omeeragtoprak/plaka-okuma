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
| `iwpod_net.onnx` | [claudiojung/iwpod-net](https://github.com/claudiojung/iwpod-net) `weights/iwpod_net.h5` (Silva & Jung, *A Flexible Approach for Automatic License Plate Recognition in Unconstrained Scenarios*, IEEE T-ITS 2021) | MIT | yalnız biçim dönüşümü (Keras → ONNX, `scripts/convert_iwpod.py`; çıktı farkı ≤ 1,5e-6) |

**YOLOv9 notu.** Dedektör, YOLOv9-t mimarisidir (Wang & Liao, *YOLOv9: Learning What You Want to Learn Using
Programmable Gradient Information*, 2024). Üst kaynak (open-image-models) ağırlıkları MIT lisanslı projesinin
parçası olarak yayınlar. Ağırlıkların yazarı, modeli **WongKinYiu/yolov9 (GPL-3.0) tabanlı kendi çatalıyla**
ürettiğini yazılı olarak belirtmiştir ([open-image-models #22](https://github.com/ankandrew/open-image-models/issues/22),
2026-06-14; [fast-alpr #47](https://github.com/ankandrew/fast-alpr/issues/47), 2026-07-05). GPL lisanslı kodla
eğitilmiş ağırlıkların lisans durumu hukuken belirsizdir; yazar da kesin bir hüküm vermemekte ve şüphe
durumunda kendi dedektörünüzü eğitmenizi önermektedir. Ağırlıklar burada üst kaynağın yayınladığı hâliyle,
değiştirilmeden dağıtılır. Bu sizin kullanımınız için belirleyiciyse kendi hukuki değerlendirmenizi yapın
ya da `DetectorModelPath` ile kendi modelinizi verin.

**IWPOD-NET lisans metni** (MIT'in koşulu gereği aynen aktarılır):

```
MIT License

Copyright (c) 2021 claudiojung

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

IWPOD-NET, aşağıdaki WPOD-NET'in **aynı yazarlarca** (Silva & Jung) geliştirilmiş ardılıdır; yazarlar onu
ve ağırlıklarını MIT ile yayınlamıştır. Pakete 2.2.0'dan itibaren varsayılan rektifikasyon modeli olarak girer.

## Pakete DÂHİL OLMAYAN eski model: WPOD-NET (rektifikasyon)

2.2.0'dan önce `EnhanceRectify` aşamasının kullandığı `wpod_lpd.onnx`,
[WPOD-NET](https://github.com/sergiomsilva/alpr-unconstrained) ağırlıklarından türemiştir ve o depo **GPL-3.0
(ya da sonraki bir sürüm)** lisanslıdır. MIT lisanslı bu pakete **konmaz** (2.0.0'dan itibaren). Artık gerekmez:
IWPOD-NET onun yerini alır ve bu projenin ölçümünde ondan daha iyi okur. Yine de kullanmak isteyen, modeli kendi
lisans değerlendirmesiyle temin edip `WpodModelPath` ile gösterir; tür kendiliğinden anlaşılır.

## ffmpeg

Paket ffmpeg **içermez**; kurulu `ffmpeg` ikilisini ayrı bir süreç olarak çalıştırır. ffmpeg'in lisansı
(derlemeye göre LGPL ya da GPL) kurduğunuz ikili için geçerlidir.
