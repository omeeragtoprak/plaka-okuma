# Derin Gap Analizi — OtoparkAnpr (2026-07-21)

**Yöntem:** 11-boyut çok-ajanlı denetim (her boyutta gerçek kod okuyan bulucu → her bulgu için düşman
doğrulayıcı → sentez). 78 bulgu üretildi; **41'i düşman-doğrulamadan geçti**, 37 doğrulayıcı session-limit'e
takıldığından o bulgular ayrıca doğrulanamadı (aşağıda ⚠️ ile işaretli; en kritik 2'si hub tarafından elle
doğrulandı 🔷). Sentez raporu (bu dosya) hub (Claude) tarafından üretildi — CLAUDE.md gereği nihai
inceleme/sentez devredilmez.

**Doğrulama işareti:** ✅ düşman-doğrulandı · 🔷 hub elle doğruladı · ⚠️ bulucu-raporu, bağımsız doğrulanmadı.

---

## 0. AEP Gap Analizi (explore-formatı · `aep:explore`)

Bu rapor artık [Agentic Engineering Protocol](https://github.com/omeeragtoprak/agentic-engineering-protocol)
disiplini altında; aşağıdaki blok explore fazının zorunlu çıktısıdır (§3.1). Ayrıntı §1–§5'te.

```
As-Is (kodda doğrulanabilir):
  1. Motor olgun: detect→OCR→çok-kareli oylama→emit; gündüz-açık parite kanıtlı; 94/94 test yeşil;
     Faz A gramer-decoding emit-floor ile doğrulandı (docs/2026-07-21-gramer-decoder-kripinti-ab.md).
  2. DLL: AnprRuntime.PlateDetected olayı SENKRON worker-thread'inde çağrılıyor, back-pressure yok
     (AnprRuntime.cs:114); N kameradan eşzamanlı; sözleşme belirsiz.
  3. Sevk edilen SDK'da kalıcılık/retry YOK — zero-loss yalnız sevk-EDİLMEYEN Anpr.Service'te.
  4. Operasyon boşlukları: ffmpeg-eksik→sessiz başarı (Supervisor:136); ffmpeg-stderr LogDebug'da
     (FfmpegFrameSource:299); panel varsayılan korumasız (Program.cs:98).
  5. Senaryo: gündüz-açık ✓; kar/kapalı-otopark ÖLÇÜLMEDİ; motosiklet/plakasız/iki-satır/kuyruk BOŞLUK.
To-Be:
  - Otopark exe'sine GÜVENLE gömülebilen (net kontrat + back-pressure + config doğrulama),
    SAHADA teşhis edilebilen (ffmpeg/health görünürlüğü), KVKK-uyumlu, tüm-senaryo-dayanıklı DLL.
Gaps: bkz §2 — H1–H7 (critical/high), M1–M21 (medium), 27 low. En kritik 5 = §1 yönetici özeti.
Unknowns:
  - Hedef win-x64 PC'de sıfır ölçüm (ms/kare, AVX, ffmpeg varlığı); kar/kapalı-otopark verisi yok;
    ⚠️ ile işaretli 37 bulgu bağımsız doğrulanmadı (verifier session-limit); SkipCharLevel=0.3 canlı doğrulanmadı.
Baseline: `dotnet build OtoparkAnpr.sln -c Debug` + `dotnet test` → 94/94 YEŞİL (2026-07-21, .claude/aep-check.sh).
```

**Premise kontrolü (§3.1 skeptik):** Bu bir gap-AUDIT'i (uygulanmamış boşluklar), tekil değişiklik değil —
kapanış her boşluğu ayrı bir AEP görevi (explore→plan→implement→verify) olarak sürmeli; `gap-auditor` her
kapanışta kanıt talep etmeli. Motor doğruluğu zaafı DEĞİL; eksen = gömülebilirlik + operasyon + senaryo-kapsamı.

---

## 1. Yönetici özeti

Motor ve boru hattı olgun (gündüz-açık dilimde parite kanıtlı, 94 test, gece 11 bug kapatıldı). Ancak
**DLL ürünleştirmesi denetlenmemiş bir kontrat yüzeyi getirdi** ve sevk sınırı yeni boşluklar açtı. En
kritik 5 boşluk **bariyer davranışını ve saha teşhisini** etkiliyor, motor doğruluğunu değil:

1. **Kuyrukta gerçek ikinci araç düşürülüyor** — varyant/müdavim bastırma, arka arkaya gelen İKİ FARKLI
   aracı "aynı geçişin varyantı" sanıp ikincinin bariyerini açmıyor (araç sıkışır). ✅HIGH
2. **ffmpeg eksikse sessiz ölüm** — StartAsync başarı döner ama hiç kare gelmez, hata yok, bariyer hiç
   açılmaz. Saha kurulumunda en olası sessiz başarısızlık. 🔷HIGH
3. **Arıza sebebi görünmez** — ffmpeg stderr (bağlantı/auth/codec hatası) LogDebug'da; üretimde
   operatör NEDEN kameranın çalışmadığını göremez. 🔷HIGH
4. **Panel varsayılan olarak korumasız** — PanelToken boş + 0.0.0.0 bind ⇒ canlı görüntü/plakalar/ROI
   tüm LAN'a açık; dağıtılan pilot config de böyle. ✅HIGH
5. **Sevk edilen SDK'da kalıcılık/retry YOK** — tüm zero-loss mühendisliği sevk edilmeyen Anpr.Service'te;
   PMS entegratörü bunu kendi yazmak zorunda ve bunu bilmiyor. ✅MEDIUM (etki: HIGH)

---

## 2. Şiddet-sıralı bulgu tablosu

### CRITICAL / HIGH

| # | Dv | Boşluk | Yer | Senaryo / etki | Düzeltme |
|---|----|--------|-----|----------------|----------|
| H1 | ✅ | Varyant/müdavim bastırma kuyrukta GERÇEK ikinci aracı düşürüyor | `CameraWorker.cs:548` (+`AnprServiceOptions.cs:204` VariantSuppressSeconds=30, lev≤2) | Aynı il+harf bloklu iki farklı araç arka arkaya gelince ikincisi "varyant" sayılıp emit edilmez → bariyer açılmaz, sıkışma | Bastırmayı zaman+konum+güven ile daralt; ROI-içi ardışık farklı-plaka için bastırmayı devre dışı bırak; kuyruk senaryosu testi |
| H2 | 🔷 | ffmpeg yoksa/PATH'te değilse StartAsync SESSİZCE başarı döner | `AnprCameraSupervisor.cs:136` (worker.Start fire-and-forget; :156 koruması yalnız started==0'da fırlar) | Hedef PC'de ffmpeg kurulu değilse: hata yok, kamera bağlı=false, hiç kare, bariyer hiç açılmaz, sessiz | Açılışta ffmpeg varlık+versiyon probe'u (net hata); veya ilk-kare-timeout'ta StartAsync'i başarısız say |
| H3 | 🔷 | ffmpeg stderr (arıza sebebi) LogDebug'da gizli | `FfmpegFrameSource.cs:299` | Kamera bağlanmayınca operatör "kare yok" görür ama sebebini (auth/timeout/codec) göremez → saha teşhisi çöker | Akış başarısızken son-N stderr satırını Warning'de yayınla; /health'e son-ffmpeg-hata alanı |
| H4 | ✅ | PanelToken boş varsayılan + 0.0.0.0 bind ⇒ görüntü/plaka/ROI LAN'a açık | `Program.cs:98` + `pilot-kit/win64/app/appsettings.json:41` | Ağdaki herkes canlı kamerayı, plaka geçmişini, ROI'yi görür/değiştirir (KVKK+güvenlik) | Varsayılan 127.0.0.1 bind; PanelToken boşsa panel/api'yi KAPAT (yalnız /health); pilot config'e zorunlu token |
| H5 | ⚠️ | Windows servis modunda TÜM loglar kaybolur (yalnız console sink) | `Anpr.Service/Program.cs:10` | Servis olarak koşarsa console hiçbir yere gitmez → sıfır teşhis; saha olayları kör | Dosya/EventLog sink ekle; UseWindowsService + rolling file |
| H6 | ⚠️ | Windows servis barındırma kodu yok (dokümante edilen oto-başlat/çökme-restart yok) | `Anpr.Service.csproj` | "7/24 oto-başlat" iddiası uygulanmamış; çökünce elle başlatma | Microsoft.Extensions.Hosting.WindowsServices + kurulum betiği |
| H7 | ⚠️ | Webhook kuyruk-derinliği + disk metriği /health ve heartbeat'te yok | `Program.cs:149` | Kayıt-webhook kuyruğu birikirse (downstream down) fark edilmez → sessiz kayıt kaybı | /health'e queueDepth+diskFree; heartbeat'e kuyruk sayacı |

