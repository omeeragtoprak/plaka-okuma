#!/usr/bin/env python3
"""
KnownPlates aday-listesi HARVEST — sitenin düzenli araçlarını veriden çıkarır (2026-07-23).

NEDEN: whitelist snap (SnapMaxLev=1) sistematik yanlış-okumaları sitedeki bilinen plakaya düzeltir.
Ama liste EL ile bakımı zor + filo değişir. Bu araç, BİRİKEN veriden listeyi yeniden üretir.

NEDEN AUTO-FREQUENT DEĞİL: yalnız bizim sık okumalarımızı hedef yapmak NET-NEGATİF ölçüldü (-7) —
kendi SİSTEMATİK yanlış-okumalarımızı snap hedefi yapardık. ÇÖZÜM: ÇAPRAZ-AGREEMENT — kameranın
BAĞIMSIZ ANPR kayıtları (farklı hata modu) + bizim okumalar + operatör-düzeltmeleri. Hem kamera hem
biz sık+güvenle okuyorsa → gerçek düzenli araç (bizim sistematik yanlış-okumamızı kamera onaylamaz).

ÖLÇÜLDÜ (val+night, C#-birebir snap, gate 0.99): %78.4 -> %87.7 (+9.3pp), %0.16 yanlış-snap.

KVKK: plakalar PII'dir. Bu ARAÇ commit'lenir; ÇIKTI (aday listesi) gitignored appsettings.json'a /
--out dosyasına yazılır ve ASLA repoya girmez. Ham plaka basılmaz (yalnız sayaç + preview).

Kullanım:
  python3 scripts/harvest_knownplates.py \
    --events Anpr.Service/bin/Release/net8.0/events.jsonl \
    --camera-records Anpr.Service/bin/Release/net8.0/camera-records.jsonl \
    --out /tmp/knownplates.json [--preview N]
Çıktıyı gözden geçirip appsettings.json Anpr.KnownPlates'e koyun (SnapMaxLev=1, SnapMaxOcrConfidence=0.99).
"""
import argparse, json, re, os, glob
from collections import defaultdict, Counter

def compact(s): return re.sub(r'[^A-Z0-9]', '', (s or '').upper())

def tr_valid(c):
    # il 01-81 + 1-3 harf (Q,W,X yok) + 2-5 rakam
    m = re.match(r'^(\d{2})([A-PR-VYZ]{1,3})(\d{2,5})$', c)
    return bool(m) and 1 <= int(m.group(1)) <= 81

def load_ours(path):
    """events.jsonl (çoklu-şema toleranslı) -> compact: (count, high_conf_count, mean_ocr)."""
    agg = defaultdict(lambda: [0, 0, 0.0])
    if not path or not os.path.exists(path): return agg
    for line in open(path):
        try: d = json.loads(line)
        except: continue
        pc = compact(d.get('PlateCompact') or d.get('plateCompact') or d.get('Plate') or d.get('plate') or '')
        if not tr_valid(pc): continue
        scope = d.get('Scope') or d.get('scope') or 'roi'
        if scope != 'roi': continue
        ocr = float(d.get('OcrConfidence') or d.get('ocrConfidence') or 0)
        agg[pc][0] += 1
        if ocr >= 0.95: agg[pc][1] += 1
        agg[pc][2] += ocr
    for pc in agg:
        if agg[pc][0]: agg[pc][2] /= agg[pc][0]
    return agg

def load_camera(cam_path, poll_glob):
    """camera-records.jsonl + poll_*.raw (kameranın BAĞIMSIZ okumaları) -> compact: count."""
    agg = defaultdict(int)
    if cam_path and os.path.exists(cam_path):
        for line in open(cam_path):
            try: d = json.loads(line)
            except: continue
            pc = compact(d.get('Plate') or d.get('plate') or '')
            if tr_valid(pc): agg[pc] += 1
    for f in glob.glob(poll_glob or ''):
        for line in open(f):
            for tok in line.rstrip('\n').split('\t'):
                pc = compact(tok)
                if tr_valid(pc): agg[pc] += 1
    return agg

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--events', default='Anpr.Service/bin/Release/net8.0/events.jsonl')
    ap.add_argument('--camera-records', default='Anpr.Service/bin/Release/net8.0/camera-records.jsonl')
    ap.add_argument('--poll-glob', default='dataset/diag4/poll_*.raw')
    ap.add_argument('--corrections-dir', default='dataset-corrections')
    ap.add_argument('--out', default='/tmp/knownplates-candidates.json')
    ap.add_argument('--preview', type=int, default=20)
    a = ap.parse_args()

    ours = load_ours(a.events)
    cam = load_camera(a.camera_records, a.poll_glob)
    oper = {compact(os.path.basename(d)) for d in glob.glob(f'{a.corrections_dir}/*') if tr_valid(compact(os.path.basename(d)))}

    cands = []
    for pc in set(ours) | set(cam) | oper:
        oc = ours.get(pc, [0, 0, 0]); oc_cnt, oc_hi = oc[0], oc[1]
        cc = cam.get(pc, 0); both = oc_cnt > 0 and cc > 0
        if pc in oper: tier = 'OPERATOR'                          # insan-onaylı (en güvenilir)
        elif both and (oc_hi >= 3 or cc >= 3): tier = 'AGREEMENT' # iki bağımsız sistem sık+güvenli hemfikir
        elif cc >= 5 and oc_cnt == 0: tier = 'CAMERA-ONLY'        # kameranın sık okuduğu, bizim kaçırdığımız (kronik)
        elif oc_hi >= 8 and cc >= 1: tier = 'OURS-STRONG'
        else: tier = None
        if tier: cands.append((pc, tier, oc_cnt, oc_hi, cc))

    order = {'OPERATOR': 0, 'AGREEMENT': 1, 'CAMERA-ONLY': 2, 'OURS-STRONG': 3}
    cands.sort(key=lambda x: (order[x[1]], -(x[3] + x[4])))
    plates = [c[0] for c in cands]

    print(f"KANIT DAĞILIMI: {dict(Counter(c[1] for c in cands))}")
    print(f"TOPLAM aday: {len(plates)}  (bizim distinct-valid: {len(ours)}, kamera distinct-valid: {len(cam)})")
    print(f"önizleme (ilk {a.preview}):")
    for pc, tier, oc, hi, cc in cands[:a.preview]:
        print(f"  {pc:10} {tier:12} biz={oc}(hi{hi}) kam={cc}")
    json.dump(plates, open(a.out, 'w'), ensure_ascii=False, indent=1)
    print(f"\n[{len(plates)} aday yazıldı: {a.out} — KVKK: repoya GİRMEZ. appsettings Anpr.KnownPlates'e koyun.]")
    print("[gate: SnapMaxLev=1, SnapMaxOcrConfidence=0.99 (ölçüm-optimal)]")

if __name__ == '__main__':
    main()
