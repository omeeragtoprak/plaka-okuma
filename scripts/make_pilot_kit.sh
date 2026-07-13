#!/bin/bash
# Hedef-PC pilot kiti: self-contained win-x64 yayın + modeller + örnek config + ölçüm talimatı.
# Kullanım: bash scripts/make_pilot_kit.sh  -> pilot-kit/OtoparkAnpr-pilot-win64.zip
set -e
cd "$(dirname "$0")/.."
OUT=pilot-kit/win64
rm -rf pilot-kit && mkdir -p "$OUT"

dotnet publish Anpr.Service -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "$OUT/app"
cp Anpr.Service/appsettings.example.json "$OUT/app/appsettings.json"

cat > "$OUT/PILOT-TALIMAT.md" << 'MD'
# Hedef-PC Pilot Kiti — Kurulum ve Ölçüm Sırası

## 0. Ön koşullar
- Windows 10/11 x64; ffmpeg.exe PATH'te (https://www.gyan.dev/ffmpeg/builds/ -> release-essentials)
- CPUID kontrolü (AVX/AVX2): PowerShell'de:
  `Get-CimInstance Win32_Processor | Select Name; coreinfo -f` (Sysinternals) — AVX2 yoksa NOT AL.

## 1. Yapılandırma
`app/appsettings.json` içinde Cameras[].Ip/Username/Password ve RtspUrl doldur.
KVKK: canlıda `PanelToken` doldur; `MaskPlatesInLogs` kararını işletmeciyle ver.

## 2. İlk çalıştırma + ölçüm (en az 1 saat)
`app\Anpr.Service.exe` çalıştır; http://localhost:8090/panel ve /health kontrol et.
ÖLÇ: /health'ten framesProcessed artış hızı (efektif fps), Görev Yöneticisi'nden CPU% ve RAM.
KABUL: ort CPU <%50, RSS <1.5GB, kare işleme sürüyor, olaylar düşüyor.

## 3. Hız yetmezse (sırayla dene, her adımda ölç)
a) appsettings: "Fps": 3, "IdleFps": 1
b) OCR'ı QDQ'ya al: "OcrModelPath": "models/cct_xs_v2_erz_qdq.onnx" (doğruluk eşdeğer ölçüldü)
c) OpenVINO EP (Intel CPU): docs/2026-07-08-pts-raporu-karsilastirmali-analiz.md §5.4 notu —
   2025.x tabanlı paket (2026.0+ AVX2 zorunlu!)

## 4. Windows Servisi olarak kurulum (pilot geçtiyse)
sc create OtoparkAnpr binPath= "C:\otopark\app\Anpr.Service.exe" start= delayed-auto
sc failure OtoparkAnpr reset= 86400 actions= restart/5000/restart/5000/reboot/30000
BIOS: "Restore on AC Power Loss = Power On"; bcdedit /set {default} bootstatuspolicy ignoreshutdownfailures

## 5. Raporlanacaklar
ms/kare tahmini, CPU%/RAM, AVX durumu, 1 saatlik olay sayısı + panel ekran görüntüsü,
varsa hata logları (app/service.log).
MD

( cd pilot-kit && zip -qr OtoparkAnpr-pilot-win64.zip win64 )
echo "hazır: pilot-kit/OtoparkAnpr-pilot-win64.zip ($(du -sh pilot-kit/OtoparkAnpr-pilot-win64.zip | cut -f1))"