> **Kapanış (2026-07-21, AEP döngüsü):** ✅ **H2 KAPANDI** — `FfmpegFrameSource.EnsureAvailable` açılış probe'u, `AnprCameraSupervisor.StartAsync`'te fail-fast; eksik ffmpeg artık NET hata. ✅ **H3 KAPANDI** — ffmpeg stderr LogDebug→LogWarning (üretimde görünür). ✅ **H4 KAPANDI** — `HttpBindAddress` varsayılanı `0.0.0.0`→`localhost` (loopback) + `SecurityChecks.IsInsecureExposure` ile LAN-açık+boş-token'da gürültülü uyarı (onaylı Option A; localhost:8090 erişimi korundu). Her üçü adversarial-reviewer **NOT REFUTED (0 blocker)**, **105 test** yeşil. Kalan critical/high: **H1** (canlı/ölçüm gerektirir), **H5–H7**. ✅ **[32] KAPANDI** — `lastPlate` /health payload'ından çıkarıldı (KVKK; /health token-muaf olduğundan plaka metni sızmamalı, izleme için lastEventUtc yeterli).

### MEDIUM

| # | Dv | Boşluk | Yer | Not |
|---|----|--------|-----|-----|
| M1 | ✅ | CameraWorker public API'de sızıyor (somut mutable, public Start/DisposeAsync) | `CameraWorker.cs:134`, `AnprRuntime.cs:47` | Salt-okunur `ICameraStatus` arayüzü dışa ver — yoksa her iç değişiklik kırıcı |
| M2 | ✅+🔷 | Olay işleyici SENKRON worker thread'inde + N kameradan EŞZAMANLI + back-pressure yok | `AnprRuntime.cs:92,114` (birleşik: 7,8,26,38) | Yavaş PMS handler'ı boru hattını durdurur+kare düşürür; iki kamera aynı handler'a yarışır. Tek-dispatcher bounded-queue + XML-doc'ta "eşzamanlı+hızlı-dönmeli" |
| M3 | ✅ | Kuyrukta ROI-içi çok plaka → hepsi roi emit; hedef-araç seçimi yok | `AnprEngine.cs:222` | Bariyer yanlış araca açılabilir; "en büyük+en alttaki" kuralı yok |
| M4 | ✅ | Sevk edilen SDK'da kalıcılık/retry YOK (zero-loss yalnız Service'te) | `AnprRuntime.cs:94` | PMS entegratörü webhook-kuyruğunu kendi yazmalı; belgelenmeli |
| M5 | ✅ | PlateEvent/webhook yükünde şema/sözleşme sürümü yok | `PlateEvent.cs:8` | Alan değişince PMS sessizce kırılır; `schemaVersion` ekle |
| M6 | ✅ KAPANDI | Açılışta config DOĞRULAMA yok — aralık-dışı eşik sessizce kabul, bariyer sessizce hiç açılmaz | `AnprServiceOptions.cs` | ✅ `Validate()` (fps>0, eşikler [0,1], port, kamera RtspUrl/Ip, yinelenen Id) → tüm sorunları tek net hatada; `AnprCameraSupervisor.StartAsync` başında (DLL+Service ortak). adversarial NOT REFUTED; 118 test |
| M7 | ✅ | SDK options, SDK'nın UYGULAMADIĞI alanları içeriyor (WebhookUrl sessizce yok sayılır) | `AnprServiceOptions.cs:277` | Sunucu-only alanları ayır veya "SDK'da etkisiz" işaretle/uyar |
| M8 | ✅ | MaskPlatesInLogs SDK'da fiilen etkisiz — worker tam plakayı loglar | `CameraWorker.cs:510` (+Service:201) | KVKK; tüm log yollarında maskeleme uygula |
| M9 | ✅ | KVKK budaması camera-records.jsonl + corrections.jsonl için yok — plaka PII süresiz birikir | `CameraRecordPoller.cs:40` | events.jsonl gibi retention-rotasyon uygula |
| M10 | ✅ | Kamera admin şifresi + kimlik-gömülü RTSP appsettings'te düz metin; iki kamerada aynı şifre | `appsettings.json:80` (gitignored gerçek config; pilot da böyle) | Env/secret store; en az pilot dağıtımında |
| M11 | ✅ | KnownPlates snap güven-kapısı varsayılan 1.0 (kapalı) — yüksek-güvenli gerçek komşu plaka protokole yeniden yazılabilir; SnapMaxLev↔SnapMaxOcrConfidence bağlı değil | `AnprServiceOptions.cs:197,185` | SnapMaxLev>1 iken güven-kapısını otomatik düşür; belgelenmeli |
| M12 | ⚠️ | Kimlik anahtarı rakam bloğunu YOK SAYIYOR — aynı il+harf farklı araçlar tek ize düşer | `PlateConfirmer.cs:325` | Track-key'e rakam bloğunu dahil et (H1 ile ilişkili) |
| M13 | ⚠️ | Kanonik OCR modeli 3 yüzeyde çelişiyor; pilot yanlış modeli sevk ediyor olabilir | `AnprServiceOptions.cs:44` | Tek kaynak; pilot config'in erz modelini kullandığını doğrula |
| M14 | ✅ KAPANDI | /health üst-durumu bir kamera bağlıysa 'ok' — kör bariyer kamerasını maskeler | `Program.cs:146` | ✅ `HealthStatus.Compute`: all→ok/some→degraded/none→down; testli (111 test) |
| M15 | ⚠️ | DLL modunda kamera bağlantı-durumu değişimi için olay/callback yok | `AnprRuntime.cs:47` | `CameraStatusChanged` olayı — PMS sessizce körleşmesin |
| M16 | ⚠️ | PlateFuser hizalama referansı=crops[0], boyut referansı=en büyük (uyumsuz) | `PlateFuser.cs:28` | Hizalama+boyut referansını en büyük kırpıntıya birle |
| M17 | ⚠️ | PlateRectifier2.Invert3x3 tekilliğe korumasız (kötü-koşullu homografide çöp) | `PlateRectifier2.cs:216` | det≈0 kontrolü → rektifikasyonu atla, ham kırpıntı |
| M18 | ⚠️ | NuGet paketlerinde LICENSE metadata/dosya yok | `Anpr.Sdk.csproj:12` | PackageLicenseExpression + LICENSE; herkese açık pakette şart |
| M19 | ⚠️ | Sevk DLL'de debug sembol/SourceLink yok — saha çökme yığını okunamaz | `publish.yml:49` | snupkg + SourceLink |
| M20 | ⚠️ | Paket README'si ESKİ mimariyi (bağımsız webhook) gösteriyor | `docs/entegrasyon-kilavuzu.md:60` | DLL/AnprRuntime akışına güncelle |
| M21 | ⚠️ | Sürüm iki csproj'de 0.9.0 sabit; Sdk→Core NuGet bağımlılığı pack sürümüne sabit | `Anpr.Sdk.csproj:13` | Tek `<Version>` (Directory.Build.props); SemVer + CI tag'i |

