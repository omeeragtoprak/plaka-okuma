#!/usr/bin/env python3
"""annotations.csv -> train.csv/val.csv, PLAKA-bazlı bölme (sızıntısız: bir plakanın tüm kırpıntıları
tek tarafta). Tekrarlı satırlar ve var-olmayan dosyalar ayıklanır. Deterministik (plaka adına göre)."""
import csv, os, argparse, hashlib

ap = argparse.ArgumentParser()
ap.add_argument("--annotations", required=True)
ap.add_argument("--out-dir", required=True)
ap.add_argument("--val-ratio", type=float, default=0.2)
a = ap.parse_args()

rows = {}
with open(a.annotations) as f:
    for r in csv.DictReader(f):
        p = r["image_path"]
        if os.path.exists(p):
            rows[p] = r["plate_text"]   # tekrarlar tekilleşir

by_plate = {}
for path, plate in rows.items():
    by_plate.setdefault(plate, []).append(path)

# deterministik bölme: plaka adının hash'i (yeni veri eklendiğinde eski plakalar taraf değiştirmez)
train, val = [], []
for plate, paths in sorted(by_plate.items()):
    side = val if (int(hashlib.md5(plate.encode()).hexdigest(), 16) % 100) < a.val_ratio * 100 else train
    side += [(p, plate) for p in paths]

os.makedirs(a.out_dir, exist_ok=True)
# fast-plate-ocr yolları CSV'nin klasörüne GÖRELİ çözer -> out_dir'e göreli yaz.
out_abs = os.path.abspath(a.out_dir)
for name, data in (("train.csv", train), ("val.csv", val)):
    with open(os.path.join(a.out_dir, name), "w", newline="") as f:
        w = csv.writer(f); w.writerow(["image_path", "plate_text"])
        w.writerows((os.path.relpath(os.path.abspath(p), out_abs), pl) for p, pl in data)
tp = {pl for _, pl in train}; vp = {pl for _, pl in val}
print(f"train: {len(train)} kırpıntı / {len(tp)} plaka  |  val: {len(val)} kırpıntı / {len(vp)} plaka  |  kesişim: {len(tp & vp)} (0 olmalı)")
