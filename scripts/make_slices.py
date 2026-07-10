"""Kırpıntı dilimleme: etiketli setlerdeki (val.csv, night_all.csv) her kırpıntı için
blur (gri Laplacian varyansı), pozlama (ortalama gri + karanlık/patlamış piksel yüzdesi),
genişlik ve WPOD-quad eğiklik/perspektif metriği hesaplar -> dataset/slices/slices.csv.

Dilim etiketleri (kolonlar): acili (skew üst %25), karanlik (meanGray alt bandı),
kucuk (genişlik<100 px), temiz (hiçbiri). Amaç: zenginleştirme aşamalarının HEDEF-DİLİM
A/B'leri (spec: 2026-07-10-kirpinti-goruntu-isleme-design.md, Faz 0.1).

Kullanım: .venv-train/bin/python scripts/make_slices.py
"""
import csv
import math
import os
import sys

import cv2
import numpy as np
import onnxruntime as ort

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WPOD = os.path.join(ROOT, "models", "wpod_lpd.onnx")
OUT_DIR = os.path.join(ROOT, "dataset", "slices")
SETS = [("val", "dataset/finetune/val.csv"), ("gece", "dataset/finetune/night_all.csv")]

NET_STRIDE = 16
SIDE = ((208.0 + 40.0) / 2.0) / NET_STRIDE  # 7.75 (WPOD kanonik yarı-kenar ölçeği)


def wpod_quad(sess, bgr, bound=208, threshold=0.35):
    """WPOD'dan en olası plaka dörtgeni (2x4: satır x,y; TL,TR,BR,BL) + olasılık. Yoksa None."""
    h0, w0 = bgr.shape[:2]
    factor = float(bound) / min(h0, w0)
    w = int(w0 * factor)
    h = int(h0 * factor)
    w += (w % NET_STRIDE != 0) * (NET_STRIDE - w % NET_STRIDE)
    h += (h % NET_STRIDE != 0) * (NET_STRIDE - h % NET_STRIDE)
    img = cv2.resize(bgr, (w, h)).astype(np.float32) / 255.0
    y = sess.run(None, {sess.get_inputs()[0].name: img[None]})[0]
    y = np.squeeze(y)

    probs = y[..., 0]
    affines = y[..., 2:]
    yy, xx = np.where(probs > threshold)
    if len(yy) == 0:
        return None, 0.0

    mn = np.array([w, h], dtype=float) / NET_STRIDE  # özellik haritası boyutu (x,y)
    base = np.array([[-0.5, 0.5, 0.5, -0.5],
                     [-0.5, -0.5, 0.5, 0.5],
                     [1.0, 1.0, 1.0, 1.0]])

    best_i = int(np.argmax(probs[yy, xx]))
    cy, cx = yy[best_i], xx[best_i]
    a = affines[cy, cx].reshape(2, 3)
    a[0, 0] = max(a[0, 0], 0.0)
    a[1, 1] = max(a[1, 1], 0.0)
    pts = a @ base                      # 2x4, kanonik kare
    pts = pts * SIDE + np.array([[cx + 0.5], [cy + 0.5]])
    pts = pts / mn.reshape(2, 1)        # 0..1 normalize
    pts = pts * np.array([[w0], [h0]], dtype=float)  # orijinal piksel
    return pts, float(probs[cy, cx])


def skew_metrics(pts):
    """Üst/alt kenar açısı (derece, mutlak ort.) + kenar-uzunluk oranı (perspektif şiddeti)."""
    (x, y) = pts
    def ang(i, j):
        return math.degrees(math.atan2(y[j] - y[i], x[j] - x[i]))
    def edge(i, j):
        return math.hypot(x[j] - x[i], y[j] - y[i])
    top, bottom = ang(0, 1), ang(3, 2)
    skew = (abs(top) + abs(bottom)) / 2.0
    lr = edge(0, 3), edge(1, 2)
    persp = max(lr) / max(1e-6, min(lr))
    return skew, persp


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    sess = ort.InferenceSession(WPOD, providers=["CPUExecutionProvider"])

    rows = []
    for set_name, rel in SETS:
        csv_path = os.path.join(ROOT, rel)
        base_dir = os.path.dirname(csv_path)
        for r in csv.DictReader(open(csv_path)):
            p = r["image_path"]
            if not os.path.isabs(p):
                p = os.path.normpath(os.path.join(base_dir, p))
            truth = (r["plate_text"] or "").strip()
            if not truth:
                continue  # negatifler dilim analizinin dışında
            bgr = cv2.imread(p)
            if bgr is None:
                continue
            gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
            blur = float(cv2.Laplacian(gray, cv2.CV_64F).var())
            mean_gray = float(gray.mean())
            dark_pct = float((gray < 30).mean())
            bright_pct = float((gray > 225).mean())
            quad, prob = wpod_quad(sess, bgr)
            if quad is not None:
                skew, persp = skew_metrics(quad)
            else:
                skew, persp = float("nan"), float("nan")
            rows.append(dict(set=set_name, image_path=p, plate_text=truth,
                             width=bgr.shape[1], blur=round(blur, 1),
                             mean_gray=round(mean_gray, 1),
                             dark_pct=round(dark_pct, 3), bright_pct=round(bright_pct, 3),
                             wpod_prob=round(prob, 3),
                             skew=round(skew, 2) if skew == skew else "",
                             persp=round(persp, 3) if persp == persp else ""))

    skews = sorted(float(r["skew"]) for r in rows if r["skew"] != "")
    skew_p75 = skews[int(len(skews) * 0.75)] if skews else 9e9
    grays = sorted(r["mean_gray"] for r in rows)
    gray_p20 = grays[int(len(grays) * 0.20)]

    for r in rows:
        r["acili"] = int(r["skew"] != "" and float(r["skew"]) >= skew_p75)
        r["karanlik"] = int(r["mean_gray"] <= gray_p20)
        r["kucuk"] = int(r["width"] < 100)
        r["temiz"] = int(not (r["acili"] or r["karanlik"] or r["kucuk"]))

    out = os.path.join(OUT_DIR, "slices.csv")
    with open(out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)

    n = len(rows)
    found = sum(1 for r in rows if r["skew"] != "")
    print(f"toplam={n}  wpod-quad bulundu={found} (%{100*found/n:.0f})  skew-p75={skew_p75:.1f}°  gray-p20={gray_p20:.0f}")
    for d in ("acili", "karanlik", "kucuk", "temiz"):
        print(f"  {d}: {sum(r[d] for r in rows)}")
    print("->", out)


if __name__ == "__main__":
    main()