### LOW (özet — tam liste workflow çıktısında)
Kaynak/temizlik: `_pendingPublishes` dispose-thread yarışı (L, `CameraWorker.cs:627`); çift-Start non-atomik
(`AnprRuntime.cs:79`); ONNX çıkarımı LongRunning değil→havuz açlığı (`CameraWorker.cs:138`); Dispose kendi
kendine iptal etmiyor 30s bloklar (`CameraWorker.cs:613`); Start/Stop CancellationToken yok sayılıyor
(`AnprCameraSupervisor.cs:65`); başarısız-başlangıç instance'ı yeniden-kullanılamaz kılıyor (dispose+new gerekir,
`AnprRuntime.cs:80`); FfmpegFrameSource bariyersiz alanlar (`:31`); StopAsync sıralı dispose host-timeout aşabilir
(`AnprCameraSupervisor.cs:175`); webhook TLS/auth/imza yok (`WebhookEventSink.cs:46`); HttpClient timeout yok
(100s, `:50`); belirteç query-string'de + sabit-zamanlı olmayan `!=` (`Program.cs:109`); /health son plakayı
auth'suz sızdırıyor (`:143`); heartbeat+plaka aynı URL'e `type` ayırt edici yok (`HeartbeatService.cs:36`);
settle yolu Count=1 emit (MinConfirmations atlar, `PlateConfirmer.cs:193`); constrained decoder en uzun plaka için
≥8 slot ister (`TrConstrainedDecoder.cs:94`); detector class_id filtrelenmiyor (`LicensePlateDetector.cs:104`);
Fps üst-sınırı yok/IdleFps>Fps (`CameraWorker.cs:143`).

