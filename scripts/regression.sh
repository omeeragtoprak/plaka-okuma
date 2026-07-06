#!/bin/bash
# Tek-komut regresyon bataryası: model/config değişikliğini TÜM etiketli setlerde ölçer.
# Kullanım: bash scripts/regression.sh [ocr_model.onnx]   (vars: appsettings'teki güncel varsayılan erz)
# Çıktılar: diag4 kamera-referans, live1h evaluate, val/night kırpıntı-exact (python).
set -e
cd "$(dirname "$0")/.."
OCR=${1:-cct_xs_v2_erz.onnx}
PYENV=${PYENV:-python3}   # onnxruntime'lı bir python (venv) verilebilir: PYENV=.../bin/python

echo "=============================================================="
echo "REGRESYON: $OCR   ($(date +%F' '%H:%M))"
echo "=============================================================="

echo "--- 1) Birim testler ---"
dotnet test Anpr.Tests/Anpr.Tests.csproj -c Release 2>&1 | grep -E "Başarılı!|Başarısız|Passed|Failed" | tail -1

echo "--- 2) diag4: kamera-referans (15 dk, poll gerçek-değeri; referans: erz 22/26, global 21/26) ---"
python3 scripts/camera_compare.py --root "$PWD" \
  --cameras 192.168.5.14:giris,192.168.5.15:cikis --user admin --pw "${CAM_PW:-ertansa143623}" \
  --t0-file dataset/diag4/t0_15.txt --t1-file dataset/diag4/t1_15.txt --clock-file dataset/diag4/clock_15.txt \
  --poll-dir dataset/diag4 --frames-root dataset/diag4 --fps-list 2 --fuzzy 1 --minconfirm 2 \
  --ocr "$OCR" | grep -E "TOPLAM"

echo "--- 3) live1h: 101 etiketli araç (referans: erz 87/101 & 31 yanlış-emit) ---"
dotnet run -c Release --no-build --project Anpr.Bench -- evaluate dataset/live1h dataset/live1h/labels.json \
  --ocr "$OCR" --minocr 0.45 --emitfloor 0.45 --mergeprovince false --minconfirm 2 --fuzzy 1 --label reg 2>&1 \
  | grep -iE "Plaka tam|TOPLAM emit|Karakter|gecikme"

if [ -f dataset/finetune/val.csv ] && "$PYENV" -c "import onnxruntime" 2>/dev/null; then
  echo "--- 4) kırpıntı-exact (val + gece; referans: erz %75 / %84) ---"
  for CSV in val.csv night_all.csv; do
    [ -f "dataset/finetune/$CSV" ] || continue
    "$PYENV" - "$OCR" "dataset/finetune/$CSV" <<'PY'
import sys, csv, os
import numpy as np, onnxruntime as ort
from PIL import Image
model, val_csv = f"models/{sys.argv[1]}", sys.argv[2]
sess = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
inp = sess.get_inputs()[0]
ALPHA = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_"
out = [o.name for o in sess.get_outputs() if "region" not in o.name.lower()][0]
tot = ok = 0
for r in csv.DictReader(open(val_csv)):
    p = os.path.join("dataset/finetune", r["image_path"])
    if not os.path.exists(p): continue
    im = Image.open(p).convert("RGB").resize((128, 64), Image.BILINEAR)
    y = sess.run([out], {inp.name: np.asarray(im, np.uint8)[None]})[0][0]
    text = "".join(ALPHA[i] for i in y.argmax(-1)).rstrip("_")
    tot += 1; ok += (text == r["plate_text"])
print(f"  {os.path.basename(val_csv)}: {ok}/{tot} = {100*ok/max(tot,1):.1f}%")
PY
  done
else
  echo "--- 4) atlandı (val.csv yok ya da PYENV'de onnxruntime yok) ---"
fi
echo "REGRESYON BİTTİ."
