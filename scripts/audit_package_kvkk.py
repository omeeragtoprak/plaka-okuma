#!/usr/bin/env python3
"""YAYIN KAPISI — paketle HALKA AÇILACAK metinlerde gerçek plaka (KVKK) ve lisansı uygunsuz dosya denetimi.

NEDEN VAR (2026-09-30): 2.0.0 paketi nuget.org'a (halka açık) çıkmadan önce tarandı ve şunlar bulundu:
  · `Anpr.Sdk/README.md` (paketin README'si)        → 34 farklı GERÇEK plaka
  · `Anpr.Sdk.xml` / `Anpr.Core.xml` (XML belgeleri) → 32 + 14 farklı gerçek/gerçekten türemiş plaka
    (kaynak koddaki `///` yorumlarından — `GenerateDocumentationFile=true` onları pakete taşır)
  · `assets/models/wpod_lpd.onnx`                    → GPL-3.0 türevi ağırlık (MIT paketine konamaz)
Depo private olsa da PAKET halka açıktır; kaynak yorumuna yazılan bir plaka bir sonraki sürümde dünyaya
yayınlanır. Bu betik o yolu kapatır.

NE YAPAR:
  1) KAYNAK modu (varsayılan; CI'da da koşar): SDK/Core README + LICENSE + bildirimler + iki projenin tüm
     .cs dosyalarında plaka-biçimli HER dizgi `scripts/kvkk_fictional_plates.txt` izin listesinde olmalı.
     Yeni bir örnek plaka yazacaksanız KURGUSAL bir dizgi seçip o listeye ekleyin.
  2) PAKET modu (`--nupkg a.nupkg b.nupkg`): aynı denetim paketin İÇİNDEKİ README/XML/nuspec/DLL üzerinde;
     ayrıca yasaklı dosya (WPOD) pakette olmamalı ve LICENSE + THIRD-PARTY-NOTICES.md pakette olmalı;
     Anpr.Sdk paketinde ek olarak rektifikasyon modeli (`assets/models/iwpod_net.onnx`, MIT) olmalı.
  3) YEREL KÜLLİYAT sağlaması (yalnız olay/hüküm dosyaları bu makinedeyse): izin listesindeki hiçbir dizgi
     gerçekte gözlenmiş bir plaka OLMAMALI ve taranan metinlerde külliyattan hiçbir dizgi geçmemeli.
     (CI'da külliyat yoktur → bu adım "atlandı" diye AÇIKÇA yazılır; sessiz geçilmez.)

Çıkış: 0 temiz · 1 ihlal. Hiçbir dosyayı DEĞİŞTİRMEZ.
"""
from __future__ import annotations
import glob, json, re, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALLOW_FILE = ROOT / "scripts" / "kvkk_fictional_plates.txt"
PAT = re.compile(r"(?<![0-9A-Za-z])(0[1-9]|[1-7][0-9]|8[01]) ?([A-Z]{1,3}) ?([0-9]{2,5})(?![0-9A-Za-z])")
FORBIDDEN_FILES = ("wpod_lpd.onnx",)                 # GPL-3.0 türevi → MIT paketinde olamaz
REQUIRED_FILES = ("LICENSE", "THIRD-PARTY-NOTICES.md")
# Yalnız Anpr.Sdk paketinde olması ŞART dosyalar. 2.2.0: rektifikasyon modeli (IWPOD-NET, MIT) — 2.0.0–2.1.0'da bu
# aşama paket tüketicisinde hiç çalışmıyordu; bir paketleme hatası onu SESSİZCE yeniden kapatmasın (aşama, dosya
# yoksa hata vermeden kendini kapatır — yani eksikliği ancak bu kapı görür).
REQUIRED_SDK_FILES = ("assets/models/iwpod_net.onnx",)
DEPLOY = ROOT / "Anpr.Service" / "bin" / "Release" / "net8.0"


def tokens(text: str) -> set[str]:
    return {m.group(1) + m.group(2) + m.group(3) for m in PAT.finditer(text)}


def load_allow() -> set[str]:
    if not ALLOW_FILE.exists():
        return set()
    return {l.split("#")[0].strip() for l in ALLOW_FILE.read_text(encoding="utf-8").splitlines() if l.split("#")[0].strip()}


def source_texts() -> dict[str, str]:
    files = [ROOT / "Anpr.Sdk" / "README.md", ROOT / "Anpr.Core" / "README.md", ROOT / "LICENSE", ROOT / "THIRD-PARTY-NOTICES.md"]
    for proj in ("Anpr.Sdk", "Anpr.Core"):
        files += [p for p in (ROOT / proj).rglob("*.cs") if "/obj/" not in str(p) and "/bin/" not in str(p)]
    return {str(f.relative_to(ROOT)): f.read_text(encoding="utf-8") for f in files if f.exists()}


def package_texts(nupkg: str) -> tuple[dict[str, str], list[str]]:
    out: dict[str, str] = {}
    z = zipfile.ZipFile(nupkg)
    names = z.namelist()
    for n in names:
        low = n.lower()
        if low.endswith((".md", ".xml", ".nuspec", ".targets", ".props", ".txt")) or n in ("LICENSE",):
            out[f"{Path(nupkg).name}::{n}"] = z.read(n).decode("utf-8", errors="replace")
        elif low.endswith(".dll"):
            b = z.read(n)
            out[f"{Path(nupkg).name}::{n}"] = b.decode("utf-16-le", errors="ignore") + "\n" + b.decode("latin-1")
    return out, names


