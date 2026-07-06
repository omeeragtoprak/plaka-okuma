# OtoparkAnpr — Kendi Model vs Hikvision Kamera ANPR: Karşılaştırma + Gap Analizi

Tarih: 2026-07-02 · Kamera: **iDS-2CD7A46G0/P-IZHS** (Hikvision DeepinView, yerleşik ANPR)

## 1. Kameranın kendi ANPR'ına erişim (ISAPI)

Kameranın yerleşik plaka modeli, kayıtları ISAPI üzerinden veriyor:
- **Endpoint:** `POST /ISAPI/Traffic/channels/1/vehicleDetect/plates` (digest auth), `VehicleDetectPlateCond` gövdesiyle.
- **Dönen:** `plateNumber`, `captureTime` (+0300), `country=TUR`, `laneNo`, `direction`, `picName`.
- **Notlar:** Sayfa başına ~20 kayıt (pagination: `searchResultPosition`); `startTime` filtresi tam uygulanmıyor → istemci tarafında zaman filtrele. Plaka kırpıntı resmi (`picName`) indirilemedi (403). Kamera saati Mac'ten ~60 sn ileri.

## 2. 15 dk eşzamanlı karşılaştırma (kamera = %100 referans)

Kameranın gördüğü **37 farklı araç**. Kendi CANLI servisimiz:

| Sonuç | Sayı | Oran |
|---|---|---|
| Doğru okuduk (birebir) | 18 | %49 |
| 1-hane yanlış | 3 | %8 |
| **Hiç kaçırdık** | 16 | **%43** |
| Fazladan/gürültü (il-varyantı vb.) | 14 | — |

**Asıl açık DOĞRULUK değil, RECALL.** Önceki testler bunu gizlemişti (yalnız gördüğümüz araçları etiketliyorlardı).

## 3. Teşhis: model mi, yakalama mı? → **MODEL İYİ**

Kendi karelerimizi (giriş kamerası, 8 dk @5fps, 2400 kare) **yoğun** işledik ve aynı pencerede kameranın 9 plakasıyla karşılaştırdık:

| | Plakalar |
|---|---|
| Kamera (9) | 61BC200 · 25AFH277 · 25EJ169 · 25EB105 · 25ACR626 · 25AEL283 · 25AG010 · **34BSS48** · **25AEA791** |
| Biz (yoğun @5fps) | 61BC200 · 25AFH277 · 25EJ169 · 25EB105 · 25ACR626 · 25AEL283 · 25AG010 |

**Yoğun işlemede kameranın 9'undan 7'sini BİREBİR okuduk (%78), il-varyantı gürültüsü YOK.** → OCR modelimiz kötü değil; TR-fine-tuning gerekmiyor. Kalan 2 (34BSS48, 25AEA791) yoğun işlemede de kaçtı = **gerçek donanımsal yakalama açığı** (kameranın optimize pozlaması/sensörü).

**Sonuç: canlı servisteki %43 kaçırma + gürültü, EMİSYON MANTIĞINDAN kaynaklanıyor** (kendi kendine yaralanma), model/kamera kalitesinden değil.

## 4. Emisyon gap analizi (diag, emitsim modu)

| Ayar | Recall (kamera 9'a karşı) | Toplam emit | Gürültü |
|---|---|---|---|
| fps=1, minConf=2 (boşta) | 5/9 | 8 | az |
| fps=5, minConf=2, merge=true | 7/9 | 18 | 11 (rakam-varyantı) |
| fps=5, minConf=2, merge=false | 6/9 | 11 | 5 (il-varyantı) |
| **fps=5, minConf=2, fuzzy=2** | **7/9 (6 birebir)** | **8** | **1** |

İki kaldıraç:
1. **Recall = fps.** Boşta 1 fps aracın iyi karesini kaçırıyor; 5 fps tavana (7/9) ulaşıyor.
2. **Gürültü = varyant parçalanması.** Bir aracın geçişindeki il/rakam/harf yanlış-okumaları ayrı kimliklere düşüyor. `merge` ayarı türü değiştiriyor ama çözmüyor. **Çözüm: bulanık zamansal kümeleme (fuzzy)** — edit-mesafesi ≤N okumalar aynı araç → tek uzlaşı (kameranın içeride yaptığı). diag'da emit 18→8, gürültü 11→1.

## 5. Fuzzy kümeleme doğrulaması (live1h, 101 etiketli araç)

| Ayar | Recall | Toplam emit | Yanlış emit | Karakter benz. |
|---|---|---|---|---|
| fuzzy=0 (baseline) | %86.1 | 170 | 60 | %86.1 |
| **fuzzy=1 (varsayılan)** | **%85.1** | 153 | **46** | %85.3 |
| fuzzy=2 | %83.2 | 134 | 31 | %88.2 |
| fuzzy=2 + minConf=1 | %73.3 | 134 | 49 | %85.3 |

**fuzzy=1:** yanlış emit -%24 (60→46), recall bedeli sadece -1 araç → **bariyer için tatlı nokta**. **fuzzy=2:** yanlış emit yarıya (60→31), karakter %86→88 ama recall -3 (yoğun sette benzer-plakalı farklı araçları birleştirme riski). **Uyarı:** live1h etiketleri servisin kendi (parçalı) emitlerinden türetildi → fuzzy'nin "recall kaybı"nın bir kısmı **sahte** olabilir (etiketteki varyant-kopyaları doğru birleştirince metrik ceza veriyor). Kameraya karşı ölçülen diag (fuzzy=2 orada en iyiydi) daha güvenilir. `minConf=1` her durumda kötü → elendi.

**Karar:** varsayılan `FuzzyMergeDistance=1` (bariyerde recall öncelikli, güvenli, gürültüyü %24 azaltır). Düşük-trafik / precision-öncelikli sahalarda `2` (config).

## 6. Kararlar + öneriler

- **OCR modeli yeterli** — dense %78 birebir. Model değiştirme/fine-tuning öncelik değil.
- **Recall kaldıracı = fps** (boşta-gecikmeyi azalt / aktif fps yükselt). CPU-bağımlı → hedef PC'de ölçülerek ayarlanmalı (zayıf CPU kuralı).
- **Gürültü kaldıracı = fuzzy kümeleme** (config `FuzzyMergeDistance`). Bariyerde recall öncelikli → varsayılan ölçümle seçildi; `minConf=1` kullanılmaz.
- **Kalan ~%20 açık donanımsal** (kameranın ANPR-optimize pozlaması). Kapatmak için: tetik bölgesinde okuma + gerekiyorsa kameranın kendi ANPR'ını ISAPI'den TÜKETME (ANPR'lı noktalarda ~%100 bedava; kendi modelimiz ANPR'sız kameralar için).
