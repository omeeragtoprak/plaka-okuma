"""İkinci-Şans Şeridi offline A/B (Faz 2.6a): WPOD rektifikasyonunun dilim-bazlı etkisi.
Aynı kırpıntı HAM ve REKTİFİYE olarak cct_xs_v2_erz'e verilir; dilim başına exact karşılaştırılır.
Rektifikasyon C# portundan ÖNCE burada kazanmalı (ölçmeden yatırım yok — deskew/füzyon dersi).

Kullanım: .venv-train/bin/python scripts/enhance_ab.py [--pad 0.5] [--thr 0.35]
"""
import argparse
import csv
import math
import os

import cv2
import numpy as np
import onnxruntime as ort

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALPHA = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_"
NET_STRIDE = 16
SIDE = 7.75

ocr = ort.InferenceSession(os.path.join(ROOT, "models/cct_xs_v2_erz.onnx"),
                           providers=["CPUExecutionProvider"])
wpod = ort.InferenceSession(os.path.join(ROOT, "models/wpod_lpd.onnx"),
                            providers=["CPUExecutionProvider"])


def read_probs(bgr):
    img = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    img = cv2.resize(img, (128, 64))
    return ocr.run(None, {"input": img[None].astype(np.uint8)})[0][0].reshape(-1, len(ALPHA))


def decode(probs):
    text = "".join(ALPHA[i] for i in probs.argmax(1)).replace("_", "")
    conf = float(probs.max(1).mean())
    return text, conf


def read_plate(bgr):
    return decode(read_probs(bgr))


def wpod_quad(bgr, bound=208, threshold=0.35):
    h0, w0 = bgr.shape[:2]
    factor = float(bound) / min(h0, w0)
    w = int(w0 * factor); h = int(h0 * factor)
    w += (w % NET_STRIDE != 0) * (NET_STRIDE - w % NET_STRIDE)
    h += (h % NET_STRIDE != 0) * (NET_STRIDE - h % NET_STRIDE)
    img = cv2.resize(bgr, (w, h)).astype(np.float32) / 255.0
    y = np.squeeze(wpod.run(None, {wpod.get_inputs()[0].name: img[None]})[0])
    probs = y[..., 0]; affines = y[..., 2:]
    yy, xx = np.where(probs > threshold)
    if len(yy) == 0:
        return None
    mn = np.array([w, h], dtype=float) / NET_STRIDE
    base = np.array([[-0.5, 0.5, 0.5, -0.5], [-0.5, -0.5, 0.5, 0.5], [1, 1, 1, 1.0]])
    bi = int(np.argmax(probs[yy, xx]))
    cy, cx = yy[bi], xx[bi]
    a = affines[cy, cx].reshape(2, 3)
    a[0, 0] = max(a[0, 0], 0.0); a[1, 1] = max(a[1, 1], 0.0)
    pts = (a @ base) * SIDE + np.array([[cx + 0.5], [cy + 0.5]])
    pts = pts / mn.reshape(2, 1) * np.array([[w0], [h0]], dtype=float)
    return pts  # 2x4 TL,TR,BR,BL


def rectify(bgr, pad_ratio):
    """ort-alpr reçetesi: marjlı besle -> quad -> maks kenar 240'a ölçekli hedef + %10 kenar payı -> warp."""
    h0, w0 = bgr.shape[:2]
    py, px = int(h0 * pad_ratio), int(w0 * pad_ratio)
    padded = cv2.copyMakeBorder(bgr, py, py, px, px, cv2.BORDER_REPLICATE)
    quad = wpod_quad(padded)
    if quad is None:
        return None
    x, y = quad
    def e(i, j): return math.hypot(x[j] - x[i], y[j] - y[i])
    W = max(e(0, 1), e(3, 2)); H = max(e(0, 3), e(1, 2))
    if W < 8 or H < 4:
        return None
    scale = 240.0 / max(W, H)
    W, H = int(W * scale), int(H * scale)
    mx, my = W // 10, H // 10
    dst = np.float32([[mx, my], [mx + W, my], [mx + W, my + H], [mx, my + H]])
    src = np.float32(quad.T)
    Hm = cv2.getPerspectiveTransform(src, dst)
    return cv2.warpPerspective(padded, Hm, (W + 2 * mx, H + 2 * my))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pad", type=float, default=0.5)
    ap.add_argument("--thr", type=float, default=0.35)
    ap.add_argument("--gated", action="store_true",
                    help="tasarım proxy'si: yalnız ham güven<0.85 iken dene; güveni yüksek olan kazanır")
    ap.add_argument("--vote", type=float, default=0,
                    help="ÜRETİM proxy'si: ham güven<EŞİK ise rektifiye et ve iki dağılımı TOPLA (oylama)")
    a = ap.parse_args()

    rows = list(csv.DictReader(open(os.path.join(ROOT, "dataset/slices/slices.csv"))))
    slices = {"acili": [], "temiz": [], "karanlik": [], "kucuk": []}
    for r in rows:
        for k in slices:
            if r[k] == "1":
                slices[k].append(r)

    print(f"pad={a.pad} thr={a.thr}")
    print(f"{'dilim':>9} {'n':>4} {'quad%':>6} {'HAM':>10} {'REKTİFİYE':>10} {'yalnız-rekt-kazanç':>18}")
    for name, rs in slices.items():
        n = len(rs)
        raw_ok = rect_ok = found = both = gain = loss = 0
        for r in rs:
            bgr = cv2.imread(r["image_path"])
            if bgr is None:
                continue
            truth = r["plate_text"]
            praw = read_probs(bgr)
            t_raw, c_raw = decode(praw)
            ok_raw = (t_raw == truth)
            if a.vote > 0:
                if c_raw >= a.vote:
                    ok_rec = ok_raw
                else:
                    rimg = rectify(bgr, a.pad)
                    if rimg is not None:
                        found += 1
                        merged = praw + read_probs(rimg)   # oylama: dağılım toplamı
                        t_m, _ = decode(merged)
                        ok_rec = (t_m == truth)
                    else:
                        ok_rec = ok_raw
                raw_ok += ok_raw; rect_ok += ok_rec
                if ok_rec and not ok_raw: gain += 1
                if ok_raw and not ok_rec: loss += 1
                continue
            if a.gated and c_raw >= 0.85:
                ok_rec = ok_raw  # kapı: yüksek güvenli ize dokunulmaz
            else:
                rimg = rectify(bgr, a.pad)
                if rimg is not None:
                    found += 1
                    t_rec, c_rec = read_plate(rimg)
                    if a.gated:
                        # oylama yaklaşıklığı: güveni yüksek olan kazanır
                        ok_rec = (t_rec == truth) if c_rec > c_raw else ok_raw
                    else:
                        ok_rec = (t_rec == truth)
                else:
                    ok_rec = ok_raw  # bulunamadıysa ham sonuç geçerli (üretimde aşama atlanır)
            raw_ok += ok_raw
            rect_ok += ok_rec
            if ok_rec and not ok_raw: gain += 1
            if ok_raw and not ok_rec: loss += 1
        print(f"{name:>9} {n:>4} {100*found/max(1,n):>5.0f}% {raw_ok:>6}/{n:<3} {rect_ok:>6}/{n:<3}   +{gain} / -{loss}")


if __name__ == "__main__":
    main()
