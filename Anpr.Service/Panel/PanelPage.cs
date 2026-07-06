namespace Anpr.Service.Panel;

/// <summary>Canlı karşılaştırma paneli — tek gömülü HTML (CDN'siz; internetsiz hedef PC'de çalışır).</summary>
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
  .preview canvas { width:100%; max-height:56vh; border-radius:6px; background:#000; display:block; cursor:crosshair; }
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
  .m-green { border-left-color:var(--green); } .m-yellow { border-left-color:var(--yellow); }
  .m-red { border-left-color:var(--red); } .m-blue { border-left-color:var(--blue); }
  .hint { color:var(--dim); font-size:12px; }
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
      <span class="hint" id="roiHint">Sarı çokgen = BİZİM yazılımın okuma alanı (kameranın ayarına dokunmaz).
        Çiz: tıkla-tıkla nokta, çift-tık ile kapat.</span>
    </div>
    <canvas id="cv" width="1344" height="760"></canvas>
  </section>
  <section class="cols">
    <div class="col"><h2>📷 Hikvision (kamera ANPR) <span class="hint" id="camCount"></span></h2><div class="rows" id="camRows"></div></div>
    <div class="col"><h2>🤖 Bizim model <span class="hint" id="ourCount"></span></h2><div class="rows" id="ourRows"></div></div>
  </section>
</main>
<script>
"use strict";
const $=id=>document.getElementById(id);
let cams=[], cam=null;                 // kamera listesi + seçili
const feed={};                          // cameraId -> {cam:[], ours:[]}
let roi=[], roiDraft=null;             // kayıtlı ROI noktaları (0-1000), çizim taslağı
let natW=0, natH=0, img=new Image();

function lev(a,b){if(a===b)return 0;const m=a.length,n=b.length;if(!m)return n;if(!n)return m;
 let dp=Array.from({length:n+1},(_,j)=>j);
 for(let i=1;i<=m;i++){let prev=dp[0];dp[0]=i;
  for(let j=1;j<=n;j++){const cur=dp[j];dp[j]=Math.min(dp[j]+1,dp[j-1]+1,prev+(a[i-1]===b[j-1]?0:1));prev=cur;}}
 return dp[n];}
const compact=s=>(s||"").toUpperCase().replace(/[^A-Z0-9]/g,"");

function ensure(id){ if(!feed[id]) feed[id]={cam:[],ours:[]}; return feed[id]; }

// Eşleştirme: aynı kamera, ±30 sn; compact eşit → green, lev=1 → yellow; eşleşmeyen → red/blue.
function rematch(f){
  f.cam.forEach(r=>{r.match="red";r.pair=null;});
  f.ours.forEach(r=>{r.match="blue";r.pair=null;});
  for(const o of f.ours){
    let best=null,bestD=2;
    for(const c of f.cam){
      if(c.pair)continue;
      if(Math.abs(c.t-o.t)>30000)continue;
      const d=lev(compact(o.plate),compact(c.plate));
      if(d<bestD){bestD=d;best=c;if(d===0)break;}
    }
    if(best){o.pair=best;best.pair=o;o.match=best.match=(bestD===0?"green":"yellow");}
  }
}
function board(f){
  const g=f.ours.filter(r=>r.match==="green").length,
        y=f.ours.filter(r=>r.match==="yellow").length,
        r=f.cam.filter(x=>x.match==="red").length,
        b=f.ours.filter(x=>x.match==="blue").length,
        tot=g+y+r;
  $("cExact").textContent=g; $("cNear").textContent=y;
  $("cCamOnly").textContent=r; $("cOursOnly").textContent=b;
  $("cPct").textContent=tot?Math.round(100*g/tot)+"%":"–";
}
function row(r,side){
  const d=document.createElement("div");
  d.className="row m-"+r.match;
  const time=new Date(r.t).toLocaleTimeString("tr-TR");
  d.innerHTML=`<span class="plate">${r.plate}</span>
    <span class="meta">${time}${side==="ours"&&r.conf!=null?"<br>güven "+r.conf:""}</span>`;
  return d;
}
function render(){
  if(!cam)return;
  const f=ensure(cam);
  rematch(f); board(f);
  const cr=$("camRows"), or=$("ourRows");
  cr.replaceChildren(...f.cam.slice(-50).reverse().map(r=>row(r,"cam")));
  or.replaceChildren(...f.ours.slice(-50).reverse().map(r=>row(r,"ours")));
  $("camCount").textContent=f.cam.length+" kayıt";
  $("ourCount").textContent=f.ours.length+" kayıt";
}

// ---- SSE (otomatik yeniden bağlanır) ----
let es=null, retry=1000;
function connect(){
  es=new EventSource("/api/stream");
  es.onopen=()=>{retry=1000;$("svcBadge").innerHTML='servis: <b style="color:var(--green)">bağlı</b>';};
  es.onerror=()=>{es.close();$("svcBadge").innerHTML='servis: <b style="color:var(--red)">koptu</b>';
    setTimeout(connect,retry);retry=Math.min(retry*2,15000);};
  es.onmessage=e=>{
    const m=JSON.parse(e.data);
    if(m.source==="status"){ if(m.cameraId===cam)
      $("camBadge").innerHTML='kamera: <b style="color:var(--'+(m.cameraPollOk?'green':'red')+')">'+(m.cameraPollOk?'ok':'hata')+'</b>';
      return; }
    const f=ensure(m.cameraId);
    const rec={plate:m.plate,t:Date.parse(m.timeIso),conf:m.conf};
    (m.source==="camera"?f.cam:f.ours).push(rec);
    if(f.cam.length>200)f.cam.shift(); if(f.ours.length>200)f.ours.shift();
    if(m.cameraId===cam)render();
  };
}

// ---- Önizleme + ROI ----
const cv=$("cv"), ctx=cv.getContext("2d");
function toCanvas(p){ return [p[0]*cv.width/1000, p[1]*cv.height/1000]; }
function draw(boxes){
  ctx.clearRect(0,0,cv.width,cv.height);
  if(img.complete&&natW){ ctx.drawImage(img,0,0,cv.width,cv.height); }
  else { ctx.fillStyle="#111"; ctx.fillRect(0,0,cv.width,cv.height);
         ctx.fillStyle="#666"; ctx.font="20px sans-serif"; ctx.fillText("kare bekleniyor…",20,40); }
  // tespit kutuları (piksel → canvas)
  if(natW) for(const b of boxes||[]){
    ctx.strokeStyle="#e05252"; ctx.lineWidth=2;
    ctx.strokeRect(b.x*cv.width/natW, b.y*cv.height/natH, b.w*cv.width/natW, b.h*cv.height/natH);
  }
  // kayıtlı ROI (sarı dolgulu)
  const poly=roiDraft??roi;
  if(poly.length){
    ctx.beginPath();
    poly.forEach((p,i)=>{const[x,y]=toCanvas(p); i?ctx.lineTo(x,y):ctx.moveTo(x,y);});
    if(!roiDraft) ctx.closePath();
    ctx.strokeStyle="#e7b416"; ctx.lineWidth=2.5; ctx.setLineDash(roiDraft?[7,5]:[]); ctx.stroke(); ctx.setLineDash([]);
    if(!roiDraft){ ctx.fillStyle="rgba(231,180,22,.13)"; ctx.fill(); }
    for(const p of poly){const[x,y]=toCanvas(p); ctx.fillStyle="#e7b416"; ctx.beginPath(); ctx.arc(x,y,4,0,7); ctx.fill();}
  }
}
let lastBoxes=[];
async function refresh(){
  if(!cam)return;
  try{
    const r=await fetch(`/api/snapshot/${cam}?t=${Date.now()}`);
    if(r.ok){ const blob=await r.blob(); const url=URL.createObjectURL(blob);
      await new Promise(res=>{img.onload=()=>{natW=img.naturalWidth;natH=img.naturalHeight;res();};img.src=url;});
      URL.revokeObjectURL(url); }
    const d=await(await fetch(`/api/detections/${cam}`)).json();
    lastBoxes=d.boxes||[];
    if(!roiDraft) roi=parseRoi(d.roi);
  }catch{}
  draw(lastBoxes);
}
const parseRoi=s=>!s?[]:s.split(";").map(p=>p.split(",").map(Number)).filter(p=>p.length===2&&p.every(Number.isFinite));
const roiToStr=pts=>pts.map(p=>Math.round(p[0])+","+Math.round(p[1])).join(";");

cv.addEventListener("click",e=>{
  if(!roiDraft)return;
  const r=cv.getBoundingClientRect();
  roiDraft.push([(e.clientX-r.left)/r.width*1000,(e.clientY-r.top)/r.height*1000]);
  draw(lastBoxes);
});
cv.addEventListener("dblclick",e=>{
  if(!roiDraft||roiDraft.length<3)return;
  e.preventDefault();
  $("roiSave").disabled=false;
});
$("roiDraw").onclick=()=>{roiDraft=[];$("roiSave").disabled=true;$("roiCancel").disabled=false;draw(lastBoxes);};
$("roiCancel").onclick=()=>{roiDraft=null;$("roiSave").disabled=true;$("roiCancel").disabled=true;draw(lastBoxes);};
$("roiSave").onclick=async()=>{
  await fetch(`/api/roi/${cam}`,{method:"PUT",headers:{"Content-Type":"application/json"},
    body:JSON.stringify({roi:roiToStr(roiDraft)})});
  roi=roiDraft; roiDraft=null;
  $("roiSave").disabled=true;$("roiCancel").disabled=true; draw(lastBoxes);
};
$("roiClear").onclick=async()=>{
  if(!confirm("Bu kameranın ROI'si temizlensin mi? (Tüm kare okunur)"))return;
  await fetch(`/api/roi/${cam}`,{method:"PUT",headers:{"Content-Type":"application/json"},body:JSON.stringify({roi:""})});
  roi=[];roiDraft=null;draw(lastBoxes);
};

// ---- kamera sekmeleri ----
async function init(){
  const h=await(await fetch("/health")).json();
  cams=h.cameras.map(c=>({id:c.id,name:c.name}));
  const t=$("tabs");
  cams.forEach((c,i)=>{
    const b=document.createElement("button");
    b.textContent=c.name||c.id;
    b.onclick=()=>{cam=c.id;[...t.children].forEach(x=>x.classList.remove("on"));b.classList.add("on");
                   roiDraft=null;$("roiSave").disabled=true;$("roiCancel").disabled=true;render();refresh();};
    if(i===0){b.classList.add("on");cam=c.id;}
    t.appendChild(b);
  });
  connect(); render(); refresh();
  setInterval(refresh,1000);
}
init();
</script>
</body>
</html>
""";
}
