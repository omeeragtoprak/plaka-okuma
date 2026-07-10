#!/usr/bin/env python3
"""
Hikvision kamera ANPR (referans) vs kendi modelimiz — SAĞLAM, TEKRARLANABİLİR karşılaştırma.

Şüpheci tasarım (önceki tek-testin zayıf noktalarını kapatır):
  * Saat kayması: kamera captureTime, ISAPI/System/time'dan ölçülüp otomatik telafi edilir (±pad).
  * Pagination: searchResultPosition ile döngü; kamera position'ı yok sayıp aynı sayfayı döndürürse
    (tekrar tespiti) durur; kısa sayfada durur; toplam çekilen + eksiklik şüphesi raporlanır.
  * Belirsizlik: tüm oranlar Wilson %95 güven aralığıyla verilir (küçük n uyarısı).
  * Kendi modelimiz: dense karelerde `Anpr.Bench emitsim` ile farklı fps'lerde offline simüle edilir
    (aynı karelerde adil A/B). Kamera = referans (kullanıcı talebi: ~%100 doğru kabul).

Kullanım:
  camera_compare.py --root <repo> --cameras 192.168.5.14:giris,192.168.5.15:cikis \
     --user admin --pw <pw> --t0-file t0.txt --t1-file t1.txt --clock-file clock.txt \
     --ocr cct_xs_v2_global.onnx --fps-list 1,2,3,5 --fuzzy 1 --minconfirm 2
"""
import argparse, re, subprocess, math, sys, os
from datetime import datetime, timezone, timedelta

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

def wilson(k, n, z=1.96):
    if n == 0: return (0.0, 0.0, 0.0)
    p = k / n; d = 1 + z*z/n
    c = (p + z*z/(2*n)) / d
    h = z*math.sqrt(p*(1-p)/n + z*z/(4*n*n)) / d
    return (p, max(0, c-h), min(1, c+h))

def curl_digest(url, user, pw, method="GET", body=None, timeout=25):
    cmd = ["curl", "-s", "--digest", "-u", f"{user}:{pw}", "--max-time", str(timeout)]
    if method == "POST":
        cmd += ["-X", "POST", "-H", "Content-Type: application/xml", "-d", body]
    cmd.append(url)
    return subprocess.run(cmd, capture_output=True, text=True).stdout

def parse_cam_epoch(s):
    m = re.match(r'(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})([+-]\d{2})(\d{2})', s)
    if not m: return None
    g = [int(m.group(i)) for i in range(1, 7)]
    off = timezone(timedelta(hours=int(m.group(7))))
    return datetime(g[0], g[1], g[2], g[3], g[4], g[5], tzinfo=off).timestamp()

def clock_offset_seconds(clock_file):
    """kamera - mac (saniye). clock.txt: 'kamera=<iso> mac=<iso>'"""
    try:
        t = open(clock_file).read()
        cam = re.search(r'kamera=(\S+)', t); mac = re.search(r'mac=(\S+)', t)
        if cam and mac:
            return datetime.fromisoformat(cam.group(1)).timestamp() - datetime.fromisoformat(mac.group(1)).timestamp()
    except FileNotFoundError:
        pass
    return 0.0

def pull_camera(ip, user, pw, t0_cam_iso, t1_cam_iso, t0_ep, t1_ep, offset, pad=90, page=20, cap=80):
    """[t0,t1] penceresindeki plakaları SAĞLAM çek. Dönüş: (distinct_norm_set, raw_count, diag)."""
    raw = []; seen = set(); pages = 0; repeated = False
    for pos in range(0, cap * page, page):
        body = (f'<VehicleDetectPlateCond version="2.0"><searchID>1</searchID>'
                f'<maxResult>{page}</maxResult><searchResultPosition>{pos}</searchResultPosition>'
                f'<timeSpanList><timeSpan><startTime>{t0_cam_iso}</startTime><endTime>{t1_cam_iso}</endTime></timeSpan></timeSpanList>'
                f'</VehicleDetectPlateCond>')
        r = curl_digest(f"http://{ip}/ISAPI/Traffic/channels/1/vehicleDetect/plates", user, pw, "POST", body)
        plates = re.findall(r'<plateNumber>([^<]*)</plateNumber>', r)
        times = re.findall(r'<captureTime>([^<]*)</captureTime>', r)
        if not plates: break
        key = tuple(times[:5])
        if key in seen:  # kamera position'ı yok sayıyor -> tekrar -> dur
            repeated = True; break
        seen.add(key); pages += 1
        raw += list(zip(plates, times))
        if len(plates) < page: break
    # zaman filtresi: captureTime (kamera saati) -> mac saatine indir (offset çıkar), ±pad
    inwin = {}
    for p, t in raw:
        ep = parse_cam_epoch(t)
        if ep is None: continue
        ep_mac = ep - offset
        if t0_ep - pad <= ep_mac <= t1_ep + pad:
            pn = norm(p)
            if pn and pn != "UNKNOWN":
                inwin.setdefault(pn, ep_mac)
    diag = (f"ham={len(raw)} sayfa={pages} tekrar_tespit={repeated} pencere_içi_distinct={len(inwin)} "
            f"(pad={pad}s, saat_offset={offset:+.0f}s)")
    return set(inwin.keys()), len(raw), diag

