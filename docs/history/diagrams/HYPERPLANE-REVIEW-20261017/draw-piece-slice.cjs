const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/pipe_render/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dot=(a,b)=>a.reduce((s,x,i)=>s+x*b[i],0),unit=a=>a.map(x=>x/Math.hypot(...a)),cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const cam=unit([-6,5,-8]),right=unit(cross(cam,[0,1,0])),up=cross(right,cam),h=.62;
const C={ink:'#183146',sub:'#576d7c',gray:'#cbd5dc',blue:'#3283cc',gold:'#b67500',fill:'#ffc65c',X:'#c9721d',Y:'#ca4c85',Z:'#2a6eb4'};
const project=p=>[330+166*dot(p,right),385-166*dot(p,up)];
const fmt=p=>p.map(v=>v.toFixed(2)).join(',');
function text(x,y,s,size=22,color=C.ink,more=''){return `<text x="${x}" y="${y}" font-size="${size}" fill="${color}" ${more}>${s}</text>`;}
function line(a,b,color,w=1.5,dash=''){return `<path d="M${fmt(project(a))} L${fmt(project(b))}" stroke="${color}" stroke-width="${w}" fill="none" ${dash?`stroke-dasharray="${dash}"`:''}/>`;}
function polygon(v,color,opacity,stroke){return `<polygon points="${v.map(p=>fmt(project(p))).join(' ')}" fill="${color}" fill-opacity="${opacity}" stroke="${stroke}" stroke-width="1.6"/>`;}
function point(p,r,fill,stroke='white',w=1.5){const [x,y]=project(p);return `<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}" stroke="${stroke}" stroke-width="${w}"/>`;}
const samples=[-.72,0,.72].flatMap(x=>[-.78,-.30,.30,.78].map(z=>({q:[x,h,z,0]})));
function panel(after){let s=text(30,35,after?'② 90°後：超平面 X = 0':'① 初期：超平面 W = 0',27)+text(30,72,after?'X ≠ 0 の駒を断面から除外':'駒は (x, h, z, 0) に固定',22,C.sub);
 const cube=Array.from({length:8},(_,i)=>[0,1,2].map(a=>(i&(1<<a))?1:-1));
 for(let i=0;i<8;i++)for(let a=0;a<3;a++){const j=i^(1<<a);if(i<j)s+=line(cube[i],cube[j],C.gray,1.1);}
 // The base represents the board XZ plane at Y=0, not a second set of pieces.
 s+=polygon([[-1,0,-1],[1,0,-1],[1,0,1],[-1,0,1]],'#c3cbd1',.12,'#a8b6c2');
 if(after){s+=polygon([[0,-1,-1],[0,1,-1],[0,1,1],[0,-1,1]],'#64b4f4',.22,C.blue);for(const t of [-.5,0,.5]){s+=line([0,t,-1],[0,t,1],C.blue,.8);s+=line([0,-1,t],[0,1,t],C.blue,.8);}}
 else{s+=polygon([[-1,h,-1],[1,h,-1],[1,h,1],[-1,h,1]],C.fill,.10,'#d5b36d');}
 for(const [i,a] of ['X','Y','Z'].entries()){const start=[0,0,0],end=[0,0,0];start[i]=-1.16;end[i]=1.42;s+=line(start,end,C[a],2);const e=project(end),v=unit([e[0]-330,e[1]-385]),n=[-v[1],v[0]];s+=`<polygon points="${fmt(e)} ${fmt([e[0]-v[0]*11+n[0]*4,e[1]-v[1]*11+n[1]*4])} ${fmt([e[0]-v[0]*11-n[0]*4,e[1]-v[1]*11-n[1]*4])}" fill="${C[a]}"/>`;s+=text(e[0]-7,e[1]+(a==='Y'?-10:25),a,25,C[a]);}
 if(after)s+=line([0,h,-1.03],[0,h,1.03],C.gold,3);
 for(const {q} of samples){if(after&&q[0]!==0)continue;s+=point(q.slice(0,3),7.5,C.fill,C.gold,1.8);}
 s+=point([0,0,0],6.3,'white',C.ink,2);s+=point([0,h,0],5.3,'#1a4c86','white',1.5);
 const p=project([0,h,0]),o=project([0,0,0]);s+=text(o[0]+14,o[1]+25,'O',23)+text(p[0]+14,p[1]-11,'P',23)+text(p[0]-26,(p[1]+o[1])/2,'h',21,C.sub);
 s+=text(30,650,after?'残る駒： (0, h, z, 0)':'駒の面：Y = h、W = 0',25,after?C.gold:C.ink);
 s+=text(30,688,after?'高さは h のまま。Pを通るZ方向の列。':'盤の面：Y = 0、W = 0（XZ平面）',21,C.sub);
 return s;}
async function run(){
 let s=text(32,45,'XYZ空間での駒の位置 ― 駒は固定し、断面だけを回す',30)+text(32,82,'O = (0, 0, 0, 0)　 P = (0, h, 0, 0)　 ZY平面を固定し、XW平面で90°回転',21,C.sub);
 s+=`<g transform="translate(0,110)">${panel(false)}</g><g transform="translate(690,110)">${panel(true)}</g>`;
 s+=text(32,846,'金色の点：説明用の駒位置。P自体に駒があるとは限りません。hの数値・実際の駒数は指定していません。',20,C.sub);
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="1380" height="880" viewBox="0 0 1380 880"><style>text{font-family:Meiryo,'Yu Gothic UI',sans-serif}</style><rect width="100%" height="100%" fill="white"/>${s}</svg>`;
 const dest=path.join(__dirname,'piece-slice-XYZ');fs.writeFileSync(dest+'.svg',svg);
 for(const {q} of samples){if(q[1]!==h||q[3]!==0)throw new Error('Sample on wrong piece plane');if(q[0]===0&&q[1]===0)throw new Error('Lost height');}
 const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--disable-background-networking','--disable-extensions']});
 const page=await browser.newPage({viewport:{width:1380,height:880},deviceScaleFactor:1.5});await page.goto('file:///'+dest.replaceAll('\\','/')+'.svg');await page.screenshot({path:dest+'.png'});await browser.close();
 fs.writeFileSync(dest+'-record.json',JSON.stringify({scope:'Explanation diagram only; no game source edits.',pieces:'q=(x,h,z,0), fixed',sliceBefore:'W=0',sliceAfter:'X=0',survivors:'q=(0,h,z,0)',h:'symbolic; .62 is drawing placement only',samples:'illustrative, not shogi initial layout',display:'XYZ world reference with fixed O and P; no recentering'},null,2));
 console.log(dest+'.png');
}run().catch(e=>{console.error(e);process.exit(1)});
