# Hızlı-araç yakalama — fps & settle ölçümü (2026-07-22)

**Soru:** Hızlı araç geçişlerini daha iyi yakalamak için ne yapmalı? **Yöntem:** diag4 (15 dk çift-kamera,
4500 kare/kamera @5fps gerçek yakalama + poll gerçek-değer, kamera-başına 13 gerçek araç) üzerinde
`Anpr.Bench emitsim` ile canlı emit pipeline'ını OFFLINE simüle ederek, `camera_compare.py` ile poll
gerçek-değere karşı recall/precision. Üretim bayrakları: `--fuzzy 1 --minconfirm 2 --spatialiou 0.35
--skipchar 0.3 --qblur 600 --qdark .85 --qbright .80`, model `cct_xs_v2_erz.onnx`.

## Kök neden (kodda doğrulandı)
`PlateConfirmer.DrainReady`: iz `Count >= MinConfirmations(2)` (onaylı) VEYA `now-Last >= SettleWindow(3s)`
(settle) olunca emit eder. **Hızlı araç ROI'de az kare kalır:** ≥2 kare → onayla ANINDA emit (sorun yok);
**tam 1 kare → onaylanmaz → yalnız settle ile emit (araç geçtikten 3s sonra)**; 0 kare (fps'ten hızlı) →
tümden kaçar. Yani sorun OCR-model değil, **kare-bütçesi + emit-zamanlaması**.

## Bulgu 1 — fps↑ NET-NEGATİF (sezgisel "çözüm" bir REGRESYON)

| Kamera | fps | birebir | yakalama | kaçan | gürültü |
|--------|-----|---------|----------|-------|---------|
| giris | 2 | 12 (%92) | 12 (%92) | 1 (25SA969) | 5 |
| giris | 4/5 | 11 (%85) | **13 (%100)** | 0 | 8 |
| cikis | 2 | 10 (%77) | 11 (%85) | 2 (25ACV952, 25SA969) | 4 |
| cikis | 4 | 8 (%62) | 11 (%85) | **aynı 2** | 8 |

- **giris**: fps↑ 1 hızlı aracı (25SA969) kurtardı (+yakalama) ama birebir −1, gürültü +3.
- **cikis**: fps↑ HİÇBİR ŞEY kurtarmadı (aynı 2 kaçan), birebir −2, gürültü ×2.
- Net (iki kamera): +1 yakalama vs −3 birebir + gürültü iki katı + **zayıf hedef-PC'de ~2× CPU** (fps=4/5
  tüm 5fps kareleri işler; ROI yalnız OCR'ı ucuzlatır, ffmpeg decode + yolo tespiti değil).
- Kaçan araçlar (25ACV952 = hafızada "her oturumda kaçan" kronik-zor) **OCR/geometri-limitli, fps-limitli
  DEĞİL** — daha çok kare onları düzeltmiyor.
- **KARAR: fps varsayılanı DEĞİŞTİRİLMEDİ.** Sezgisel fix ölçümle regresyon. (Eski hafıza bulgusuyla da
  tutarlı: "fps recall'i kurtarmıyor"; 07-07'nin "ROI ile fps=5 işe yaradı"ı tek-araç/tek-koşula özeldi.)

## Bulgu 2 — settle↓ GÜVENLİ bariyer-gecikme kazancı (uygulandı)

`SettleWindow` YALNIZ 1-kare (onaylanmamış) izin emit ZAMANINI etkiler; 2+ kare onayla zaten anında emit.

| Kamera | settle=3 | settle=1 |
|--------|----------|----------|
| giris | 17 distinct | **17 distinct (birebir aynı liste)** |
| cikis | 16 distinct | **16 distinct** |

İki kamerada settle 1..3s emit sonucunu DEĞİŞTİRMİYOR → **saf zamanlama knob'u, sıfır recall/precision
bedeli.** 1-kare hızlı aracın bariyer okuması 3s yerine daha erken gelir. **Uygulandı: `SettleWindowSeconds`
varsayılanı 3→2** (kod + appsettings + pilot). Konservatif 2 (settle=1 de ölçülmüş güvenli, ama 2 canlıda
occlusion'la 1-3s geç gelen 2. kareye tolerans bırakır). Bench/regresyon default'u (3) tekrarlanabilirlik
için değiştirilmedi.

## Dürüst sonuç — sistem bu veride tavanına yakın
- Yakalama: giris %92, cikis %85 (n=13, GA geniş). **Sezgisel fps fix'i net-negatif.** Güvenli settle
  kazancı marjinal (1s gecikme, 1-kare alt-kümesi).
- **Kaçan araçlar OCR/geometri-limitli.** Bunların TEK ölçülü çözüm yolu (augmentation İKİ KEZ başarısız):
  (a) **`KnownPlates` whitelist snap** — sistematik yanlış-okuma sitedeki bilinen plakaya lev≤1 düzeltilir
  (offline +27pp; 25ACV952 gibi kronikler için birincil aday — **kullanıcıdan plaka listesi bekleniyor**);
  (b) zaten canlı ikinci-şans (WPOD rektifikasyon + FSRCNN SR) zor kırpıntılarda; (c) gerçek kış/zor-koşul
  verisi. 0-kare (fps'ten hızlı) araçlar ancak daha yüksek yakalama-fps'iyle gelir → **hedef-PC CPU
  başlığı ölçülmeden shipping edilemez** (skor-kartının en büyük bilinmeyeni).

## Sıradaki (ölçülebilir)
- **Güven-duyarlı settle** (profesyonel rafine): 1-kare okuma yüksek-güven+TR-geçerli ise HIZLI emit,
  belirsizse tam settle bekle → bu veride flat settle=2 ile aynı (belirsiz-1-kare vakası yok), canlıda daha
  dayanıklı. Uygulanabilir ama canlı-only fayda.
- Hedef-PC'de fps/CPU başlığı ölçümü (pilot kiti hazır) → 0-kare araçlar için fps kararı ancak o zaman.