def load_poll_truth(poll_file, t0_ep, t1_ep, offset, pad=90):
    """Poll ile biriktirilen ham kayıtlardan (plate<TAB>captureTime) pencere-içi distinct truth kümesi.
    Kamera son-20'yi döndürdüğü için poll = TAM gerçek-değer (araç 2/dk, 20s'de kaçmaz)."""
    inwin = {}
    try:
        for line in open(poll_file):
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 2: continue
            # sütun sırası belirsiz (captureTime<TAB>plate ya da tersi): hangisi zaman parse edilirse o.
            a_, b_ = parts[0], parts[1]
            ea, eb = parse_cam_epoch(a_), parse_cam_epoch(b_)
            if ea is not None: ep, p = ea, b_
            elif eb is not None: ep, p = eb, a_
            else: continue
            ep_mac = ep - offset
            if t0_ep - pad <= ep_mac <= t1_ep + pad:
                pn = norm(p)
                if pn and pn != "UNKNOWN": inwin.setdefault(pn, ep_mac)
    except FileNotFoundError:
        pass
    return set(inwin.keys())

def run_emitsim(root, cam_dir, ocr, fps, fuzzy, minconfirm, det=None, minwidth=0.0, dedup=8.0, earlyemit=0.0, roi="", spatialiou=0.0, skipchar=0.0):
    cmd = ["dotnet", "run", "-c", "Release", "--no-build", "--project", "Anpr.Bench", "--",
           "emitsim", cam_dir, "--ocr", ocr, "--fps", str(fps), "--minconfirm", str(minconfirm),
           "--minocr", "0.45", "--emitfloor", "0.45", "--fuzzy", str(fuzzy)]
    if det: cmd += ["--det", det]
    if minwidth: cmd += ["--minwidth", str(minwidth)]
    if dedup != 8.0: cmd += ["--dedup", str(dedup)]
    if earlyemit: cmd += ["--earlyemit", str(earlyemit)]
    if spatialiou: cmd += ["--spatialiou", str(spatialiou)]
    if skipchar: cmd += ["--skipchar", str(skipchar)]
    if roi: cmd += ["--roi", roi]
    out = subprocess.run(cmd, cwd=root, capture_output=True, text=True).stdout
    m = re.search(r'DISTINCT plaka=\d+\s*\n\s*(.+)', out)
    plates = set()
    if m:
        for tok in m.group(1).split('·'):
            pn = norm(tok)
            if pn: plates.add(pn)
    return plates

