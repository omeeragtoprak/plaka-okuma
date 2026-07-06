# Tasarım: Proje Geneli Derin Gap-Analizi Raporu (karar dokümanı)

Tarih: 2026-07-06 · Onay: kullanıcı ("devam et", tasarım sunumu sonrası)

## Amaç
"Kendi ANPR modelimiz kameranın yerleşik ANPR'ını ikame edebilir mi, hangi şartlarla?" sorusuna
karar verdiren TEK kaynak: 4 günlük tüm ölçümler + gap envanteri + risk + yol haritası.
Hedef kitle: yönetim + teknik ekip (karar dokümanı; teknik ekler dahil).

## Çıktı
`docs/2026-07-06-proje-gap-analizi-ve-yol-haritasi.md` — mevcut dokümanlara (gece raporu,
finetune-runbook, deployment-checklist) referans verir, içerik TEKRARLAMAZ.

## Bölümler
1. Yönetici özeti (½ sayfa): sonuç + ikame önerisi ("tetik-bölgesi kapsamıyla evet, izlemeli").
2. Kanıt: koşul tablosu (öğleden-sonra/sabah/gece/canlı; n ve Wilson GA ile), fine-tune öncesi/sonrası,
   stabilite (24+ saat 0 restart, RSS 150-300MB sınırlı), pazartesi-sabah A/B (berabere, n=14).
3. Gap envanteri (kalp): her gap = kanıt → kök neden → kapatma yolu → maliyet/etki/öncelik.
   (a) gece/IR, (b) sabah alçak güneş (İKİ model için de, bugünkü kanıtla), (c) donanımsal pozlama
   (kapatılamaz kısım), (d) hedef-PC bilinmeyenleri, (e) kapsam farkı (şerit vs tüm-kare → ROI kararı),
   (f) köprüsüz varyant zincirleri, (g) tekrarlayan zor plakalar (25ACV952), (h) veri seti sınırları
   (127 plaka, tek saha/mevsim, val n=188).
4. Risk matrisi: olasılık × etki × azaltım (hedef-PC hızı, gece regresyonu, firmware/ISAPI değişimi,
   tek-model bağımlılığı, veri-drift).
5. Yol haritası (3 faz): Faz-1 hedef-PC pilotu → Faz-2 exe entegrasyonu + ROI → Faz-3 sürekli
   iyileştirme (hasat + periyodik re-tune + regression.sh kapısı).
6. Ekler: metodoloji dersleri (poll-truth, saat farkı, zaman filtresi, son-20 tuzağı), doküman haritası.

## İlkeler
- Her sayı ölçümden; n küçükse GA ile "gösterge" etiketi. Abartı yok; kapatılamayan gap açıkça söylenir.
- Kurallar görünür: TR plakaları + zayıf CPU + exe entegrasyonu.
