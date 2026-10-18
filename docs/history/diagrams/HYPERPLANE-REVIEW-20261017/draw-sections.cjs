const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/USER/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dir=__dirname;
const C={ink:'#14293b',muted:'#536c80',ref:'#b7c6d2',blue:'#247bcc',fill:'#5ca9ee',X:'#c9721d',Y:'#ca4c85',Z:'#2a6eb4',W:'#258c83'};
const dot=(a,b)=>a.reduce((s,x,i)=>s+x*b[i],0);
const add=(a,b)=>a.map((x,i)=>x+b[i]);
const mul=(a,v)=>a.map(x=>x*v);
const norm=a=>Math.sqrt(dot(a,a));
const unit=a=>mul(a,1/norm(a));
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const camera=unit([-6,5,-8]),right=unit(cross(camera,[0,1,0])),up=cross(right,camera);
const axes=['X','Y','Z','W'],e=axes.map((_,i)=>axes.map((_,j)=>i===j?1:0));
const refs=[{name:'XYZ',hidden:'W',slots:['X','Y','Z']},{name:'XYW',hidden:'Z',slots:['X','Y','W']},{name:'XZW',hidden:'Y',slots:['X','W','Z']},{name:'YZW',hidden:'X',slots:['W','Y','Z']}];
const normal0=[0,0,0,1];
function RXW(v){return [-v[3],v[1],v[2],v[0]];}
const normal90=RXW(normal0);
const states=[{name:'initial',title:'初期状態',eq:'W = 0',n:normal0},{name:'rotated',title:'ZY平面を固定して90°回転した後',eq:'X = 0',n:normal90}];
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;');
function text(x,y,s,size=22,color=C.ink,extra=''){return `<text x="${x}" y="${y}" fill="${color}" font-size="${size}" ${extra}>${esc(s)}</text>`;}
function project(p){return [310+118*dot(p,right),267-118*dot(p,up)];}
function coords(p){return p.map(v=>Number(v.toFixed(3))).join(',');}
function line(a,b,color,width=1.7,dash=''){const p=project(a),q=project(b);return `<path d="M${coords(p)} L${coords(q)}" fill="none" stroke="${color}" stroke-width="${width}" ${dash?`stroke-dasharray="${dash}"`:''}/>`;}
function poly(v,fill,opacity,stroke,width=1.5){return `<polygon points="${v.map(p=>coords(project(p))).join(' ')}" fill="${fill}" fill-opacity="${opacity}" stroke="${stroke}" stroke-width="${width}"/>`;}
function planeQuad(axis,value=0){const other=[0,1,2].filter(a=>a!==axis);return [[-1,-1],[1,-1],[1,1],[-1,1]].map(v=>{const p=[0,0,0];p[axis]=value;p[other[0]]=v[0];p[other[1]]=v[1];return p;});}
const cube=Array.from({length:8},(_,i)=>[0,1,2].map(a=>(i&(1<<a))?1:-1));
const edges=[];for(let i=0;i<8;i++)for(let a=0;a<3;a++){const j=i^(1<<a);if(i<j)edges.push([cube[i],cube[j]]);}
const cubeFaces=[];for(let a=0;a<3;a++)for(const sign of [-1,1])cubeFaces.push(planeQuad(a,sign));
function arrow(origin,end,color,id){let s=line(origin,end,color,2.1);const a=project(origin),b=project(end),d=unit([b[0]-a[0],b[1]-a[1]]),perp=[-d[1],d[0]],base=add(b,mul(d,-11));const p=add(base,mul(perp,4.5)),q=add(base,mul(perp,-4.5));return s+`<polygon points="${coords(b)} ${coords(p)} ${coords(q)}" fill="${color}"/>`;}
const records=[];
function panel(state,ref){const restricted=ref.slots.map(a=>state.n[axes.indexOf(a)]),zero=restricted.every(x=>x===0),normalAxis=restricted.findIndex(x=>x!==0);
const common=zero?ref.name:ref.slots.filter((_,i)=>restricted[i]===0).sort((a,b)=>axes.indexOf(a)-axes.indexOf(b)).join('');
const r={state:state.name,reference:ref.name,referenceEquation:ref.hidden+'=0',restrictedNormal:restricted,dimension:zero?3:2,commonAxes:common};records.push(r);
let s=text(24,36,ref.name+' 空間',29)+text(24,70,ref.hidden+' = 0 の基準空間',20,C.muted);
if(zero){const ordered=cubeFaces.slice().sort((a,b)=>dot(a.reduce(add,[0,0,0]),camera)-dot(b.reduce(add,[0,0,0]),camera));for(const face of ordered)s+=poly(face,C.fill,.09,C.blue,1.25);for(const[a,b]of edges)s+=line(a,b,C.blue,1.8);}
else{for(const[a,b]of edges)s+=line(a,b,C.ref,1.3);s+=poly(planeQuad(normalAxis),C.fill,.24,C.blue,2.4);const free=[0,1,2].filter(a=>a!==normalAxis);for(const t of [-.5,0,.5])for(const a of free){const b=free.find(i=>i!==a),p=[0,0,0],q=[0,0,0];p[a]=q[a]=t;p[b]=-1;q[b]=1;s+=line(p,q,C.blue,.9);}}
// All four reference views use the same 3D camera. +Z is always in the depth slot.
for(let i=0;i<3;i++){const a=ref.slots[i],start=[0,0,0],end=[0,0,0];start[i]=-1.18;end[i]=1.65;s+=line(start,[0,0,0],C[a],1.3);s+=arrow([0,0,0],end,C[a]);const p=project(mul(end,1.08));s+=text(p[0]-8,p[1]+7,a,25,C[a]);}
const o=project([0,0,0]);s+=`<circle cx="${o[0]}" cy="${o[1]}" r="6.7" fill="white" stroke="${C.ink}" stroke-width="2.1"/>`;
// hDraw only positions the label in a schematic with no numerical tick scale.
if(ref.hidden!=='Y'){const p3=ref.slots.map(a=>a==='Y'?.63:0),p=project(p3);s+=`<circle cx="${p[0]}" cy="${p[1]}" r="6.5" fill="#185cba" stroke="white" stroke-width="1.6"/>`+text(p[0]+13,p[1]-7,'P',23)+text(o[0]+13,o[1]+25,'O',23);s+=text(p[0]-28,(p[1]+o[1])/2+7,'h',20,C.muted);}
else{s+=`<circle cx="${o[0]}" cy="${o[1]}" r="3" fill="#185cba"/>`+text(o[0]+13,o[1]+25,'O（投影P⊥）',20);}
s+=text(24,483,zero?'青：'+ref.name+'空間全体と一致（3次元）':'青：'+common+'平面（2次元の共通部分）',23,C.blue);
s+=text(24,522,ref.hidden==='Y'?'Pの実点はY=h。投影P⊥だけがOと重なる。':zero?'立方体は、無限に広い3次元超平面の一部。':'外枠：基準空間の表示範囲。青い面はその内部。',18,C.muted);
return s;
}
function svg(body,w,h){return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><style>text{font-family:Meiryo,'Yu Gothic UI',sans-serif;font-weight:400}</style><rect width="100%" height="100%" fill="white"/>${body}</svg>`;}
function page(state){let s=text(28,44,state.title+'：主超平面 H は '+state.eq,31)+text(28,84,'O=(0,0,0,0)、P=(0,h,0,0)。各図はOを通る基準空間とHの共通部分を表示。',21,C.muted);
refs.forEach((ref,i)=>{s+=`<g transform="translate(${(i%2)*620},${112+Math.floor(i/2)*555})">${panel(state,ref)}</g>`;});
s+=text(28,1255,'青い図形と灰色の箱は説明用の切取り。箱の大きさ・hとの比率は設定値ではありません。',20,C.muted);
return svg(s,1240,1290);}
async function run(){
for(const state of states){fs.writeFileSync(path.join(dir,state.name+'.svg'),page(state));for(const ref of refs)fs.writeFileSync(path.join(dir,state.name+'-'+ref.name+'.svg'),svg(panel(state,ref),620,555));}
// Deterministic checks on the plotted mathematical states (not product tests).
const expected={initial:{XYZ:'XYZ',XYW:'XY',XZW:'XZ',YZW:'YZ'},rotated:{XYZ:'YZ',XYW:'YW',XZW:'ZW',YZW:'YZW'}};
for(const r of records)if(r.commonAxes!==expected[r.state][r.reference])throw new Error('Wrong intersection: '+JSON.stringify(r));
for(const h of [-7,.2,5]){const p=[0,h,0,0];if(RXW(p).some((v,i)=>v!==p[i]))throw new Error('Pivot moved');}
if(dot(RXW([1,0,0,0]),normal90)!==0||normal90[0]!==-1)throw new Error('Rotated plane incorrect');
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--disable-background-networking','--disable-extensions']});const tab=await browser.newPage({deviceScaleFactor:1.5});
for(const name of ['initial','rotated',...states.flatMap(st=>refs.map(r=>st.name+'-'+r.name))]){const full=name==='initial'||name==='rotated';await tab.setViewportSize({width:full?1240:620,height:full?1290:555});await tab.goto('file:///'+path.join(dir,name+'.svg').replaceAll('\\','/'));await tab.screenshot({path:path.join(dir,name+'.png')});}
await browser.close();
const unique=records.slice(0,0);for(const r of records)if(!unique.some(x=>x.state===r.state&&x.reference===r.reference))unique.push(r);
fs.writeFileSync(path.join(dir,'diagram-record.json'),JSON.stringify({scope:'Mathematical recognition drawings only. No product source edit or adopted UI behavior.',assumption:{O:[0,0,0,0],P:[0,'h',0,0],h:'positive, unspecified',initialHyperplane:'W=0'},rotation:{interpretation:'Fix affine ZY plane through P; rotate its orthogonal XW plane +90 degrees.',positive:'X -> +W, W -> -X',fixedPlane:'X=0 and W=0; contains O and P',result:'X=0',withinZYWouldBeDifferent:true},references:refs,referenceCenter:'O',blue:'Intersection of H with each reference 3-space, clipped by a schematic box; not a general projection of H.',camera:{position:[-6,5,-8],right,up},hDrawOnly:.63,box:'[-1,1]^3 used only for the drawing; no numeric scale or ratio to h is adopted.',results:unique,checks:'Eight intersection spaces, fixed P at varied h, and transformed normal verified.'},null,2));
console.log(JSON.stringify({images:10,uniqueStates:unique.length,status:'rendered and algebra checked',scope:'diagrams only'}));}
run().catch(e=>{console.error(e);process.exit(1)});
