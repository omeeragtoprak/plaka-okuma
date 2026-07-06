# Tasarım: Canlı Karşılaştırma Paneli (Hikvision ⇆ Bizim Model) — ✅ ONAYLANDI

Tarih: 2026-07-06 · Kapsam: kullanıcı seçimi **"Çekirdek + Görsel"** · KATI KURAL: kameraya YALNIZ
salt-okuma erişim; panel hiçbir kamera ayarı DEĞİŞTİRMEZ (çizimler sadece bizim yazılımın ROI'si).

## Amaç
Solda Hikvision'ın okuduğu plakalar, sağda bizim modelin okudukları CANLI düşer; kamera seçilir;
eşleşmeler renklenir; canlı önizleme üstünde SARI çokgenle BİZİM ROI çizilip kaydedilir. Parity'nin
sürekli, gözle görülür kabul testi.

## Mimari (servis-içi; internetsiz hedef PC'de çalışır)
Anpr.Service (mevcut minimal API) genişler — ayrı süreç/başka teknoloji YOK, CDN YOK (tek gömülü HTML).

### Yeni arka uç parçaları
1. **PanelEventSink : IEventSink** (mevcut sink desenine 4. üye) — bizim emitleri SSE abonelerine iter.
2. **CameraRecordPoller (hosted service)** — 15 sn'de bir iki kameranın son-20 kaydını POLL eder
   (mevcut salt-okuma ISAPI POST'u; kimlikler config'ten), (plaka,captureTime) bazında YENİ olanları
   SSE'ye iter. Saat farkı düzeltmesi uygulanır (ISAPI /System/time'dan periyodik).
3. **Son-kare tamponu** — CameraWorker zaten kareyi çözüyor; worker'a "son kareyi JPEG olarak sakla"
   (yalnız panel istemcisi varken, ~1 sn'de bir; ek CPU ≈ JPEG encode ×1/sn, ölçülecek) + son tespit
   kutuları listesi.
4. **RoiStore** — `GET/PUT /api/roi/{cameraId}`: çokgeni `roi.<cameraId>.json`'a yazar (appsettings'e
   dokunmaz), çalışan motora anında uygular (AnprEngine'e thread-safe `SetRoi` eklenir; volatile alan).
   Config'teki `Cameras[].Roi` başlangıç değeri; dosya varsa dosya kazanır.

### Yeni endpoint'ler
| Endpoint | İş |
|---|---|
| `GET /panel` | Gömülü tek-dosya HTML/JS/CSS |
| `GET /api/stream` | SSE: `{source:"camera"\|"ours", cameraId, plate, timeIso, conf?, box?}` |
| `GET /api/snapshot/{cameraId}` | Son kare JPEG (yoksa 503) |
| `GET /api/detections/{cameraId}` | Son tespit kutuları + ROI (JSON) |
| `GET/PUT /api/roi/{cameraId}` | ROI oku/kaydet (0-1000 normalize "x,y;x,y;...") |

### UI (tek sayfa, saf JS + canvas)
- Üst bar: kamera sekmeleri (Giriş/Çıkış) + oturum skorbordu (🟢 birebir n/% · 🟡 1-fark · 🔴 sadece-kamera ·
  🔵 sadece-biz) + bağlantı rozetleri (servis/kamera).
- Orta: canlı önizleme canvas'ı (~1 sn yenileme) + bizim tespit kutuları + kayıtlı ROI (sarı, yarı saydam).
  "ROI Çiz" modu: tıkla=nokta, çift-tık=kapat, Kaydet/İptal/Temizle. Kaydet → PUT /api/roi + anında uygulanır.
- Alt: iki sütun canlı akış (sol Hikvision, sağ biz; en yeni üstte, son 50). Eşleştirme: aynı kamera +
  ±30 sn + compact-eşit → 🟢; lev=1 → 🟡; eşleşmeyen taraf 🔴/🔵. Bizim satırda güven + (varsa) kırpıntı önizleme.
- SSE koparsa exponential backoff ile otomatik yeniden bağlanma; kamera poll hatasında sol sütunda rozet.

## Test / doğrulama
- Birim: RoiStore kalıcılık + hot-apply (AnprEngine.SetRoi), poller "yeni kayıt" diff mantığı. Eşleştirme/renk
  mantığı istemci JS'te (tek kural: ±30 sn + compact-eşit/lev1) — canlı smoke ile doğrulanır.
- Canlı smoke: servis + panel açık 10 dk; iki tarafta akış, ROI çiz-kaydet-uygula döngüsü; CPU farkı ölç
  (panel açık/kapalı) — zayıf-CPU bütçesini bozmamalı (hedef: +%5'ten az).
- Kabul: kullanıcı gözüyle canlı karşılaştırma (senin onayın).

## Kapsam dışı (V2)
Mavi tetik çizgisi (Core'a "çizgi geçilince emit" özelliği), CSV dışa aktarma, geçmiş tarayıcı, kimlik doğrulama.
