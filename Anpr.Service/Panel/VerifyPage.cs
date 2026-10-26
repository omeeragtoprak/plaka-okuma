namespace Anpr.Service.Panel;

/// <summary>
/// Gözle doğrulama sayfası (/dogrulama) — KAMERA-BAĞIMSIZ doğruluk ölçümünün insan arayüzü.
/// Kanıt görselini gösterir, okuduğumuz plakayı sorar, verdict'i kaydeder ve tabakalı raporu üretir.
/// </summary>
public static class VerifyPage
{
    public const string Html = """
<!doctype html><html lang="tr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>OtoparkAnpr — Doğrulama</title>
<style>
:root{--bg:#0d1117;--panel:#161b22;--line:#26303d;--fg:#e6edf3;--dim:#8b949e;
--green:#3fb950;--yellow:#d29922;--red:#f85149;--blue:#58a6ff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,-apple-system,Segoe UI,sans-serif}
header{padding:14px 20px;border-bottom:1px solid var(--line);display:flex;gap:16px;align-items:center;flex-wrap:wrap}
h1{font-size:16px;margin:0;font-weight:600}
a{color:var(--blue)}
.wrap{max-width:1100px;margin:0 auto;padding:20px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:16px;margin-bottom:16px}
.imgbox{text-align:center;background:#000;border-radius:6px;padding:8px;margin:12px 0}
.imgbox img{max-width:100%;height:auto;border-radius:4px}
.plate{font:700 34px/1.2 ui-monospace,SFMono-Regular,Menlo,monospace;letter-spacing:2px;text-align:center;margin:10px 0}
.meta{display:flex;gap:18px;flex-wrap:wrap;color:var(--dim);font-size:13px;justify-content:center}
.btns{display:flex;gap:10px;margin-top:14px;flex-wrap:wrap;justify-content:center}
button{font:600 14px system-ui;padding:11px 20px;border-radius:6px;border:1px solid var(--line);
background:#21262d;color:var(--fg);cursor:pointer}
button:hover{border-color:var(--blue)}
button.ok{background:#1b3a24;border-color:var(--green);color:#7ee787}
button.no{background:#3d1a1a;border-color:var(--red);color:#ffa198}
button.skip{background:#2d2a15;border-color:var(--yellow);color:#e3b341}
input[type=text]{font:600 18px ui-monospace,monospace;padding:9px 12px;border-radius:6px;
border:1px solid var(--line);background:#0d1117;color:var(--fg);text-transform:uppercase;width:220px}
table{width:100%;border-collapse:collapse;font-size:13px}
th,td{padding:7px 9px;border-bottom:1px solid var(--line);text-align:right}
th:first-child,td:first-child{text-align:left}
th{color:var(--dim);font-weight:600}
.big{font:700 26px ui-monospace,monospace}
.warn{background:#2d2a15;border:1px solid #4d4520;border-radius:6px;padding:10px 12px;margin:8px 0;
color:#e3b341;font-size:13px}
.dim{color:var(--dim)}
.hint{color:var(--dim);font-size:12px;text-align:center;margin-top:8px}
</style></head><body>
<header>
  <h1>Gözle Doğrulama</h1>
  <span class="dim">kamera referansı KULLANILMAZ — gerçek doğruluk ölçümü</span>
  <span style="margin-left:auto"><a href="/panel">canlı panel</a> · <a href="/analiz">analiz</a></span>
</header>
<div class="wrap">

<div class="card" id="task">
  <div class="dim" id="progress">yükleniyor…</div>
  <div class="imgbox"><img id="ev" alt="kanıt görseli"></div>
  <div class="plate" id="ours">—</div>
  <div class="meta">
    <span>ocr: <b id="ocr">—</b></span>
    <span>bant: <b id="band">—</b></span>
    <span>kamera: <b id="cam">—</b></span>
    <span>zaman: <b id="at">—</b></span>
    <span id="lowc"></span>
  </div>
  <div class="hint">Görseldeki plaka yukarıdakiyle AYNI mı? Emin değilseniz <b>iyileştir</b>'i deneyin
  (tam kareden geniş kadraj + kontrast germe), tam kareyi açın ve <b>aşağıdaki çapraz kanıda mutlaka bakın</b>.
  Okunamıyorsa TAHMİN ETMEYİN — "görselden okunamıyor" seçin; ölçümün dürüstlüğü buna bağlı.</div>
  <div class="btns">
    <button class="ok"   onclick="send('Dogru')">✓ Doğru</button>
    <button class="no"   onclick="wrong()">✗ Yanlış</button>
    <button class="skip" onclick="send('YanlisBelirsiz')" title="Okuma yanlış ama gerçek plaka kesin belirlenemiyor — HATA sayılır, etiketli sete plakayla girmez">✗? Yanlış ama doğrusu okunamıyor</button>
    <button class="skip" onclick="send('Okunamadi')" title="Plaka hiç okunamıyor — paydadan çıkar">? Görselden hiç okunamıyor</button>
    <button onclick="enhance()" id="enh">🔍 iyileştir</button>
    <a id="full" href="#" target="_blank"><button>tam kareyi aç</button></a>
  </div>
  <div id="ctxwrap" style="display:none;margin-top:14px;border-top:1px solid var(--line);padding-top:12px">
    <div class="dim" style="font-size:12px;margin-bottom:6px">
      Bu plakanın diğer okumaları — <b>en net kanıt başta</b>. <b>"Doğru" ya da "Yanlış" demeden ÖNCE
      buraya bakın</b> — bulanık tek kırpıntı İKİ YÖNDE de yanıltır: ölçümde hem yanlış "yanlış"
      (3 kez) hem yanlış "doğru" (1 kez: 25AFL548 → "25ATE548" onaylanmıştı) üretti. Yanlış ONAY
      daha tehlikelidir: ölçülen doğruluğu şişirir ve fark edilmez.
    </div>
    <div id="ctx" style="display:flex;gap:8px;overflow-x:auto;padding-bottom:6px"></div>
    <div id="ctxnote" class="dim" style="font-size:11px;margin-top:4px"></div>
  </div>
  <div class="btns" id="truthbox" style="display:none">
    <input type="text" id="truth" placeholder="25 ABC 123" autocomplete="off">
    <button class="no" onclick="sendWrong()">gerçek plaka bu → kaydet</button>
  </div>
</div>

<div class="card">
  <h3 style="margin:0 0 10px">Ölçülen gerçek doğruluk</h3>
  <div id="caveats"></div>
  <div style="text-align:center;margin:14px 0">
    <div class="big" id="acc">—</div>
    <div class="dim" id="accci">henüz doğrulama yok</div>
    <div id="autoacc" style="margin-top:10px;padding-top:10px;border-top:1px solid #2a2f3a"></div>
  </div>
  <table>
    <thead><tr><th>güven bandı</th><th>popülasyon</th><th>doğrulanan</th><th>doğru</th><th>yanlış</th>
      <th>okunamadı</th><th>doğruluk</th><th>%95 aralık</th></tr></thead>
    <tbody id="bands"></tbody>
  </table>

  <div style="margin-top:18px;border-top:1px solid var(--line);padding-top:12px">
    <h3 style="margin:0 0 6px">Gözle bakılması önerilen çiftler <span class="dim" style="font-weight:400">(inceleme kuyruğu)</span></h3>
    <div class="dim" style="font-size:12px;margin-bottom:8px">
      Sık okunan bir plakanın, <b>bariyer kanalında hiç görünmemiş</b> benzer bir komşusu.
      <b>Bu bir hüküm değildir.</b> Ölçüldü: 9 çiftin 8'inde nadir taraf yalnızca baskın plakanın
      zayıf yanlış okumasıdır (masum); yalnız 1'inde nadir taraf GERÇEK plakaydı
      (<code>25ACY952</code> ← <code>25ACV952</code>, 39 olayda bariyeri yanlış açmıştı).
      Veride ikisi aynı görünür — <b>ayrımı yalnız siz, kanıt görselinden yapabilirsiniz.</b>
    </div>
    <div id="suspects"></div>
  </div>
</div>

</div>
<script>
const q = new URLSearchParams(location.search);
const tok = q.get('token');
const T = u => tok ? (u + (u.includes('?') ? '&' : '?') + 'token=' + encodeURIComponent(tok)) : u;
let cur = null;
let zoom = 0;   // 0=orijinal, 1=iyileştir-yakın, 2=iyileştir-geniş. Her yeni olayda sıfırlanır.

async function load(){
  const r = await (await fetch(T('/api/groundtruth/next'))).json();
  const box = document.getElementById('task');
  if (r.done){
    box.innerHTML = '<div style="text-align:center;padding:30px" class="dim">Doğrulanacak olay kalmadı. 🎉</div>';
    return report();
  }
  cur = r;
  document.getElementById('progress').textContent = r.pending + ' olay doğrulanmayı bekliyor';
  zoom = 0;
  document.getElementById('enh').textContent = '🔍 iyileştir';
  document.getElementById('ev').src = T('/' + r.evidence);
  document.getElementById('full').href = r.frame ? T('/' + r.frame) : '#';
  document.getElementById('ours').textContent = r.ours;
  document.getElementById('ocr').textContent = r.ocr.toFixed(4);
  document.getElementById('band').textContent = r.band;
  document.getElementById('cam').textContent = r.cameraId;
  document.getElementById('at').textContent = new Date(r.at).toLocaleString('tr-TR');
  document.getElementById('lowc').innerHTML = r.lowConfidence
    ? '<b style="color:var(--yellow)">operatör onayına düşmüş</b>' : '<b style="color:var(--green)">otomatik açmış</b>';
  document.getElementById('truthbox').style.display = 'none';
  loadContext();
  document.getElementById('truth').value = '';
  report();
}
// İnsan gözü için iyileştirme: tam kareden geniş kadraj + persendil germe + keskinleştirme.
// OCR'ı ETKİLEMEZ — yalnız operatörün okuyabilmesi için. Ölçüm: 5 belirsizin 2'si kurtarıldı.
function enhance(){
  if (!cur) return;
  const img = document.getElementById('ev'), btn = document.getElementById('enh');
  // İKİ KADEMELİ: yakın kadraj en çok büyütür ama açılı plakada il kodunu kesebilir (ölçüldü);
  // geniş kadraj plakanın tamamını gösterir. Operatör ikisini de görebilmeli.
  zoom = (zoom + 1) % 3;
  if (zoom === 0){
    img.src = T('/' + cur.evidence); btn.textContent = '🔍 iyileştir';
  } else if (zoom === 1){
    img.src = T('/api/enhance?eventId=' + cur.eventId + '&pad=0.35');
    btn.textContent = '🔍 daha geniş';
  } else {
    img.src = T('/api/enhance?eventId=' + cur.eventId + '&pad=1.0');
    btn.textContent = '↩ orijinale dön';
  }
}
// Çapraz kanıt şeridi: aynı plakanın (ve lev1 komşularının) diğer okumaları, en net başta.
async function loadContext(){
  const wrap = document.getElementById('ctxwrap'), box = document.getElementById('ctx');
  box.innerHTML = ''; wrap.style.display = 'none';
  if (!cur) return;
  const r = await (await fetch(T('/api/groundtruth/context?eventId=' + cur.eventId))).json();
  if (!r.shots || !r.shots.length) return;
  wrap.style.display = 'block';
  // İlişki türü RENKLE ve ETİKETLE ayrılır — hepsi aynı güçte kanıt DEĞİL:
  //   aynı  = aynı dizgi (en güvenilir)   komşu/benzer = metinsel yakınlık
  //   eş-zamanlı = yalnız ZAMAN yakınlığı; AYNI ARAÇ OLDUĞUNU KANITLAMAZ, aday sunar.
  const LBL = {ayni:'aynı', komsu:'komşu', benzer:'benzer', 'es-zamanli':'eş-zamanlı ⚠'};
  const CLR = {ayni:'#3fb950', komsu:'#d29922', benzer:'#d29922', 'es-zamanli':'#58a6ff'};
  box.innerHTML = r.shots.map(s => `<div style="flex:0 0 auto;text-align:center">
    <img src="${T('/' + s.evidence)}" style="height:78px;border-radius:4px;border:1px solid ${CLR[s.relation]||'#d29922'}">
    <div style="font:600 11px ui-monospace,monospace;margin-top:3px">${s.plate}</div>
    <div style="font-size:10px;color:#8b949e">${LBL[s.relation]||s.relation} · ${s.boxArea}px² · ocr ${s.ocr.toFixed(2)}</div>
  </div>`).join('');
  const hasTime = r.shots.some(s => s.relation === 'es-zamanli');
  document.getElementById('ctxnote').innerHTML = hasTime
    ? '<b style="color:#58a6ff">eş-zamanlı ⚠</b> = yalnız ±15 sn içinde okunmuş GÜÇLÜ (ocr≥0,95) bir okuma. ' +
      'Aynı araç olduğunu KANITLAMAZ — yoğun trafikte başka araç da geçebilir. Kırpıntıya bakıp siz karar verin. ' +
      '<span class="dim">(Ölçüldü: metin benzerliği doğrulanmış hataların yalnız %27\'sinde gerçek plakayı ' +
      'yüzeye çıkardı; bu sinyal %77 — ikisi birlikte %100.)</span>'
    : '';
}
function wrong(){
  document.getElementById('truthbox').style.display = 'flex';
  document.getElementById('truth').focus();
}
async function send(verdict, truth){
  if (!cur) return;
  const body = { eventId: cur.eventId, verdict };
  if (truth) body.truth = truth;
  const res = await fetch(T('/api/groundtruth'), {
    method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (!res.ok){ alert('kaydedilemedi: ' + (await res.text())); return; }
  load();
}
function sendWrong(){
  const t = document.getElementById('truth').value.trim();
  if (!t){ alert('gerçek plakayı yazın'); return; }
  send('Yanlis', t);
}
const PCT = x => (x==null) ? '—' : (100*x).toFixed(1) + '%';
// Kırpıntılar YAN YANA gösterilir: karar tek bir görsele değil, İKİSİNİN karşılaştırmasına dayanır.
async function loadSuspects(){
  const box = document.getElementById('suspects');
  try {
    const r = await (await fetch(T('/api/groundtruth/suspects'))).json();
    if (!r.pairs || !r.pairs.length){ box.innerHTML = '<div class="dim">şüpheli çift yok</div>'; return; }
    box.innerHTML = r.pairs.map(p => `<div style="display:flex;gap:10px;align-items:center;
        padding:6px 0;border-bottom:1px solid #21262d">
      <div style="text-align:center;flex:0 0 auto">
        ${p.dominantEvidence ? `<img src="${T('/'+p.dominantEvidence)}" style="height:56px;border-radius:3px">` : ''}
        <div style="font:600 11px ui-monospace,monospace">${p.dominant}</div>
        <div style="font-size:10px;color:#8b949e">${p.dominantRoiCount} kez bariyer · ocr ≤ ${p.dominantMaxOcr.toFixed(3)}</div>
      </div>
      <div style="color:#8b949e;font-size:18px">↔</div>
      <div style="text-align:center;flex:0 0 auto">
        ${p.rareEvidence ? `<img src="${T('/'+p.rareEvidence)}" style="height:56px;border-radius:3px;border:1px solid #58a6ff">` : ''}
        <div style="font:600 11px ui-monospace,monospace">${p.rare}</div>
        <div style="font-size:10px;color:#8b949e">yalnız gözlem · ${p.rareCount} kez · ocr ≤ ${p.rareMaxOcr.toFixed(3)}</div>
      </div>
    </div>`).join('');
  } catch(e){ box.innerHTML = '<div class="dim">kuyruk yüklenemedi</div>'; }
}
async function report(){
  const r = await (await fetch(T('/api/groundtruth/report'))).json();
  const n = r.totalCorrect + r.totalWrong;
  // KÜÇÜK ÖRNEKLEMDE NOKTA TAHMİN GÖSTERME. n=1'de "100,0%" yazmak, panelin "%95,8" ile yaptığı
  // yanıltmanın aynısıdır — asıl bilgi aralıktır. n≥30'a kadar başlık ARALIK olarak verilir.
  if (r.weightedAccuracy == null){
    document.getElementById('acc').textContent = '—';
    document.getElementById('accci').textContent = 'henüz doğrulama yok';
  } else if (n < 30){
    document.getElementById('acc').textContent = `${PCT(r.weightedCiLow)} – ${PCT(r.weightedCiHigh)}`;
    document.getElementById('accci').innerHTML =
      `<b style="color:var(--yellow)">örneklem çok küçük (n=${n})</b> — nokta tahmin (${PCT(r.weightedAccuracy)}) ` +
      `bu aşamada yanıltıcıdır, aralık gösteriliyor`;
  } else {
    document.getElementById('acc').textContent = PCT(r.weightedAccuracy);
    document.getElementById('accci').textContent =
      `%95 aralık: ${PCT(r.weightedCiLow)} – ${PCT(r.weightedCiHigh)} · ${r.totalCorrect}/${n} doğrulandı`;
  }
  // BARİYER METRİĞİ ayrı gösterilir: eşiğin altındaki okumalar operatöre düşer, bariyeri AÇMAZ —
  // o yüzden oradaki hatalar genel sayıyı düşürür ama bariyer güvenliğini etkilemez. Tek sayı
  // ikisini de yanlış anlatır (ölçüldü: genel %95,1 iken otomatik açma %98,0).
  const an = r.autoOpenVerified - r.autoOpenUnreadable;
  document.getElementById('autoacc').innerHTML = (r.autoOpenAccuracy == null)
    ? '<span class="dim">otomatik açan bantta henüz doğrulama yok</span>'
    : `<b>Bariyeri açan kararda doğruluk: ${an < 30 ? PCT(r.autoOpenCiLow)+' – '+PCT(r.autoOpenCiHigh)
                                                    : PCT(r.autoOpenAccuracy)}</b>` +
      `<span class="dim"> · ocr ≥ eşik olan ${r.autoOpenPopulation} olay · n=${an}` +
      (an >= 30 ? ` · %95 aralık ${PCT(r.autoOpenCiLow)} – ${PCT(r.autoOpenCiHigh)}` : ' (küçük örneklem → aralık)') +
      (r.autoOpenUnreadable > 0
        ? ` · <b style="color:var(--yellow)">${r.autoOpenUnreadable} kayıt doğrulanamadığı hâlde otomatik açmış</b>`
        : '') + '</span>';
  document.getElementById('caveats').innerHTML =
    (r.caveats||[]).map(c => '<div class="warn">⚠ ' + c + '</div>').join('');
  loadSuspects();
  document.getElementById('bands').innerHTML = (r.bands||[]).map(b => `<tr>
    <td>${b.band}</td><td>${b.population}</td><td>${b.verified}</td>
    <td style="color:var(--green)">${b.correct}</td><td style="color:var(--red)">${b.wrong}</td>
    <td class="dim">${b.unreadable}</td><td><b>${PCT(b.accuracy)}</b></td>
    <td class="dim">${b.ciLow==null?'—':PCT(b.ciLow)+' – '+PCT(b.ciHigh)}</td></tr>`).join('');
}
document.getElementById('truth').addEventListener('keydown', e => { if (e.key === 'Enter') sendWrong(); });
load();
</script></body></html>
""";
}
