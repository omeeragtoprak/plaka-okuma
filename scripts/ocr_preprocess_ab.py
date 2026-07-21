"""OCR önişleme A/B — kamera GEREKTİRMEZ. Etiketli plaka kırpıntılarında cct OCR modelini
çeşitli önişlemelerle (baseline/invert/auto_invert/clahe/stretch/dual-pass) çalıştırıp
exact-match + karakter doğruluğunu karşılaştırır. Siyah/düşük-kontrast plaka araştırması için.

2026-07-21 bulgusu: önişleme siyah resmi plakayı BEYAZI BOZMADAN düzeltemiyor (ELENDİ).
Ayrıntı: docs/2026-07-21-siyah-plaka-onisleme-arastirmasi.md

Kullanım:
  python scripts/ocr_preprocess_ab.py <etiketli_dizin>
Etiket kaynağı: dosya adında son '_' sonrası plaka (…_25TGG001.jpg) ya da üst klasör adı.
Gereksinim: .venv-train (onnxruntime, cv2, numpy).
"""
import sys, os, re, glob
import numpy as np
import cv2
import onnxruntime as ort

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEL = os.path.join(ROOT, "models", "cct_xs_v2_erz.onnx")
ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_"
W, H = 128, 64
_sess = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])
_inp = _sess.get_inputs()[0].name


def label_from_path(p):
    stem = os.path.splitext(os.path.basename(p))[0].upper()
    tok = stem.split("_")[-1]
    if re.fullmatch(r"[0-9]{2}[A-Z]{1,4}[0-9]{2,5}", tok):
        return tok
    parent = os.path.basename(os.path.dirname(p)).upper()
    return parent if re.fullmatch(r"[0-9]{2}[A-Z]{1,4}[0-9]{2,5}", parent) else None


def pp_baseline(img): return img
def pp_invert(img): return 255 - img
def pp_auto_invert(img):
    g = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    return 255 - img if g.mean() < 100 else img
def pp_clahe(img):
    lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
    l, a, b = cv2.split(lab)
    l = cv2.createCLAHE(2.0, (8, 8)).apply(l)
    return cv2.cvtColor(cv2.merge((l, a, b)), cv2.COLOR_LAB2BGR)
def pp_stretch(img):
    g = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    lo, hi = np.percentile(g, 2), np.percentile(g, 98)
    if hi <= lo: return img
    return np.clip((img.astype(np.float32) - lo) * (255.0 / (hi - lo)), 0, 255).astype(np.uint8)

VARIANTS = {"baseline": pp_baseline, "invert": pp_invert, "auto_invert": pp_auto_invert,
            "clahe": pp_clahe, "stretch": pp_stretch}


def _run(img, pp):
    x = cv2.resize(pp(img), (W, H), interpolation=cv2.INTER_LINEAR)
    x = cv2.cvtColor(x, cv2.COLOR_BGR2RGB).astype(np.uint8)[None]
    p = _sess.run(None, {_inp: x})[0][0]
    idx = p.argmax(1)
    conf = float(np.mean([p[s, idx[s]] for s in range(len(idx))]))
    return "".join(ALPHABET[i] for i in idx).replace("_", ""), conf


def ocr(img, pp): return _run(img, pp)[0]


def char_acc(a, b):
    n = max(len(a), len(b))
    return 1.0 if n == 0 else sum(1 for i in range(min(len(a), len(b))) if a[i] == b[i]) / n


def main(target):
    files = [f for f in glob.glob(os.path.join(target, "**", "*.jpg"), recursive=True)
             if "_full" not in f and label_from_path(f)]
    print(f"{len(files)} etiketli kırpıntı\n{'varyant':<14}{'exact':>9}{'exact%':>8}{'kar%':>7}")
    # dual-pass (orij + ters, güveni yüksek olan)
    for vname in list(VARIANTS) + ["dual_always"]:
        ex = 0; ca = 0.0; n = 0
        for f in files:
            truth = label_from_path(f); img = cv2.imread(f)
            if img is None: continue
            n += 1
            if vname == "dual_always":
                po, co = _run(img, pp_baseline); pi, ci = _run(img, pp_invert)
                pred = pi if ci > co else po
            else:
                pred = ocr(img, VARIANTS[vname])
            if pred == truth: ex += 1
            ca += char_acc(pred, truth)
        if n:
            print(f"{vname:<14}{ex:>5}/{n:<3}{100*ex/n:>7.1f}{100*ca/n:>7.1f}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("kullanım: python scripts/ocr_preprocess_ab.py <etiketli_dizin>"); sys.exit(1)
    main(sys.argv[1])
