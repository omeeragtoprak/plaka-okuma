# Siyah (resmi) plaka OCR önişleme araştırması — NEGATİF sonuç (ELENDİ)

Tarih: 2026-07-21 · Yöntem: offline OCR A/B (kamera gerektirmez; kameralar bu gece RTSP arızasında).
Amaç: kabul raporunun (2026-07-13) #1 doğruluk zaafı olan **siyah resmi plakaları** (koyu zemin +
açık yazı; 25 LT 424, 25 TGG 001 gibi düzenli belediye/protokol araçları) önişlemeyle düzeltmek.

## Temiz test seti (gözle etiketli)
`dataset-corrections/` altındaki 53 hasat kırpıntısı montajla **gözle** etiketlendi (dosya adındaki
emit etiketi = düzeltmeye çalıştığımız YANLIŞ okuma olduğu için kullanılamaz). Kategoriler:
- **Siyah (resmi): 33** kırpıntı · **Beyaz: 12** · **Kırmızı: 2**
- Beyaz regresyon kontrolü için ayrıca servis kanıtından **400 beyaz-ağırlıklı** kırpıntı (emit-etiketli, ~yaklaşık gerçek).

## Zaafın niceliği (tek-kare, oylama YOK)
| Kategori | baseline exact | karakter% |
|---|---|---|
| **Siyah (resmi)** | **%51.5** (17/33) | %89.9 |
| Beyaz | %100 (12/12) | %100 |
| Kırmızı | %100 (2/2) | %100 |

Siyah plaka tek-kare OCR'ı belirgin zayıf; beyaz/kırmızı kusursuz. (Üretimde çok-kareli oylama bunu
yükseltir ama taban zaaf gerçek.)

## Denenen önişlemeler ve sonuç
Siyah exact / beyaz exact (tek-kare):

| Yöntem | Siyah | Beyaz | Karar |
|---|---|---|---|
| baseline (önişlemesiz) | 51.5 | 100 | — |
| invert (her zaman tersle) | 57.6 | 91.7 | beyazı bozar |
| auto_invert (parlaklık<eşik) | 60.6 | 91.7 | **eşik güvenilmez**: kırpıntı koyu araç-gövdesi bağlamı içerdiğinden beyaz medyanı da düşük (siyah medyan 28±28 vs beyaz 45±22, ayrışmıyor) |
| CLAHE | 42.4 | 100 | siyahı bozar |
| kontrast-germe | 48.5 | 100 | kazanç yok |
| **dual-pass** (orij+ters, GÜVENİ yüksek olanı seç) | 60.6 | 100* | temiz sette iyi ama... |

\* Temiz 12 beyazda 0 regresyon; ama **400 beyaz-ağırlıklı sette dual_always -2 puan** (9 kötüleşen /
1 iyileşen): beyaz plakanın tersi, sahte-yüksek-güvenli ÇÖP okuma üretip iyi okumayı bozuyor
(ör. 25FTH211→25FTH21, 24AE111→2552FF1).

## Adaptif + marj taraması (güvenli nokta arayışı)
Yalnız ilk güven <Tlow ise ters dene, ve ters ≥ orij+marj ise kabul et:

| Tlow | marj | Δsiyah | Δbeyaz |
|---|---|---|---|
| 0.93 (marjsız) | — | **+12.1** | −1.0 |
| 0.90 | 0.05 | +6.1 | −0.5 |
| 0.90 | 0.10 | +3.0 | −0.2 |
| 0.90 | 0.15 | +0.0 | +0.0 |

**Hiçbir operasyon noktası, beyazı bozmadan siyahta anlamlı kazanç vermiyor.** En "güvenli" nokta
+3 siyah / −0.2 beyaz — beyaz baskın sınıf (~%95 trafik) olduğu için NET etki nötr-negatif; CPU
(2× OCR) + karmaşıklığa değmez.

## Sonuç: ELENDİ
Önişleme (tersleme/CLAHE/germe/dual-pass), siyah resmi plakayı beyaz plakaları bozmadan düzeltemiyor.
Kök neden: model beyaz-plaka dağılımında eğitilmiş; siyah plaka farklı bir ışık/kontrast rejimi ve
kırpıntı bağlamı (koyu araç gövdesi) güvenilir bir "koyu-plaka" dedektörünü imkânsız kılıyor. Bu,
"augmentation koşul dayanıklılığı satın almıyor" dersinin kardeşi.

**Gerçek çözüm yolları (önişleme DEĞİL):**
1. **Saha verisi + fine-tune v5**: siyah resmi plaka kırpıntılarını (bunlar DÜZENLİ/frekanslı araçlar)
   temizce etiketleyip eğitim setine ekle. 33 temiz kırpıntı var; daha fazla hasat gerekir.
2. **Müdavim/whitelist snap**: siyah plakalı araçlar frekanslı (belediye protokol araçları). Okuma,
   bilinen bir frekans/whitelist plakasına lev≤2 yakınsa ona "snap"lenir. `FrequentPlateRegistry`
   zaten müdavimleri öğreniyor; bir sonraki adım bunu düzeltme için kullanmak (ayrı ölçüm gerekir).

## Yeniden üretim
`scripts/ocr_preprocess_ab.py` (bu araştırmanın taşınabilir hâli). Temiz gözle-etiketler ve kırpıntılar
KVKK gereği repoda değil (`dataset-corrections/`, gitignore'lu).
