"""Kalite kapısı eşik taraması (Faz 1.3): C# qscore çıktısı (bulanıklık/pozlama) + OCR
doğruluğu (cct_xs_v2_erz, python) birleştirilir; aday eşikler için "atılan DOĞRU okuma %"
vs "atılan YANLIŞ okuma %" tablosu üretilir.
Seçim kuralı (plan): doğru okunanların <%2'sini atan EN AGRESİF eşik.

Kullanım: .venv-train/bin/python scripts/quality_sweep.py
"""
import csv
import os

import numpy as np
import onnxruntime as ort
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALPHA = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_"
MODEL = os.path.join(ROOT, "models", "cct_xs_v2_erz.onnx")

sess = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])


def read_plate(path):
    img = Image.open(path).convert("RGB").resize((128, 64))
    x = np.asarray(img, dtype=np.uint8)[None]
    out = sess.run(None, {"input": x})[0][0].reshape(-1, len(ALPHA))
    return "".join(ALPHA[i] for i in out.argmax(1)).replace("_", "")


def truth_map(csv_path):
    base = os.path.dirname(os.path.abspath(csv_path))
    m = {}
    for r in csv.DictReader(open(csv_path)):
        p = r["image_path"]
        if not os.path.isabs(p):
            p = os.path.normpath(os.path.join(base, p))
        t = (r["plate_text"] or "").strip()
        if t:
            m[p] = t
    return m


def load(qscore_csv, truth):
    rows = []
    for r in csv.DictReader(open(qscore_csv)):
        p = r["image_path"]
        if p not in truth:
            continue
        got = read_plate(p)
        rows.append(dict(path=p, blur=float(r["blur"]), dark=float(r["dark_pct"]),
                         bright=float(r["bright_pct"]), ok=(got == truth[p])))
    return rows


def sweep(rows, label):
    ok = [r for r in rows if r["ok"]]
    bad = [r for r in rows if not r["ok"]]
    print(f"\n== {label}: n={len(rows)} doğru={len(ok)} yanlış={len(bad)} ==")
    print(f"{'blurMin':>8} {'atılanDoğru%':>13} {'atılanYanlış%':>14}")
    best = None
    for th in (10, 20, 30, 40, 60, 80, 100, 150, 200, 300):
        do = 100 * sum(1 for r in ok if r["blur"] < th) / max(1, len(ok))
        dy = 100 * sum(1 for r in bad if r["blur"] < th) / max(1, len(bad))
        mark = ""
        if do < 2.0:
            best = (th, do, dy)
            mark = " <-"
        print(f"{th:>8} {do:>12.1f} {dy:>13.1f}{mark}")
    if best:
        print(f"ÖNERİ blurMin={best[0]} (doğru kaybı %{best[1]:.1f}, yanlış eleme %{best[2]:.1f})")
    # pozlama uçları bilgi amaçlı
    for k in ("dark", "bright"):
        hi_ok = sorted(r[k] for r in ok)[-1] if ok else 0
        print(f"{k}: doğru okunanlarda MAKS {hi_ok:.2f} (eşik bunun üstüne konulmalı)")
    return best


val = load(os.path.join(ROOT, "dataset/slices/qscore_val.csv"),
           truth_map(os.path.join(ROOT, "dataset/finetune/val.csv")))
gece = load(os.path.join(ROOT, "dataset/slices/qscore_gece.csv"),
            truth_map(os.path.join(ROOT, "dataset/finetune/night_all.csv")))

sweep(val, "VAL (gündüz ağırlıklı)")
sweep(gece, "GECE")
sweep(val + gece, "BİRLEŞİK")