def compare(ours, truth, dist=1):
    """truth = kamera (referans). Donus: exact, near, missed (kamerada var bizde yok), extra (bizde var kamerada yok)."""
    exact = ours & truth
    o_rest = ours - truth; t_rest = truth - ours
    near = []; om = set(); tm = set()
    for tp in sorted(t_rest):
        for op in sorted(o_rest):
            if op in om: continue
            if lev(op, tp) <= dist: near.append((tp, op)); om.add(op); tm.add(tp); break
    missed = [t for t in t_rest if t not in tm]   # kameranın görüp bizim kaçırdığımız
    extra = [o for o in o_rest if o not in om]     # bizim görüp kamerada olmayan (muhtemel gürültü)
    return exact, near, missed, extra

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--cameras", required=True, help="ip:label,ip:label")
    ap.add_argument("--user", default="admin"); ap.add_argument("--pw", required=True)
    ap.add_argument("--t0-file", required=True); ap.add_argument("--t1-file", required=True)
    ap.add_argument("--clock-file", default=None)
    ap.add_argument("--ocr", default="cct_xs_v2_global.onnx")
    ap.add_argument("--det", default=None, help="dedektör onnx (varsayılan yolo-v9-t-384)")
    ap.add_argument("--minwidth", type=float, default=0.0, help="tetik bölgesi MinPlateWidthRatio")
    ap.add_argument("--dedup", type=float, default=8.0, help="DedupWindow saniye")
    ap.add_argument("--earlyemit", type=float, default=0.0, help="EarlyEmitMinOcr eşiği")
    ap.add_argument("--spatialiou", type=float, default=0.0, help="uzamsal iz birleştirme IoU (0=kapalı)")
    ap.add_argument("--skipchar", type=float, default=0.0, help="SKIP_CHAR seviyesi (0=kapalı)")
    ap.add_argument("--roi", default="", help="ROI çokgeni (tek kamera koşusunda)")
    ap.add_argument("--fps-list", default="2")
    ap.add_argument("--fuzzy", type=int, default=1); ap.add_argument("--minconfirm", type=int, default=2)
    ap.add_argument("--poll-dir", default=None, help="verilirse truth poll_<label>.raw'dan yüklenir (TAM gerçek-değer)")
    ap.add_argument("--frames-root", default="dataset/diag3", help="emitsim kare kökü (örn dataset/diag4)")
    a = ap.parse_args()

    t0_mac = open(a.t0_file).read().strip(); t1_mac = open(a.t1_file).read().strip()
    t0_ep = datetime.fromisoformat(t0_mac).timestamp(); t1_ep = datetime.fromisoformat(t1_mac).timestamp()
    offset = clock_offset_seconds(a.clock_file) if a.clock_file else 0.0
    # kameraya sorarken kamera-saatli pencere ver (offset ekle) — yine de client filtreliyoruz
    t0_cam = (datetime.fromisoformat(t0_mac) + timedelta(seconds=offset)).strftime("%Y-%m-%dT%H:%M:%S+03:00")
    t1_cam = (datetime.fromisoformat(t1_mac) + timedelta(seconds=offset)).strftime("%Y-%m-%dT%H:%M:%S+03:00")
    cams = [c.split(":") for c in a.cameras.split(",")]
    fps_list = [int(x) for x in a.fps_list.split(",")]

    print(f"# Pencere (mac): {t0_mac} .. {t1_mac}  | saat_offset(kamera-mac)={offset:+.0f}s")
    truth = {}
    for ip, label in cams:
        if a.poll_dir:
            tset = load_poll_truth(os.path.join(a.poll_dir, f"poll_{label}.raw"), t0_ep, t1_ep, offset)
            print(f"# KAMERA[{label}] (POLL, tam): pencere_içi_distinct={len(tset)}")
        else:
            tset, rawn, diag = pull_camera(ip, a.user, a.pw, t0_cam, t1_cam, t0_ep, t1_ep, offset)
            print(f"# KAMERA[{label}] {ip}: {diag}")
        truth[label] = tset

    for fps in fps_list:
        print(f"\n===== bizim model @ fps={fps} (fuzzy={a.fuzzy}, minConfirm={a.minconfirm}) =====")
        tot_exact = tot_near = tot_missed = tot_extra = tot_truth = 0
        for ip, label in cams:
            cam_dir = f"{a.frames_root}/{label}"
            ours = run_emitsim(a.root, cam_dir, a.ocr, fps, a.fuzzy, a.minconfirm, a.det, a.minwidth, a.dedup, a.earlyemit, a.roi, a.spatialiou, a.skipchar)
            ex, near, missed, extra = compare(ours, truth[label])
            T = len(truth[label])
            det = len(ex) + len(near)   # yakalanan (exact + 1-karakter)
            print(f"  [{label}] kamera={T}  bizim_exact={len(ex)}  1-fark={len(near)}  KAÇAN={len(missed)}  fazla(gürültü?)={len(extra)}")
            if missed: print(f"      kaçan: {', '.join(sorted(missed))}")
            if extra:  print(f"      fazla: {', '.join(sorted(extra))}")
            tot_exact += len(ex); tot_near += len(near); tot_missed += len(missed); tot_extra += len(extra); tot_truth += T
        det = tot_exact + tot_near
        pr, lo, hi = wilson(tot_exact, tot_truth)
        rc, rlo, rhi = wilson(det, tot_truth)
        print(f"  --- TOPLAM fps={fps}: kamera={tot_truth}  birebir={tot_exact} (%{100*pr:.0f}, GA %{100*lo:.0f}-{100*hi:.0f})  "
              f"yakalama(exact+near)={det} (%{100*rc:.0f}, GA %{100*rlo:.0f}-{100*rhi:.0f})  kaçan={tot_missed}  gürültü={tot_extra}")
    print(f"\n# n={tot_truth} araç. Küçük n'de GA geniştir — oranları GÖSTERGE oku.")

if __name__ == "__main__":
    main()