def real_corpus() -> set[str] | None:
    """Bu makinede gözlenmiş/hükümlenmiş/whitelist'teki her dizgi. Dosyalar yoksa None (CI)."""
    ev = DEPLOY / "events.jsonl"
    if not ev.exists():
        return None
    real: set[str] = set()

    def add(v):
        if isinstance(v, str):
            c = re.sub(r"[^0-9A-Z]", "", v.upper())
            if 5 <= len(c) <= 9:
                real.add(c)

    srcs = [ev, DEPLOY / "ground-truth.jsonl", DEPLOY / "camera-records.jsonl", DEPLOY / "corrections.jsonl"]
    srcs += [Path(p) for p in glob.glob(str(ROOT / "dataset" / "gt-backup" / "*.jsonl"))]
    for f in srcs:
        if not f.exists():
            continue
        for line in open(f, errors="ignore"):
            try:
                e = json.loads(line)
            except Exception:
                continue
            for k in ("PlateCompact", "SnappedFrom", "RawText", "Truth", "Ours", "Plate", "plate", "PlateNumber", "EarlyEmitHeldAs", "Text"):
                add(e.get(k))
            for c in (e.get("Candidates") or []):
                add(c.get("Text") if isinstance(c, dict) else c)
    try:
        cfg = json.load(open(ROOT / "Anpr.Service" / "appsettings.json"))
        for k in cfg.get("Anpr", cfg).get("KnownPlates", []):
            add(k)
    except Exception:
        pass
    return real


def main() -> int:
    args = sys.argv[1:]
    allow = load_allow()
    problems: list[str] = []
    texts: dict[str, str] = {}
    if "--nupkg" in args:
        pkgs = args[args.index("--nupkg") + 1:]
        if not pkgs:
            print("kullanım: audit_package_kvkk.py --nupkg <paket.nupkg> [...]"); return 1
        for pkg in pkgs:
            t, names = package_texts(pkg)
            texts.update(t)
            for bad in FORBIDDEN_FILES:
                hit = [n for n in names if n.lower().endswith(bad)]
                if hit:
                    problems.append(f"{Path(pkg).name}: YASAKLI dosya pakette → {hit[0]} (GPL-3.0 türevi; MIT paketine konamaz)")
            for req in REQUIRED_FILES:
                if req not in names:
                    problems.append(f"{Path(pkg).name}: {req} pakette YOK")
            if Path(pkg).name.lower().startswith("anpr.sdk."):
                # PackagePath="assets\models\" + Link="models\…" girdileri `assets/models//x` diye yazar (2.0.0'dan
                # beri böyle; .targets `**` ile buluyor) → karşılaştırmadan önce yinelenen '/' sadeleştirilir.
                flat = {re.sub(r"/+", "/", n) for n in names}
                for req in REQUIRED_SDK_FILES:
                    if req not in flat:
                        problems.append(f"{Path(pkg).name}: {req} pakette YOK (rektifikasyon aşaması tüketicide sessizce kapanır)")
        mode = f"PAKET ({len(pkgs)} dosya)"
    else:
        texts = source_texts()
        mode = "KAYNAK"

    found: dict[str, list[str]] = {}
    for name, t in texts.items():
        for tok in tokens(t):
            found.setdefault(tok, []).append(name)
    unknown = sorted(t for t in found if t not in allow)
    for t in unknown:
        problems.append(f"izin listesinde OLMAYAN plaka-biçimli dizgi '{t}' → {found[t][0]}"
                        + (f" (+{len(found[t]) - 1} yer)" if len(found[t]) > 1 else ""))

    corpus = real_corpus()
    if corpus is None:
        corpus_note = "yerel külliyat YOK → gerçek-plaka çapraz denetimi ATLANDI (yalnız izin listesi denetlendi)"
    else:
        bad_allow = sorted(a for a in allow if a in corpus)
        for a in bad_allow:
            problems.append(f"izin listesindeki '{a}' GERÇEKTE GÖZLENMİŞ bir dizgi — kurgusal değil, listeden çıkarıp metni değiştirin")
        # kaba kuvvet: biçim düzenli ifadesinin kaçırabileceği yazımlar için bitişik/boşluklu her aday
        brute = 0
        for name, t in texts.items():
            cands = set(re.findall(r"(?<![0-9A-Za-z])[0-9][0-9A-Z]{4,8}(?![0-9A-Za-z])", t))
            cands |= {re.sub(r"\s", "", m) for m in re.findall(r"(?<![0-9A-Za-z])[0-9]{2} [A-Z]{1,3} [0-9]{2,5}(?![0-9A-Za-z])", t)}
            for c in cands:
                if c in corpus and re.search(r"[A-Z]", c) and re.search(r"[0-9]", c) and c not in allow:
                    brute += 1
                    problems.append(f"GERÇEK külliyattan bir dizgi metinde geçiyor ('{c[:2]}…', {len(c)} karakter) → {name}")
        corpus_note = f"yerel külliyat {len(corpus)} dizgi · izin listesiyle kesişim {len(bad_allow)} · metinlerde kaba-kuvvet isabet {brute}"

    print(f"KVKK/lisans paket denetimi — mod: {mode} · taranan metin {len(texts)} · plaka-biçimli dizgi {len(found)} (izin listesi {len(allow)})")
    print(f"  {corpus_note}")
    if problems:
        print(f"🔴 {len(problems)} İHLAL:")
        for p in problems[:40]:
            print("  ✗", p)
        if len(problems) > 40:
            print(f"  … +{len(problems) - 40} daha")
        return 1
    print("✅ temiz: pakete girecek metinlerde izin listesi dışında plaka-biçimli dizgi yok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