---

## 3. Senaryo-kapsama matrisi

| Senaryo | Durum | Not |
|---------|-------|-----|
| Gündüz / açık otopark | ✅ KAPSANDI | Parite kanıtlı (250-araç kabul, exact %87.3 / N-1 %96.3) |
| Gece / IR aşırı-pozlama | 🟡 KISMİ | Parlaklık kapısı plakayı sessizce eler, LowConfidence fallback yok (`AnprEngine.cs:253`); gece %84 kırpıntı; kök çözüm=gerçek kış verisi |
| Kar / kış | ⬛ ÖLÇÜLMEDİ | Mevsim verisi yok; skor-kartında 0 puan (dürüst) |
| Açılı plaka | 🟡 KISMİ | WPOD rektifikasyon (PlateRectifier2) canlı ama tekillik-korumasız (M17); augmentation 2× başarısız |
| Motosiklet (ön plakasız) | ⛔ BOŞLUK | Fiziksel: ön-okuma kapısı okuyamaz; PLAKASIZ_ARAC olayı da yok → bariyerde hiç sinyal |
| Plakasız araç | ⛔ BOŞLUK | Okunamayan araç için olay üretilmiyor (`CameraWorker.cs:223`) — operatör-onay tetiği yok |
| İki-satırlı plaka (moto arka/ticari) | ⛔ BOŞLUK | Normalizer tek-satır 7-8 karakter varsayar (`TurkishPlateNormalizer.cs:69`) |
| Kuyruk / aynı karede çok araç | ⛔ BOŞLUK | Hedef-araç seçimi yok (M3); üstelik ikinci araç bastırılabilir (H1) — **çift risk** |
| Hızlı araç / az-kare | 🟡 KISMİ | Settle yolu oylamasız tek okuma + 3s gecikme (`PlateConfirmer.cs:194`) — bariyer için geç+riskli; füzyon kapalı |
| Siyah protokol plaka | 🟡 HAZIR | Whitelist snap +27pp doğrulandı ama KnownPlates listesi bekleniyor (kullanıcı) |
| Çapraz-kamera aynı araç | 🟡 KISMİ | İçerik-bazlı çift-emit koruması yok (`CameraWorker.cs:46`); giriş+çıkış aynı bariyerde mükerrer |
| Bariyer karar gecikmesi | 🟡 KISMİ | Settle/confirm beklerken araç geçebilir; erken-emit güven-eşiği ayarı var ama ölçülmedi |
| RTSP kesinti / güç kaybı | ✅ KAPSANDI | Watchdog + cooldown + oto-reconnect; ffmpeg-öksüzlük gece düzeltildi (ama H2/H3 teşhisi zayıf) |
| ffmpeg yok (hedef PC) | ⛔ BOŞLUK | H2: sessiz başarısızlık |

