namespace Anpr.Service.Panel;

/// <summary>Canlı karşılaştırma paneli — tek gömülü HTML (CDN'siz; internetsiz hedef PC'de çalışır).
/// Önizleme: gerçek MJPEG akışı (kaynak hızında) + üstünde saydam overlay (tespit kutuları + ROI).</summary>
public static class PanelPage
{
    public const string Html = """
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<title>OtoparkAnpr — Canlı Karşılaştırma</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  :root { --bg:#0e1116; --card:#161b23; --line:#232b37; --tx:#dbe4ee; --dim:#8494a8;
          --green:#2fbf71; --yellow:#e7b416; --red:#e05252; --blue:#4f8fe6; }
  * { box-sizing:border-box; margin:0; }
  body { background:var(--bg); color:var(--tx); font:14px/1.45 -apple-system,'Segoe UI',Roboto,sans-serif; }
  header { display:flex; flex-wrap:wrap; gap:10px; align-items:center; padding:10px 14px;
           border-bottom:1px solid var(--line); position:sticky; top:0; background:var(--bg); z-index:5; }
  h1 { font-size:16px; margin-right:8px; }
  .tabs button { background:var(--card); color:var(--tx); border:1px solid var(--line);
                 padding:6px 14px; cursor:pointer; font-size:14px; }
  .tabs button:first-child { border-radius:8px 0 0 8px; } .tabs button:last-child { border-radius:0 8px 8px 0; }
  .tabs button.on { background:#24425f; border-color:#3a6ea5; }
  .chips { display:flex; gap:8px; margin-left:auto; flex-wrap:wrap; }
  .chip { background:var(--card); border:1px solid var(--line); border-radius:999px; padding:4px 10px; font-size:12.5px; }
  .chip b { font-size:14px; }
  .dot { display:inline-block; width:8px; height:8px; border-radius:50%; margin-right:5px; vertical-align:1px; }
  main { padding:12px 14px; display:grid; gap:12px; }
  .preview { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:10px; }
  .preview .bar { display:flex; gap:8px; align-items:center; margin-bottom:8px; flex-wrap:wrap; }
  .stage { position:relative; border-radius:6px; overflow:hidden; background:#000; }
  .stage img { width:100%; max-height:58vh; display:block; object-fit:contain; }
  .stage canvas { position:absolute; inset:0; width:100%; height:100%; cursor:crosshair; }
  button.act { background:#24425f; color:var(--tx); border:1px solid #3a6ea5; border-radius:6px; padding:5px 12px; cursor:pointer; }
  button.act[disabled] { opacity:.45; cursor:default; }
  button.warn { background:#5f2424; border-color:#a53a3a; }
  .cols { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
  @media (max-width:800px){ .cols { grid-template-columns:1fr; } }
  .col { background:var(--card); border:1px solid var(--line); border-radius:10px; overflow:hidden; }
  .col h2 { font-size:13.5px; padding:8px 12px; border-bottom:1px solid var(--line); color:var(--dim);
            display:flex; justify-content:space-between; align-items:center; }
  .rows { max-height:44vh; overflow-y:auto; }
  .row { display:flex; gap:10px; align-items:center; padding:7px 12px; border-bottom:1px solid var(--line);
         border-left:4px solid transparent; }
  .row .plate { font:600 16px/1 ui-monospace,Menlo,monospace; letter-spacing:1px; }
  .row .meta { color:var(--dim); font-size:12px; margin-left:auto; text-align:right; }
  .row.obs { opacity:.72; }
  .scopetag { font-size:10.5px; color:var(--dim); border:1px solid var(--line); border-radius:999px; padding:1px 7px; }
  .thumb { height:26px; border-radius:4px; border:1px solid var(--line); display:block; }
  .lowconf { color:var(--yellow); border-color:var(--yellow); }
  .m-green { border-left-color:var(--green); } .m-yellow { border-left-color:var(--yellow); }
  .m-red { border-left-color:var(--red); } .m-blue { border-left-color:var(--blue); }
  .hint { color:var(--dim); font-size:12px; }
  #roiBadge { font-size:12px; }
</style>
</head>
<body>
<header>
  <h1>🅿️ Canlı Karşılaştırma</h1>
  <div class="tabs" id="tabs"></div>
  <div class="chips">
    <span class="chip"><span class="dot" style="background:var(--green)"></span>Birebir <b id="cExact">0</b> (<span id="cPct">–</span>)</span>
    <span class="chip"><span class="dot" style="background:var(--yellow)"></span>1-fark <b id="cNear">0</b></span>
    <span class="chip"><span class="dot" style="background:var(--red)"></span>Sadece kamera <b id="cCamOnly">0</b></span>
    <span class="chip"><span class="dot" style="background:var(--blue)"></span>Sadece biz <b id="cOursOnly">0</b></span>
    <span class="chip" id="svcBadge">servis: <b>—</b></span>
    <span class="chip" id="camBadge">kamera: <b>—</b></span>
  </div>
</header>
<main>
  <section class="preview">
    <div class="bar">
      <button class="act" id="roiDraw">🖊️ ROI Çiz</button>
      <button class="act" id="roiSave" disabled>💾 Kaydet</button>
      <button class="act" id="roiCancel" disabled>İptal</button>
      <button class="act warn" id="roiClear">ROI Temizle</button>
      <span class="chip" id="roiBadge">ROI: —</span>
      <span class="hint">Sarı çokgen = BİZİM okuma alanı (kameraya yazılmaz; kalıcıdır). Çiz: tıkla-tıkla, çift-tık kapat.</span>
    </div>
    <div class="stage">
      <img id="live" alt="canlı">
      <canvas id="cv"></canvas>
    </div>
  </section>
  <section class="cols">
    <div class="col"><h2>📷 Hikvision (kamera ANPR) <span class="hint" id="camCount"></span></h2><div class="rows" id="camRows"></div></div>
    <div class="col"><h2>🤖 Bizim model <span class="hint" id="ourCount"></span></h2><div class="rows" id="ourRows"></div></div>
  </section>
</main>
<script>
"use strict";
const $=id=>document.getElementById(id);
const TOKEN=new URLSearchParams(location.search).get("token");
const withTok=u=>TOKEN?u+(u.includes("?")?"&":"?")+"token="+encodeURIComponent(TOKEN):u;
let cams=[], cam=null;
const feed={};
let roi=[], roiDraft=null;
let natW=0, natH=0;
const live=$("live"), cv=$("cv"), ctx=cv.getContext("2d");

function lev(a,b){if(a===b)return 0;const m=a.length,n=b.length;if(!m)return n;if(!n)return m;
 let dp=Array.from({length:n+1},(_,j)=>j);
 for(let i=1;i<=m;i++){let prev=dp[0];dp[0]=i;
  for(let j=1;j<=n;j++){const cur=dp[j];dp[j]=Math.min(dp[j]+1,dp[j-1]+1,prev+(a[i-1]===b[j-1]?0:1));prev=cur;}}
 return dp[n];}
const compact=s=>(s||"").toUpperCase().replace(/[^A-Z0-9]/g,"");
function ensure(id){ if(!feed[id]) feed[id]={cam:[],ours:[],keys:new Set()}; return feed[id]; }
const COLLAPSE_MS=180000; // aynı plaka bu pencerede tek satır (kamera kendi-kopyaları, bizim gözlem+roi çifti)
function push(f,side,rec,key){ if(f.keys.has(key))return; f.keys.add(key);
  const arr=f[side], pc=compact(rec.plate);
  for(let i=arr.length-1;i>=0;i--){
    const r=arr[i];
    if(rec.t-r.t>COLLAPSE_MS)break;
    if(compact(r.plate)===pc){
      r.t0=Math.min(r.t0||r.t,rec.t); r.t=Math.max(r.t,rec.t); r.n=(r.n||1)+1;
      if(rec.conf!=null&&(r.conf==null||rec.conf>r.conf))r.conf=rec.conf;
      if(rec.ev)r.ev=rec.ev;
      if(rec.scope==="roi")r.scope="roi"; // bölge-içi okuma gözlemi yükseltir
      return;
    }
  }
  arr.push(rec); if(arr.length>200)arr.shift(); }

function inWin(c,o,ms){ const lo=(o.t0||o.t)-ms, hi=o.t+ms; return c.t>=lo&&c.t<=hi; }
function rematch(f,other){
  f.cam.forEach(r=>{r.match="red";r.pair=null;r.cross=false;});
  f.ours.forEach(r=>{r.match="blue";r.pair=null;});
  // 1. geçiş: yalnız BİREBİR eşleşmeler (varyant satırının birebir eşi çalmasını önler)
  for(const o of f.ours){
    if(o.pair)continue;
    for(const c of f.cam){
      if(c.pair)continue;
      if(!inWin(c,o,30000))continue;
      if(compact(o.plate)===compact(c.plate)){o.pair=c;c.pair=o;o.match=c.match="green";break;}
    }
  }
  // 2. geçiş: 1-fark
  for(const o of f.ours){
    if(o.pair)continue;
    let best=null;
    for(const c of f.cam){
      if(c.pair)continue;
      if(!inWin(c,o,30000))continue;
      if(lev(compact(o.plate),compact(c.plate))===1){best=c;break;}
    }
    if(best){o.pair=best;best.pair=o;o.match=best.match="yellow";}
  }
  // 2. geçiş: kalan kırmızılar cadde-geçişi olabilir — BİZİM DİĞER kameradaki okumayla eşle (⇄).
  if(other){
    for(const c of f.cam){
      if(c.pair)continue;
      let bestD=2, hit=null;
      for(const o of other.ours){
        if(!inWin(c,o,45000))continue;
        const d=lev(compact(c.plate),compact(o.plate));
        if(d<bestD){bestD=d;hit=o;if(!d)break;}
      }
      if(hit){c.match=(bestD===0?"green":"yellow");c.cross=true;}
    }
  }
}
function board(f){
  // Adil sayım penceresi: iki tarafın da kaydı olan ORTAK aralık (servis yeniden başlatma /
  // kamera tamponu farkları tek taraflı "kaçırdı" sayımı üretmesin diye).
  const t0=Math.max(
    f.cam.length ?Math.min(...f.cam.map(x=>x.t)) :0,
    f.ours.length?Math.min(...f.ours.map(x=>x.t)):0);
  const cams=f.cam.filter(x=>x.t>=t0), ours=f.ours.filter(x=>x.t>=t0);
  const g=ours.filter(r=>r.match==="green").length,
        y=ours.filter(r=>r.match==="yellow").length,
        r=cams.filter(x=>x.match==="red").length,
        b=ours.filter(x=>x.match==="blue").length, tot=g+y+r;
  $("cExact").textContent=g; $("cNear").textContent=y;
  $("cCamOnly").textContent=r; $("cOursOnly").textContent=b;
  $("cPct").textContent=tot?Math.round(100*g/tot)+"%":"–";
}
function row(r,side){
  const d=document.createElement("div");
  d.className="row m-"+r.match+(side==="ours"&&r.scope==="gozlem"?" obs":"");
  const time=new Date(r.t).toLocaleTimeString("tr-TR");
  let tag=side==="ours"&&r.scope==="gozlem"?' <span class="scopetag">uzak</span>':"";
  if(side==="ours"&&r.low)tag+=' <span class="scopetag lowconf" title="güven eşiğin altında — operatör onayı önerilir">düşük güven</span>';
  if(side==="cam"&&r.cross)tag+=' <span class="scopetag" title="bizim diğer kamera okudu">⇄ diğer kamera</span>';
  const times=r.n>1?" ×"+r.n:"";
  const thumb=side==="ours"&&r.ev?`<a href="${withTok("/"+r.ev)}" target="_blank"><img class="thumb" src="${withTok("/"+r.ev)}" alt=""></a>`:"";
  if(side==="ours"&&r.cands)d.title="adaylar: "+r.cands;
  d.innerHTML=`<span class="plate">${r.plate}</span>${tag}${thumb}
    <span class="meta">${time}${times}${side==="ours"&&r.conf!=null?"<br>güven "+r.conf:""}</span>`;
  return d;
}
function render(){
  if(!cam)return;
  const f=ensure(cam);
  const oc=cams.find(c=>c.id!==cam);
  rematch(f, oc?ensure(oc.id):null); board(f);
  $("camRows").replaceChildren(...f.cam.slice(-60).reverse().map(r=>row(r,"cam")));
  $("ourRows").replaceChildren(...f.ours.slice(-60).reverse().map(r=>row(r,"ours")));
  $("camCount").textContent=f.cam.length+" kayıt";
  $("ourCount").textContent=f.ours.length+" kayıt";
}

// ---- SSE ----
let es=null, retry=1000;
function connect(){
  es=new EventSource(withTok("/api/stream"));
  es.onopen=()=>{retry=1000;$("svcBadge").innerHTML='servis: <b style="color:var(--green)">bağlı</b>';};
  es.onerror=()=>{es.close();$("svcBadge").innerHTML='servis: <b style="color:var(--red)">koptu</b>';
    setTimeout(connect,retry);retry=Math.min(retry*2,15000);};
  es.onmessage=e=>{
    const m=JSON.parse(e.data);
    if(m.source==="status"){ if(m.cameraId===cam)
      $("camBadge").innerHTML='kamera: <b style="color:var(--'+(m.cameraPollOk?'green':'red')+')">'+(m.cameraPollOk?'ok':'hata')+'</b>';
      return; }
    const f=ensure(m.cameraId);
    const rec={plate:m.plate,t:Date.parse(m.timeIso),conf:m.conf,scope:m.scope,ev:m.ev,low:m.lowConf,cands:m.cands};
    push(f, m.source==="camera"?"cam":"ours", rec, m.source+"|"+m.plate+"|"+m.timeIso);
    render();
  };
}

// ---- Sayfa-açılış dolumu (yenilemede tablo boş kalmasın) ----
async function backfill(){
  try{
    const ev=await(await fetch(withTok("/events/recent?count=100"))).json();
    // API en-yeni-önce döner; tampon kronolojik tutulur (render sonda ters çevirir) -> eskiden yeniye it.
    for(const e of (ev.events||[]).slice().reverse()){
      const f=ensure(e.cameraId);
      push(f,"ours",{plate:e.plate,t:Date.parse(e.timestamp),conf:Math.round(e.ocrConfidence*100)/100,scope:e.scope,ev:e.evidencePath,low:e.lowConfidence},
           "ours|"+e.plate+"|"+e.timestamp);
    }
  }catch{}
  for(const c of cams){
    try{
      const d=await(await fetch(withTok(`/api/camera/recent?cameraId=${c.id}`))).json();
      const f=ensure(c.id);
      for(const r of d.records||[])
        push(f,"cam",{plate:r.plate,t:Date.parse(r.timeIso)},"camera|"+r.plate+"|"+r.timeIso);
    }catch{}
  }
}

// ---- Canlı video (MJPEG) + overlay ----
function setStream(){
  live.src=withTok(`/api/mjpeg/${cam}?t=${Date.now()}`);
}
live.addEventListener("load",()=>{ natW=live.naturalWidth; natH=live.naturalHeight; });
function syncCanvas(){
  const w=live.clientWidth, h=live.clientHeight;
  if(w&&h&&(cv.width!==w||cv.height!==h)){cv.width=w;cv.height=h;}
}
// object-fit:contain letterbox telafisi: video içeriğinin canvas içindeki gerçek dikdörtgeni.
function contentRect(){
  const cw=cv.width, ch=cv.height;
  if(!natW||!natH||!cw||!ch) return {x:0,y:0,w:cw,h:ch};
  const sc=Math.min(cw/natW, ch/natH);
  const w=natW*sc, h=natH*sc;
  return {x:(cw-w)/2, y:(ch-h)/2, w, h};
}
function toCanvas(p){ const r=contentRect(); return [r.x+p[0]*r.w/1000, r.y+p[1]*r.h/1000]; }
let lastBoxes=[];
function draw(){
  syncCanvas();
  ctx.clearRect(0,0,cv.width,cv.height);
  // MJPEG'de naturalWidth ilk kareden sonra sabit — kutular doğal→görünen ölçeğiyle çizilir.
  natW=live.naturalWidth||natW; natH=live.naturalHeight||natH;
  const cr=contentRect();
  if(natW) for(const b of lastBoxes||[]){
    ctx.strokeStyle="#e05252"; ctx.lineWidth=2;
    ctx.strokeRect(cr.x+b.x*cr.w/natW, cr.y+b.y*cr.h/natH, b.w*cr.w/natW, b.h*cr.h/natH);
  }
  const poly=roiDraft??roi;
  if(poly.length){
    ctx.beginPath();
    poly.forEach((p,i)=>{const[x,y]=toCanvas(p); i?ctx.lineTo(x,y):ctx.moveTo(x,y);});
    if(!roiDraft) ctx.closePath();
    ctx.strokeStyle="#e7b416"; ctx.lineWidth=2.5; ctx.setLineDash(roiDraft?[7,5]:[]); ctx.stroke(); ctx.setLineDash([]);
    if(!roiDraft){ ctx.fillStyle="rgba(231,180,22,.13)"; ctx.fill(); }
    for(const p of poly){const[x,y]=toCanvas(p); ctx.fillStyle="#e7b416"; ctx.beginPath(); ctx.arc(x,y,4,0,7); ctx.fill();}
  }
  requestAnimationFrame(draw);
}
async function pollDetections(){
  if(!cam)return;
  try{
    const d=await(await fetch(withTok(`/api/detections/${cam}`))).json();
    lastBoxes=d.boxes||[];
    if(!roiDraft){ roi=parseRoi(d.roi); $("roiBadge").textContent = roi.length?`ROI: kayıtlı ✓ (${roi.length} nokta)`:"ROI: yok (tüm kare)"; }
  }catch{}
}
const parseRoi=s=>!s?[]:s.split(";").map(p=>p.split(",").map(Number)).filter(p=>p.length===2&&p.every(Number.isFinite));
const roiToStr=pts=>pts.map(p=>Math.round(p[0])+","+Math.round(p[1])).join(";");

cv.addEventListener("click",e=>{
  if(!roiDraft)return;
  const br=cv.getBoundingClientRect();
  const cx=(e.clientX-br.left)*cv.width/br.width, cy=(e.clientY-br.top)*cv.height/br.height;
  const cr=contentRect();
  // yalnız video içeriği üstündeki tıklar sayılır (letterbox dışı yok sayılır)
  if(cx<cr.x||cy<cr.y||cx>cr.x+cr.w||cy>cr.y+cr.h)return;
  const nx=Math.max(0,Math.min(1000,(cx-cr.x)/cr.w*1000));
  const ny=Math.max(0,Math.min(1000,(cy-cr.y)/cr.h*1000));
  roiDraft.push([nx,ny]);
});
cv.addEventListener("dblclick",e=>{ if(roiDraft&&roiDraft.length>=3){e.preventDefault();$("roiSave").disabled=false;} });
$("roiDraw").onclick=()=>{roiDraft=[];$("roiSave").disabled=true;$("roiCancel").disabled=false;};
$("roiCancel").onclick=()=>{roiDraft=null;$("roiSave").disabled=true;$("roiCancel").disabled=true;};
$("roiSave").onclick=async()=>{
  await fetch(withTok(`/api/roi/${cam}`),{method:"PUT",headers:{"Content-Type":"application/json"},
    body:JSON.stringify({roi:roiToStr(roiDraft)})});
  roi=roiDraft; roiDraft=null;
  $("roiSave").disabled=true;$("roiCancel").disabled=true;
  $("roiBadge").textContent=`ROI: kayıtlı ✓ (${roi.length} nokta)`;
};
$("roiClear").onclick=async()=>{
  if(!confirm("Bu kameranın ROI'si temizlensin mi? (Tüm kare okunur)"))return;
  await fetch(withTok(`/api/roi/${cam}`),{method:"PUT",headers:{"Content-Type":"application/json"},body:JSON.stringify({roi:""})});
  roi=[];roiDraft=null;$("roiBadge").textContent="ROI: yok (tüm kare)";
};

async function init(){
  const h=await(await fetch("/health")).json();
  cams=h.cameras.map(c=>({id:c.id,name:c.name}));
  const t=$("tabs");
  cams.forEach((c,i)=>{
    const b=document.createElement("button");
    b.textContent=c.name||c.id;
    b.onclick=()=>{cam=c.id;[...t.children].forEach(x=>x.classList.remove("on"));b.classList.add("on");
                   roiDraft=null;$("roiSave").disabled=true;$("roiCancel").disabled=true;
                   setStream(); render(); pollDetections();};
    if(i===0){b.classList.add("on");cam=c.id;}
    t.appendChild(b);
  });
  await backfill();
  connect(); setStream(); render(); pollDetections();
  setInterval(pollDetections,1000);
  requestAnimationFrame(draw);
}
init();
</script>
</body>
</html>
""";
}
