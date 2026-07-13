"""Gölge-çalışma raporu: camera-records.jsonl (kameranın kendi ANPR'ı, saat-düzeltilmiş) ile
events.jsonl (bizim roi olayları) günlük eşleştirilir — cutover kriteri 'kapı trafiğinde eşleşme
≥%98' için haftalık kanıt üretir (kabul 4.12 / skor kartı işi #5).

Eşleştirme panel kuralıyla aynı: kamera-bazlı, ±45 sn pencere, önce-birebir sonra lev1;
cadde-geçişi imzalıları (iki kamerada ≤20 sn) 'kapı trafiği' dışında ayrı raporlanır.
Kullanım: python3 scripts/shadow_report.py [gün_sayısı=7]
"""
import json
import os
import sys
from collections import defaultdict
from datetime import datetime, timedelta

BASE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Anpr.Service", "bin", "Release", "net8.0")


def lev(a, b):
    if a == b: return 0
    dp = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        prev, dp[0] = dp[0], i
        for j, cb in enumerate(b, 1):
            cur = dp[j]
            dp[j] = min(dp[j] + 1, dp[j - 1] + 1, prev + (ca != cb))
            prev = cur
    return dp[-1]


def load_jsonl(path, parse):
    rows = []
    if not os.path.exists(path): return rows
    for line in open(path):
        try:
            r = parse(json.loads(line))
            if r: rows.append(r)
        except Exception:
            pass
    return rows


def key(e, n):
    return e.get(n) or e.get(n[0].upper() + n[1:])


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 else 7
    t0 = datetime.now().astimezone() - timedelta(days=days)

    cams = load_jsonl(os.path.join(BASE, "camera-records.jsonl"), lambda e: dict(
        t=datetime.fromisoformat(key(e, "timeIso")), cam=key(e, "cameraId"), p=key(e, "plate")))
    ours = load_jsonl(os.path.join(BASE, "events.jsonl"), lambda e: dict(
        t=datetime.fromisoformat(key(e, "timestamp")), cam=key(e, "cameraId"),
        p=key(e, "plateCompact")) if key(e, "scope") == "roi" else None)
    cams = [c for c in cams if c["t"] >= t0]
    ours = [o for o in ours if o["t"] >= t0]
    if not cams:
        print("camera-records.jsonl henüz boş/yetersiz — veri birikiyor (2026-07-13'te başladı).")
        return

    # cadde-geçişi imzası: aynı plaka iki kamerada ≤20 sn — kapı trafiği DEĞİL
    by_plate = defaultdict(list)
    for c in cams: by_plate[c["p"]].append(c)
    street = set()
    for p, rs in by_plate.items():
        for a in rs:
            for b in rs:
                if a["cam"] != b["cam"] and abs((a["t"] - b["t"]).total_seconds()) <= 20:
                    street.add(id(a)); street.add(id(b))

    print(f"{'gün':<11} {'kamera':>6} {'cadde':>6} {'kapı':>5} {'birebir':>8} {'1-fark':>7} {'kaçan':>6} {'eşleşme%':>9}")
    for day_off in range(days, -1, -1):
        day = (datetime.now().astimezone() - timedelta(days=day_off)).date()
        dc = [c for c in cams if c["t"].date() == day]
        do = [o for o in ours if o["t"].date() == day]
        if not dc: continue
        gate = [c for c in dc if id(c) not in street]
        used = set()
        exact = near = 0
        for c in gate:
            best, bd = None, 2
            for i, o in enumerate(do):
                if i in used or abs((o["t"] - c["t"]).total_seconds()) > 45: continue
                d = lev(c["p"], o["p"])
                if d < bd: bd, best = d, i
                if d == 0: break
            if best is not None:
                used.add(best)
                if bd == 0: exact += 1
                else: near += 1
        missed = len(gate) - exact - near
        pct = 100 * (exact + near) / len(gate) if gate else 0
        flag = " ✓" if pct >= 98 else ""
        print(f"{day} {len(dc):>6} {len(dc)-len(gate):>6} {len(gate):>5} {exact:>8} {near:>7} {missed:>6} {pct:>8.1f}{flag}")


if __name__ == "__main__":
    main()
