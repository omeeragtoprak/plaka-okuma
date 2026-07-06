#!/usr/bin/env python3
"""
Harvest kırpıntılarını kameranın kendi ANPR okumasıyla (poll gerçek-değeri) otomatik etiketler.
Fine-tune veri seti üretir: out/<PLAKA>/<kırpıntı>.png + annotations.csv (image_path,plate_text).

Eşleştirme (şüpheci):
  - kırpıntı mutlak zamanı = T0(mac) + dosya-adındaki saniye
  - aday gerçek-değer olayları: |Δt| <= --window (vars. 12s), kamera saati offset ile mac'e çevrilir
  - OCR tahmini ile aday plaka edit-mesafesi <= 2 olmalı
  - birden çok FARKLI plaka eşleşirse (belirsiz) -> ATLA (yanlış etiket, hiç etiket olmamasından kötü)
"""
import argparse, os, re, shutil, csv
from datetime import datetime

def norm(p): return re.sub(r'[^A-Z0-9]', '', (p or '').upper())

def lev(a, b):
    if a == b: return 0
    if not a: return len(b)
    if not b: return len(a)
    dp = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        prev = dp[0]; dp[0] = i
        for j, cb in enumerate(b, 1):
            cur = dp[j]; dp[j] = min(dp[j] + 1, dp[j - 1] + 1, prev + (ca != cb)); prev = cur
    return dp[-1]

def parse_cam_epoch(s):
    m = re.match(r'(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})([+-]\d{2})(\d{2})', s)
    if not m: return None
    from datetime import timezone, timedelta
    g = [int(m.group(i)) for i in range(1, 7)]
    off = timezone(timedelta(hours=int(m.group(7))))
    return datetime(g[0], g[1], g[2], g[3], g[4], g[5], tzinfo=off).timestamp()

def clock_offset(clock_file):
    t = open(clock_file).read()
    cam = re.search(r'kamera=(\S+)', t); mac = re.search(r'mac=(\S+)', t)
    return (datetime.fromisoformat(cam.group(1)).timestamp() -
            datetime.fromisoformat(mac.group(1)).timestamp()) if cam and mac else 0.0

def load_truth_events(poll_files, offset):
    """(epoch_mac, plate) olay listesi (tekrarlar tekil)."""
    ev = set()
    for pf in poll_files:
        if not os.path.exists(pf): continue
        for line in open(pf):
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 2: continue
            ea, eb = parse_cam_epoch(parts[0]), parse_cam_epoch(parts[1])
            if ea is not None: ep, p = ea, parts[1]
            elif eb is not None: ep, p = eb, parts[0]
            else: continue
            pn = norm(p)
            if pn and pn != "UNKNOWN": ev.add((round(ep - offset), pn))
    return sorted(ev)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--crops-dir", required=True)
    ap.add_argument("--t0", required=True, help="yakalama başlangıcı ISO (mac saati)")
    ap.add_argument("--poll-files", required=True, help="virgüllü poll .raw listesi")
    ap.add_argument("--clock-file", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--window", type=float, default=12.0)
    ap.add_argument("--maxlev", type=int, default=2)
    a = ap.parse_args()

    t0 = datetime.fromisoformat(a.t0.strip()).timestamp()
    off = clock_offset(a.clock_file)
    events = load_truth_events(a.poll_files.split(","), off)
    os.makedirs(a.out, exist_ok=True)
    csv_path = os.path.join(a.out, "annotations.csv")
    new_file = not os.path.exists(csv_path)
    matched = skipped_amb = skipped_no = 0
    plates = set()
    with open(csv_path, "a", newline="") as fh:
        w = csv.writer(fh)
        if new_file: w.writerow(["image_path", "plate_text"])
        for f in sorted(os.listdir(a.crops_dir)):
            # sn hem "12.3" hem "12,3" olabilir (TR kültürüyle yazan bench sürümleri için tolerans)
            m = re.match(r's([\d.,]+)_([A-Z0-9]+)_c(\d+)\.png$', f)
            if not m: continue
            sec, guess = float(m.group(1).replace(",", ".")), m.group(2)
            abs_t = t0 + sec
            cands = {p for (et, p) in events if abs(et - abs_t) <= a.window and lev(guess, p) <= a.maxlev}
            if len(cands) == 1:
                plate = cands.pop()
                d = os.path.join(a.out, plate); os.makedirs(d, exist_ok=True)
                dst = os.path.join(d, f)
                shutil.copy2(os.path.join(a.crops_dir, f), dst)
                w.writerow([dst, plate]); matched += 1; plates.add(plate)
            elif len(cands) > 1: skipped_amb += 1
            else: skipped_no += 1
    print(f"etiketlendi={matched} benzersiz_plaka={len(plates)} belirsiz={skipped_amb} eşleşmedi={skipped_no}")

if __name__ == "__main__":
    main()
