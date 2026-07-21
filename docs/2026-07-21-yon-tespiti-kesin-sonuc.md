# Yön tespiti (yaklaşan/uzaklaşan) — KESİN SONUÇ

Tarih: 2026-07-21 · Durum: per-kamera yörünge-tabanlı yön tespiti bu kapıda **KANITLI İMKÂNSIZ**.

## Ne denendi ve ne çıktı (hepsi canlı ölçüldü)
| Yaklaşım | Sinyal | Sonuç |
|---|---|---|
| 1.0.1 SizeTrend (kutu-alanı) | boyut eğilimi | 4 canlı koşu: trend hep ~1.0, 0 bastırma |
| 1.0.2 DirectionTracker (plaka-genişliği, yoğun) | boyut eğilimi | trend çoğu araçta -1 (kararsız) |
| Hareket-yolu (centroid net yer-değiştirme) | KONUM yönü | 6 araç: 5/6 move=(0,0), hepsi düz |

## Kök neden (fiziksel, çabayla ilgisi yok)
Bu kapı geometrisinde **plaka yalnız araç bariyere gelip yavaşladığında/durduğunda** güvenilir
tespit ediliyor (küçük yakın-zon). Ayırt edici sinyal — büyüme (boyut) VE hareket (konum) — araç
UZAKTAYKEN oluşuyor; ama orada plaka çok küçük olduğundan tespit edilemiyor. Yani ölçebildiğimiz
pencerede ne boyut değişiyor ne konum → yörünge-tabanlı hiçbir sinyal ayıramıyor. `RequireApproaching`
bu yüzden varsayılan KAPALI (açılsa bile iş yapmaz; telemetri olarak SizeTrend taşınır).

## Geriye kalan GERÇEK seçenekler (ikisi de daha önce reddedildi)
Yörünge işe yaramadığına göre, teknik olarak yalnız iki yol çalışır:

1. **Ön/arka araç-yüzü sınıflandırıcı** — TEK KAREden (yakın-zonda, tam da tespitin çalıştığı yerde)
   aracın önünü mü arkasını mı gördüğünü söyler. Yörünge gerektirmez → bu geometride çalışır. Senin
   gözleminle birebir ("arka plaka gözüküyor"). MALİYET: küçük bir sınıflandırıcı eğitmek gerekir
   (çapraz-kamera çiftlerinden otomatik etiketlenebilir: yaklaşan görünüm=ön, uzaklaşan=arka).
2. **Çapraz-kamera tekilleştirme** — araç iki kamerada da göründüğü için, önce YAKLAŞTIĞI kamera
   kazanır, diğer (arka-plaka) görünüm düşürülür. Model gerektirmez, %100 güvenilir. Ama "per-kamera"
   değil (iki kamera koordine olur). Not: Hikvision da iki olayı kaydediyor; bu bizi ondan iyi yapar.

## Öneri (dürüst)
Per-kamera + modelsiz + yörünge yaklaşımları **tükendi** (hepsi canlı elendi). Karar senin:
- **(A)** Ön/arka sınıflandırıcı eğitelim (birkaç günlük ML işi, senin gözlemine en uygun, per-kamera).
- **(B)** Çapraz-kamera tekilleştirme kuralım (bugün biter, modelsiz, güvenilir; koordineli).
- **(C)** Yön filtresini bırakalım (Hikvision gibi tüm araçları oku); enerjiyi #1 zaaf olan siyah-plaka
  fix'ine (whitelist snap, bu gece +27pp doğrulandı) ve diğer iyileştirmelere verelim.

Körlemesine bir şey daha kurup "oldu" demedim; ölçüp elediğim için bu netlik var.
