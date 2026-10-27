namespace Anpr.Service.Panel;

/// <summary>
/// Kamera ayarları sayfası (/kamera) — SALT-OKUNUR. Her kamera için RTSP akışından ve (kimlik bilgisi varsa) Hikvision
/// ISAPI'den okunan ayarları, bu kameranın ölçülmüş plaka boyutunu ve ANPR önerilerini gösterir. Kameraya hiçbir şey YAZMAZ;
/// sayfada ayar değiştiren bir kontrol YOKTUR (kamera ayarı kullanıcının açık onayıyla, kameranın kendi arayüzünden değişir).
/// </summary>
public static class CameraPage
{
    public const string Html = """
<!doctype html><html lang="tr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>OtoparkAnpr — Kamera ayarları</title>
<link rel="icon" href="data:,">
<style>
:root{--bg:#0d1117;--panel:#161b22;--line:#26303d;--fg:#e6edf3;--dim:#8b949e;
--green:#3fb950;--yellow:#d29922;--red:#f85149;--blue:#58a6ff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,-apple-system,Segoe UI,sans-serif}
header{padding:14px 20px;border-bottom:1px solid var(--line);display:flex;gap:16px;align-items:center;flex-wrap:wrap}
h1{font-size:16px;margin:0;font-weight:600}
h2{font-size:15px;margin:0 0 10px}
h3{font-size:13px;margin:14px 0 6px;color:var(--dim);font-weight:600;text-transform:uppercase;letter-spacing:.04em}
a{color:var(--blue)}
.wrap{max-width:1100px;margin:0 auto;padding:20px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:16px;margin-bottom:16px}
.note{color:var(--dim);font-size:13px;margin:0 0 16px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px}
table{width:100%;border-collapse:collapse;font-size:13px}
td{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top}
td:first-child{color:var(--dim);width:45%}
td.v{font-variant-numeric:tabular-nums}
.unk{color:var(--dim);font-style:italic}
.adv{border:1px solid var(--line);border-left-width:4px;border-radius:6px;padding:10px 12px;margin:8px 0}
.adv.warning{border-left-color:var(--yellow)}
.adv.info{border-left-color:var(--blue)}
.adv .t{font-weight:600;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.adv .d{margin-top:4px}
.adv .b{margin-top:6px;color:var(--dim);font-size:12px}
.chip{font:600 11px system-ui;padding:2px 7px;border-radius:10px;border:1px solid var(--line)}
.chip.m{border-color:var(--green);color:#7ee787}
.chip.u{border-color:var(--dim);color:var(--dim)}
.prob{color:var(--yellow);font-size:12px;margin:3px 0}
button{font:600 13px system-ui;padding:7px 14px;border-radius:6px;border:1px solid var(--line);background:#21262d;color:var(--fg);cursor:pointer}
button:hover,button:focus-visible{border-color:var(--blue);outline:none}
.muted{color:var(--dim)}
</style></head><body>
<header>
  <h1>Kamera ayarları <span class="muted" style="font-weight:400">· salt-okunur</span></h1>
  <span style="margin-left:auto"><a href="/panel">canlı panel</a> · <a href="/analiz">analiz</a> · <a href="/dogrulama">doğrulama</a></span>
</header>
<div class="wrap">
  <p class="note">Bu sayfa kameralara <b>hiçbir şey yazmaz</b>. Değerler RTSP akışından (her kamerada) ve kimlik bilgisi varsa
  kameranın kendi arayüzünden (Hikvision ISAPI, yalnız okuma) gelir. "—" = kamera bu bilgiyi vermedi ya da okunamadı.
  Öneriler bilgi içindir; bir ayarı değiştirmeden önce gündüz ve gece kaydıyla ölçün.</p>
  <div id="cams" class="grid"><div class="card muted">Kameralar okunuyor…</div></div>
</div>
<script>
const q = new URLSearchParams(location.search);
const tok = q.get('token');
const T = u => tok ? (u + (u.includes('?') ? '&' : '?') + 'token=' + encodeURIComponent(tok)) : u;
if (tok) document.querySelectorAll('a[href^="/"]').forEach(a => a.setAttribute('href', T(a.getAttribute('href'))));
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = (v, suffix='') => (v === null || v === undefined) ? '<span class="unk">—</span>' : esc(v) + suffix;
const onoff = v => v === null || v === undefined ? fmt(null) : (v ? 'açık' : 'kapalı');

function rows(list){ return '<table>' + list.map(([k,v]) => `<tr><td>${esc(k)}</td><td class="v">${v}</td></tr>`).join('') + '</table>'; }

function render(c){
  const s = c.stream || {}, i = c.image || {}, w = c.plateWidths;
  const stream = rows([
    ['Kodek', fmt(s.codec)],
    ['Çözünürlük', s.width ? esc(s.width + '×' + s.height) : fmt(null)],
    ['Kare hızı', fmt(s.fps, ' fps')],
    ['Bit hızı', s.bitrateKbps ? esc(s.bitrateKbps + ' kbps' + (s.bitrateMode ? ' (' + s.bitrateMode + ')' : '')) : fmt(null)],
    ['İzin verilen en yüksek bit hızı', fmt(s.maxBitrateKbps, ' kbps')],
    ['Anahtar kare aralığı', fmt(s.gopFrames, ' kare')],
    ['Akıllı kodek', onoff(s.smartCodec)],
  ]);
  const image = c.image ? rows([
    ['Pozlama', fmt(i.exposureMode)],
    ['Deklanşör', fmt(i.shutter)],
    ['Kazanç (üretici ölçeği)', fmt(i.gainLevel)],
    ['WDR / BLC / HLC', (i.wdrMode === 'auto' ? 'otomatik' : onoff(i.wdr)) + ' / ' + onoff(i.blc) + ' / ' + onoff(i.hlc)],
    ['Gürültü azaltma', fmt(i.noiseReductionMode) + (i.temporalNoiseReductionLevel != null ? ' · 3D ' + esc(i.temporalNoiseReductionLevel) : '')],
    ['Gece/gündüz', fmt(i.dayNightMode)],
    ['Odak', fmt(i.focusMode)],
    ['Ek aydınlatma', fmt(i.illumination)],
  ]) : '<p class="muted">Görüntü ayarları okunamadı (üretici arayüzü yok ya da kimlik bilgisi verilmedi).</p>';
  const plates = w ? rows([
    ['Son olay sayısı', esc(w.count)],
    ['Plaka genişliği p10 / p50 / p90', esc(w.p10 + ' / ' + w.p50 + ' / ' + w.p90 + ' px')],
    ['İşlenen kare genişliği', fmt(c.frameWidthPx, ' px')],
  ]) : '<p class="muted">Henüz bariyer olayı yok — plaka boyutu ölçülmedi.</p>';
  const advice = (c.advice || []).map(a => `<div class="adv ${esc(a.severity)}">
      <div class="t">${a.severity === 'warning' ? '⚠️' : 'ℹ️'} ${esc(a.title)}
        <span class="chip ${a.measuredHere ? 'm' : 'u'}">${a.measuredHere ? 'bu sahada ölçüldü' : 'bu sahada ölçülmedi'}</span></div>
      <div class="d">${esc(a.detail)}</div><div class="b">Dayanak: ${esc(a.basis)}</div></div>`).join('') || '<p class="muted">Öneri yok.</p>';
  const probs = (c.problems || []).map(p => `<div class="prob">• ${esc(p)}</div>`).join('');
  return `<div class="card" id="cam-${esc(c.cameraId)}">
    <h2>${esc(c.cameraName || c.cameraId)} <span class="muted" style="font-weight:400">(${esc(c.cameraId)})</span></h2>
    <div class="muted" style="font-size:12px">okundu: ${esc(new Date(c.capturedUtc).toLocaleString('tr-TR'))}
      ${c.vendorSource ? ' · kaynak: RTSP + ' + esc(c.vendorSource) : ' · kaynak: RTSP'}
      · <button type="button" data-cam="${esc(c.cameraId)}">Yeniden oku</button></div>
    ${probs}
    <h3>Akış</h3>${stream}
    <h3>Görüntü</h3>${image}
    <h3>Bu kameranın plaka boyutu</h3>${plates}
    <h3>Öneriler</h3>${advice}
  </div>`;
}

async function loadCam(id, refresh){
  const r = await fetch(T('/api/camera/settings/' + encodeURIComponent(id) + (refresh ? '?refresh=true' : '')));
  if (!r.ok) return `<div class="card"><h2>${esc(id)}</h2><p class="prob">Okunamadı (HTTP ${r.status}).</p></div>`;
  return render(await r.json());
}

async function load(){
  const box = document.getElementById('cams');
  try {
    const h = await (await fetch('/health')).json();
    const ids = (h.cameras || []).map(c => c.id);
    if (!ids.length) { box.innerHTML = '<div class="card muted">Yapılandırılmış kamera yok.</div>'; return; }
    box.innerHTML = (await Promise.all(ids.map(id => loadCam(id, false)))).join('');
  } catch (e) { box.innerHTML = '<div class="card prob">Servise ulaşılamadı.</div>'; }
}

document.addEventListener('click', async ev => {
  const b = ev.target.closest('button[data-cam]');
  if (!b) return;
  b.disabled = true; b.textContent = 'okunuyor…';
  const id = b.dataset.cam;
  const html = await loadCam(id, true);
  const old = document.getElementById('cam-' + id);
  if (old) old.outerHTML = html;
});

load();
</script>
</body></html>
""";
}
