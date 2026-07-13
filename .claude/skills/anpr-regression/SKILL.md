---
name: anpr-regression
description: ANPR regresyon bataryasını (diag4 kamera-referans + val/gece kırpıntı-exact + birim testler) verilen OCR modeliyle koşar ve referans metriklerle karşılaştırır. Model/config/kod değişikliği sonrası doğrulama için.
argument-hint: [ocr_model.onnx]
disable-model-invocation: true
allowed-tools: Bash(bash scripts/regression.sh *), Bash(dotnet test *), Bash(python3 scripts/camera_compare.py *), Read
---

## Referans metrikler (son koşumlar)
!`tail -20 docs/bench-history.md 2>/dev/null || echo "geçmiş yok — ilk koşumdan sonra docs/bench-history.md oluşturulacak"`

## Görev
1. Kamera şifresi `CAM_PW` ortam değişkeninde olmalı (yoksa kullanıcıdan iste; ASLA dosyaya yazma).
2. `CAM_PW=... bash scripts/regression.sh ${0:-cct_xs_v2_erz.onnx}` çalıştır.
3. Referanslarla kıyasla (güncel taban): **diag4 birebir 22/26 + gürültü ≤9**, val kırpıntı-exact
   **%76.4**, gece **%82.4**, birim testler **68/68**. Bayraklar: `--spatialiou 0.35 --skipchar 0.3
   --qblur 600 --qdark 0.85 --qbright 0.80` (bataryada gömülü değilse elle ver).
4. HERHANGİ bir dilim gerilediyse nedenini araştırmadan "başarılı" deme (dilim-bazlı gerileme kuralı).
5. Sonucu `docs/bench-history.md`'ye tek satır ekle: `| tarih | model | diag4 | gürültü | val | gece | not |`.
