namespace Anpr.Service.Panel;

/// <summary>Analiz paneli (/analiz): denetlenebilir karşılaştırma tablosu, özet, iş akışı kapıları, sağlık.</summary>
public static class AnalysisPage
{
    public const string Html = """
<!doctype html><html lang="tr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>OtoparkAnpr — Analiz</title>
<style>
:root{--bg:#0d1117;--panel:#161b22;--line:#26303d;--fg:#e6edf3;--dim:#8b949e;
--green:#3fb950;--yellow:#d29922;--red:#f85149;--blue:#58a6ff;--purple:#bc8cff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 ui-sans-serif,-apple-system,"Segoe UI",Roboto,sans-serif}
header{display:flex;align-items:center;gap:14px;padding:12px 18px;border-bottom:1px solid var(--line);
background:var(--panel);position:sticky;top:0;z-index:20;flex-wrap:wrap}
h1{font-size:15px;margin:0;font-weight:650;letter-spacing:.2px}
h1 span{color:var(--dim);font-weight:400}
a.nav{color:var(--blue);text-decoration:none;font-size:13px;border:1px solid var(--line);padding:5px 11px;border-radius:7px}
a.nav:hover{background:#1f2733}
select,input[type=search]{background:#0f151c;color:var(--fg);border:1px solid var(--line);border-radius:7px;padding:5px 9px;font:inherit;font-size:13px}
main{padding:16px 18px 60px;max-width:1700px;margin:0 auto}
h2{font-size:13px;text-transform:uppercase;letter-spacing:.7px;color:var(--dim);margin:26px 0 10px;font-weight:600}
h2:first-child{margin-top:6px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(148px,1fr));gap:10px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px 14px}
.card .k{color:var(--dim);font-size:11px;text-transform:uppercase;letter-spacing:.6px}
.card .v{font-size:23px;font-weight:650;margin-top:3px;font-variant-numeric:tabular-nums}
.card .s{color:var(--dim);font-size:11px;margin-top:2px}
.v.green{color:var(--green)}.v.yellow{color:var(--yellow)}.v.red{color:var(--red)}.v.blue{color:var(--blue)}
table{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{padding:7px 10px;text-align:left;border-bottom:1px solid var(--line);font-size:13px;vertical-align:middle}
/* Başlık, TABLONUN KENDİ kaydırma kabında sabitlenir — sayfa başlığının yüksekliğine (değişken,
   flex-wrap ile kayar) bağlı bir "top" değeri vermek başlığı yanlış yere oturtuyordu. */
th{color:var(--dim);font-size:11px;text-transform:uppercase;letter-spacing:.5px;background:#131922;position:sticky;top:0;z-index:5}
tbody tr:hover{background:#1a212b}
td.plate{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-weight:600;letter-spacing:.4px}
td.num{text-align:right;font-variant-numeric:tabular-nums}
.tag{display:inline-block;padding:1px 7px;border-radius:20px;font-size:11px;font-weight:600;white-space:nowrap}
.t-birebir{background:#12341f;color:var(--green)}
.t-1-fark{background:#3a2c0c;color:var(--yellow)}
.t-farkli{background:#3d1618;color:var(--red)}
.t-sadece-biz{background:#132a42;color:var(--blue)}
.t-sadece-kamera{background:#2d1f3d;color:var(--purple)}
.t-low{background:#3a2c0c;color:var(--yellow)}
.t-snap{background:#2d1f3d;color:var(--purple)}
img.ev{height:42px;border-radius:4px;border:1px solid var(--line);cursor:zoom-in;display:block;background:#000}
.wrap{overflow:auto;border-radius:10px;max-height:74vh;border:1px solid var(--line)}
.wrap.short{max-height:none}
table{border:0}
.bars{display:flex;flex-direction:column;gap:5px}
.bar{display:grid;grid-template-columns:88px 1fr 52px;gap:9px;align-items:center;font-size:12px}
.bar .t{color:var(--dim);font-variant-numeric:tabular-nums}
.bar .g{height:16px;background:#0f151c;border-radius:4px;overflow:hidden;border:1px solid var(--line)}
.bar .g>i{display:block;height:100%;background:linear-gradient(90deg,#1f6feb,#58a6ff)}
.bar .n{text-align:right;font-variant-numeric:tabular-nums;color:var(--dim)}
.flow{display:flex;flex-wrap:wrap;gap:8px;align-items:stretch}
.step{background:var(--panel);border:1px solid var(--line);border-radius:9px;padding:9px 12px;min-width:152px;flex:1}
.step .n{font-size:10px;color:var(--dim);letter-spacing:.5px}
.step .t{font-weight:600;margin:2px 0 3px;font-size:13px}
.step .g{font-size:11px;color:var(--dim);font-family:ui-monospace,Menlo,monospace}
.step .c{font-size:11px;margin-top:4px}
.step.drop{border-color:#5a2a2a}
.hint{color:var(--dim);font-size:12px;margin:6px 0 0}
#lb{position:fixed;inset:0;background:#000c;display:none;align-items:center;justify-content:center;z-index:99;flex-direction:column;gap:10px}
#lb img{max-width:94vw;max-height:82vh;border-radius:8px}
#lb .cap{font-family:ui-monospace,Menlo,monospace;font-size:13px}
.muted{color:var(--dim)}
</style></head><body>
<header>
  <h1>📊 OtoparkAnpr <span>— Analiz</span></h1>
  <a class="nav" href="/panel">← Canlı panel</a>
  <select id="fCam"><option value="">Tüm kameralar</option></select>
  <select id="fScope"><option value="roi">Kapsam: roi (bariyer)</option><option value="">Kapsam: hepsi</option><option value="gozlem">Kapsam: gözlem</option></select>
  <select id="fVerdict"><option value="">Tüm sonuçlar</option><option value="birebir">Birebir</option><option value="1-fark">1-fark</option><option value="farkli">Farklı</option><option value="sadece-biz">Sadece biz</option><option value="sadece-kamera">Sadece kamera</option><option value="snap">Snap uygulanan</option><option value="low">Düşük güven</option></select>
  <input type="search" id="fText" placeholder="plaka ara…" size="12">
  <span class="muted" id="stamp">—</span>
</header>
<main>
  <h2>Özet</h2>
  <div class="cards" id="cards"></div>
  <p class="hint" id="sumHint"></p>

  <h2>İş akışı — araç geçişinden olaya (canlı sayaçlar)</h2>
  <div class="flow" id="flow"></div>
  <p class="hint">Her kutu boru hattındaki bir adımı ve o adımı yöneten ayarı gösterir. Kırmızı kenarlı kutular <b>eleme kapılarıdır</b>: sayaç, o kapının şimdiye kadar kaç tespiti düşürdüğüdür.</p>

  <h2>Sistem sağlığı</h2>
  <div class="wrap short"><table id="health"></table></div>

  <h2>OCR güven dağılımı <span class="muted" id="confN"></span></h2>
  <div class="bars" id="conf"></div>

  <h2>Snap denetimi <span class="muted">— whitelist düzeltmeleri, kamerayla çapraz doğrulanmış</span></h2>
  <div class="wrap short"><table id="snapT"></table></div>

  <h2>Karşılaştırma tablosu <span class="muted" id="rowsN"></span></h2>
  <div class="wrap"><table id="cmp"></table></div>
  <p class="hint">Eşleştirme sunucu tarafında, <b>tüm kayıt kümesi</b> üzerinde yapılır: aynı kamera + zaman penceresi (bizden 8 sn önce … 30 sn sonra; kamera kendi sonucunu geç yayınlar) + en yakın metin. Her kamera kaydı <b>en fazla bir</b> olayla eşleşir.</p>
</main>
<div id="lb"><img id="lbImg" alt=""><div class="cap" id="lbCap"></div></div>
<script>
const $=s=>document.querySelector(s);
let DATA={summary:null,rows:[],cameraOnly:[],health:null};

function tag(v){return `<span class="tag t-${v}">${v}</span>`}
function card(k,v,s,cls){return `<div class="card"><div class="k">${k}</div><div class="v ${cls||''}">${v}</div><div class="s">${s||''}</div></div>`}

function renderCards(){
  const s=DATA.summary; if(!s)return;
  const pct=s.agreementPct==null?'—':s.agreementPct+'%';
  const pctCls=s.agreementPct==null?'':(s.agreementPct>=90?'green':s.agreementPct>=75?'yellow':'red');
  $('#cards').innerHTML=[
    card('Olay (roi)',s.roiCount,`+${s.observationCount} gözlem`),
    card('Kamerayla eşleşen',s.matched,`${s.oursOnly} sadece biz · ${s.cameraOnly} sadece kamera`),
    card('Birebir uyum',pct,`${s.exact} birebir · ${s.near} 1-fark · ${s.far} farklı`,pctCls),
    card('Ort. OCR güveni',s.avgOcr,'roi olayları'),
    card('Otomatik bariyer',s.autoOpen,`${s.lowConfidence} operatör onayına düşer`,'blue'),
    card('Snap düzeltmesi',s.snap.total,`${s.snap.verifiedOk} doğru · ${s.snap.verifiedBad} yanlış · ${s.snap.unverified} teyitsiz`,s.snap.verifiedBad>0?'yellow':'green'),
    card('Kamera gecikmesi',s.medianLag+' sn','medyan (kamera − biz)'),
  ].join('');
  $('#sumHint').innerHTML=`<b>Birebir uyum</b>, kamerayla eşleşen olaylar içindeki tam eşleşme oranıdır — mutlak doğruluk değildir: kameranın kendi ANPR'ı da yanılır (bazı geçişlerde <code>unknown</code> döner). "Sadece biz", kameranın o geçişi hiç kaydetmediği durumları da içerir.`;
}

function renderFlow(){
  const h=DATA.health, s=DATA.summary; if(!h||!s)return;
  const sum=k=>h.cameras.reduce((a,c)=>a+(c[k]||0),0);
  const steps=[
    {n:'1',t:'Kare alma',g:'ffmpeg · RTSP',c:`${sum('framesReceived').toLocaleString('tr')} kare alındı`},
    {n:'2',t:'Örnekleme',g:`Fps=${h.fps??'—'} / IdleFps=${h.idleFps??'—'}`,c:`${sum('framesProcessed').toLocaleString('tr')} kare işlendi`},
    {n:'3',t:'Tespit',g:'YOLO · MinConfidence',c:'plaka kutuları'},
    {n:'4',t:'Geometri kapısı',g:`MinPlateAspectRatio=${h.minAspect??'—'}`,c:`${sum('aspectDropped')} elendi`,drop:1},
    {n:'5',t:'Kalite kapısı',g:'blur / karanlık / patlama',c:`${sum('qualityDropped')} elendi`,drop:1},
    {n:'6',t:'OCR + TR gramer',g:'CCT · kısıtlı çözümleme',c:'karakter dağılımı'},
    {n:'7',t:'Çok-kareli onay',g:`MinConfirmations=${h.minConfirm??'—'} · settle=${h.settle??'—'}s`,c:'oylama + birleştirme'},
    {n:'8',t:'Snap (whitelist)',g:`lev≤${h.snapLev??'—'} · güven<${h.snapGate??'—'}`,c:`${s.snap.total} düzeltme`},
    {n:'9',t:'Emit tabanı',g:`EmitMinOcr=${h.emitMinOcr??'—'}`,c:'eşik altı yayınlanmaz',drop:1},
    {n:'10',t:'Varyant bastırma',g:`${h.variantSec??'—'} sn pencere`,c:'hayalet okuma elenir',drop:1},
    {n:'11',t:'Olay',g:`ManualReviewMinOcr=${h.manualMin??'—'}`,c:`${s.autoOpen} otomatik · ${s.lowConfidence} onaya`},
  ];
  $('#flow').innerHTML=steps.map(x=>`<div class="step ${x.drop?'drop':''}"><div class="n">ADIM ${x.n}</div><div class="t">${x.t}</div><div class="g">${x.g}</div><div class="c">${x.c}</div></div>`).join('');
}

function renderHealth(){
  const h=DATA.health; if(!h)return;
  const rows=h.cameras.map(c=>`<tr>
    <td class="plate">${c.id}</td>
    <td>${c.connected?'<span class="tag t-birebir">bağlı</span>':'<span class="tag t-farkli">kopuk</span>'}</td>
    <td class="num">${(c.framesReceived||0).toLocaleString('tr')}</td>
    <td class="num">${(c.framesProcessed||0).toLocaleString('tr')}</td>
    <td class="num">${c.frameMsP50??'—'}</td><td class="num">${c.frameMsP95??'—'}</td>
    <td class="num">${c.eventsEmitted??0}</td><td class="num">${c.restartCount??0}</td>
    <td class="num">${c.qualityDropped??0}</td><td class="num">${c.aspectDropped??0}</td>
    <td class="muted">${c.lastEventUtc?new Date(c.lastEventUtc).toLocaleTimeString('tr'):'—'}</td></tr>`).join('');
  $('#health').innerHTML=`<thead><tr><th>Kamera</th><th>Durum</th><th>Kare</th><th>İşlenen</th><th>p50 ms</th><th>p95 ms</th><th>Olay</th><th>Restart</th><th>Kalite elenen</th><th>Geometri elenen</th><th>Son olay</th></tr></thead><tbody>${rows}</tbody>`;
}

function renderConf(){
  const s=DATA.summary; if(!s)return;
  const max=Math.max(1,...s.confidence.map(b=>b.count));
  const tot=s.confidence.reduce((a,b)=>a+b.count,0);
  $('#confN').textContent=`(${tot} roi olayı)`;
  $('#conf').innerHTML=s.confidence.map(b=>`<div class="bar"><span class="t">${b.label}</span>
    <span class="g"><i style="width:${100*b.count/max}%"></i></span><span class="n">${b.count}</span></div>`).join('');
}

function renderSnap(){
  const rows=DATA.rows.filter(r=>r.snappedFrom);
  if(!rows.length){$('#snapT').innerHTML='<thead><tr><th>Snap</th></tr></thead><tbody><tr><td class="muted">Bu filtrede snap düzeltmesi yok.</td></tr></tbody>';return}
  $('#snapT').innerHTML=`<thead><tr><th>Zaman</th><th>Kamera</th><th>Ham okuma</th><th>→ Düzeltilmiş</th><th>Kamera ne dedi</th><th>Sonuç</th><th class="num">Güven</th><th>Kanıt</th></tr></thead><tbody>`+
    rows.map(r=>{
      let v='<span class="tag t-sadece-biz">teyitsiz</span>';
      if(r.camera&&r.camera===r.ours)v='<span class="tag t-birebir">snap DOĞRU</span>';
      else if(r.camera&&r.camera===r.snappedFrom)v='<span class="tag t-farkli">snap YANLIŞ</span>';
      return `<tr><td>${r.time}</td><td>${r.cameraId}</td><td class="plate muted">${r.snappedFrom}</td>
      <td class="plate">${r.ours}</td><td class="plate muted">${r.camera||'—'}</td><td>${v}</td>
      <td class="num">${r.ocr.toFixed(2)}</td><td>${ev(r)}</td></tr>`}).join('')+'</tbody>';
}

function ev(r){
  if(!r.evidence)return '<span class="muted">—</span>';
  const f=r.frame?`data-full="/${r.frame}"`:'';
  return `<img class="ev" src="/${r.evidence}" ${f} data-cap="${r.ours} · ${r.time}" loading="lazy" alt="">`;
}

function filtered(){
  const v=$('#fVerdict').value, q=$('#fText').value.trim().toUpperCase();
  let rows=DATA.rows.concat(DATA.cameraOnly);
  if(v==='snap')rows=rows.filter(r=>r.snappedFrom);
  else if(v==='low')rows=rows.filter(r=>r.lowConfidence);
  else if(v)rows=rows.filter(r=>r.verdict===v);
  if(q)rows=rows.filter(r=>(r.ours||'').includes(q)||(r.camera||'').includes(q));
  return rows.sort((a,b)=>a.time<b.time?1:-1);
}

function renderCmp(){
  const rows=filtered();
  $('#rowsN').textContent=`(${rows.length} satır)`;
  $('#cmp').innerHTML=`<thead><tr><th>Zaman</th><th>Kamera</th><th>Bizim okuma</th><th>Kamera ANPR</th>
    <th class="num">Fark</th><th class="num">Gecikme</th><th class="num">Güven</th><th>Sonuç</th><th>Bayrak</th><th>Kanıt</th></tr></thead><tbody>`+
    rows.slice(0,600).map(r=>{
      const flags=[r.lowConfidence?'<span class="tag t-low">düşük güven</span>':'',
                   r.snappedFrom?`<span class="tag t-snap">snap←${r.snappedFrom}</span>`:'',
                   r.scope!=='roi'?'<span class="tag t-sadece-biz">gözlem</span>':''].filter(Boolean).join(' ');
      return `<tr><td>${r.time}</td><td>${r.cameraId}</td>
        <td class="plate">${r.ours||'<span class="muted">—</span>'}</td>
        <td class="plate ${r.camera?'':'muted'}">${r.camera||'—'}</td>
        <td class="num">${r.distance<0?'—':r.distance}</td>
        <td class="num">${r.lagSeconds==null?'—':r.lagSeconds+'s'}</td>
        <td class="num">${r.ours?r.ocr.toFixed(2):'—'}</td>
        <td>${tag(r.verdict)}</td><td>${flags||'<span class="muted">—</span>'}</td><td>${ev(r)}</td></tr>`}).join('')+'</tbody>';
  if(rows.length>600)$('#rowsN').textContent=`(${rows.length} satır — ilk 600 gösteriliyor)`;
}

document.addEventListener('click',e=>{
  const img=e.target.closest('img.ev');
  if(img){ $('#lbImg').src=img.dataset.full||img.src; $('#lbCap').textContent=img.dataset.cap+(img.dataset.full?' · tam kare':''); $('#lb').style.display='flex'; return }
  if(e.target.closest('#lb'))$('#lb').style.display='none';
});

async function load(){
  const cam=$('#fCam').value, scope=$('#fScope').value;
  const qs=new URLSearchParams(); if(cam)qs.set('cameraId',cam); if(scope)qs.set('scope',scope);
  const [a,h]=await Promise.all([
    fetch('/api/analytics?'+qs).then(r=>r.json()),
    fetch('/health').then(r=>r.json()).catch(()=>null)
  ]);
  DATA.summary=a.summary; DATA.rows=a.rows; DATA.cameraOnly=a.cameraOnly; DATA.health=Object.assign({},h,a.config||{});
  if(!$('#fCam').dataset.filled && h?.cameras){
    $('#fCam').insertAdjacentHTML('beforeend',h.cameras.map(c=>`<option value="${c.id}">${c.name||c.id}</option>`).join(''));
    $('#fCam').dataset.filled='1';
  }
  $('#stamp').textContent='güncellendi '+new Date().toLocaleTimeString('tr');
  renderCards(); renderFlow(); renderHealth(); renderConf(); renderSnap(); renderCmp();
}
['#fCam','#fScope'].forEach(s=>$(s).addEventListener('change',load));
['#fVerdict'].forEach(s=>$(s).addEventListener('change',renderCmp));
$('#fText').addEventListener('input',renderCmp);
load(); setInterval(load,15000);
</script></body></html>
""";
}