---

## 4. Öncelikli aksiyon listesi

**A) Offline / kod (şimdi yapılabilir — düşük risk, yüksek etki):**
1. **H2** ffmpeg açılış-probe'u (sessiz-ölümü önler) — küçük, kritik.
2. **H3** ffmpeg stderr'i başarısızlıkta Warning'e yükselt + /health son-hata alanı.
3. **H1+M12** kuyruk-ikinci-araç bastırmasını daralt (rakam bloğunu track-key'e; ROI-içi ardışık farklı-plaka bastırma-dışı) + **kuyruk testi**.
4. **H4** panel varsayılan güvenli (127.0.0.1 + token yoksa panel kapalı) + pilot config düzelt.
5. **M2** olay dağıtımını tek bounded-dispatcher'a al (senkron/eşzamanlı/back-pressure üçünü birden çözer) + XML-doc.
6. **M6** açılışta config doğrulama; **M5** schemaVersion; **M1** ICameraStatus; **M8+M9** KVKK maskeleme+budama; **M15** CameraStatusChanged olayı.
7. **Test boşlukları** (⚠️ HIGH): DLL kontratı (handler-istisna/çift-start/dispose), CameraWorker yaşam-döngüsü, InMemoryEventStore kalıcılık, varyant-bastırma, eşzamanlılık, WebhookEventSink zero-loss.
8. Paketleme: **M18** LICENSE, **M19** SourceLink, **M20** README, **M21** tek-sürüm.

**B) Kullanıcı kararı gerektiren:**
- KnownPlates listesi (siyah-plaka fix aktivasyonu); M4 (PMS'te retry sorumluluğu kimde?); motosiklet/plakasız/iki-satır politika kararı (bariyerde ne yapılsın?).

**C) Canlı / hedef-PC gerektiren:**
- H5/H6 Windows-servis kurulumu + dosya log (pilot); gece/kar/kapalı-otopark ölçümleri; SkipCharLevel 0.3 canlı yeniden-doğrulama; kuyruk davranışı canlı.

---

## 5. Dürüstlük notları
- 41 bulgu düşman-doğrulandı; 37'si (paketleme/test/gözlemlenebilirlik boyutlarının doğrulayıcıları)
  session-limit'e takıldığından ⚠️ ile işaretli — **bağımsız doğrulanmadı**; en kritik 2'si (H2,H3) hub'ca elle
  doğrulandı. ⚠️ bulguları uygulamadan önce kodda hızlı teyit edilmeli.
- Doğrulayıcılar uygun şüpheciydi: 37 bulguyu çürüttü, çoğu HIGH iddiayı MEDIUM/LOW'a indirdi (ör. hub'ın
  "başarısız-başlangıç kilidi" HIGH iddiası → LOW: PMS dispose+new ile kurtulabilir).
- Bazı bulgular çakışıyordu (eşzamanlı-PlateDetected 3×, MaskPlatesInLogs 2×, PanelToken 3×) — burada birleştirildi.
- Motor DOĞRULUĞU zaafı değil; bulguların çoğu **kontrat/entegrasyon/operasyon/senaryo-kapsama** — yani
  "otopark exe'sine güvenle gömülür mü + sahada teşhis edilir mi" ekseni. 07-13 skor-kartının denetlemediği eksen.
