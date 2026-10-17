const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/pipe_render/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dot=(a,b)=>a.reduce((s,x,i)=>s+x*b[i],0),unit=a=>a.map(x=>x/Math.hypot(...a)),cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const cam=unit([-6,5,-8]),right=unit(cross(cam,[0,1,0])),up=cross(right,cam),h=.65;
const C={ink:'#193448',sub:'#566d7d',gray:'#d5dce2',blue:'#3886c8',gold:'#ae750f',fill:'#ffd16b',X:'#bc7024',Y:'#bc4e84',Z:'#3278b7',pivot:'#7954b7'};
const proj=p=>[350+146*dot(p,right),414-146*dot(p,up)];
const fmt=p=>p.map(v=>v.toFixed(2)).join(',');
function text(x,y,s,size=22,color=C.ink){return `<text x="${x}" y="${y}" font-size="${size}" fill="${color}">${s}</text>`;}
function line(a,b,color,w=1.5,dash=''){return `<path d="M${fmt(proj(a))} L${fmt(proj(b))}" stroke="${color}" stroke-width="${w}" fill="none" ${dash?`stroke-dasharray="${dash}"`:''}/>`;}
function poly(v,color,opacity,stroke){return `<polygon points="${v.map(p=>fmt(proj(p))).join(' ')}" fill="${color}" fill-opacity="${opacity}" stroke="${stroke}" stroke-width="1.4"/>`;}
function point(p,r,fill,stroke='white',w=1.5){const [x,y]=proj(p);return `<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}" stroke="${stroke}" stroke-width="${w}"/>`;}
function panel(initial){const cy=initial?h:0;
 let s=text(30,35,initial?'開始時：C = P = (0, h, 0, 0)':'原点へ移動後：C = O = (0, 0, 0, 0)',25)+text(30,73,initial?'駒の配置面の中心で回転':'4D座標の原点で回転',23,C.pivot);
 const corners=Array.from({length:8},(_,i)=>[0,1,2].map(a=>((i&(1<<a))?1:-1)+(a===1?cy:0)));
 for(let i=0;i<8;i++)for(let a=0;a<3;a++){const j=i^(1<<a);if(i<j)s+=line(corners[i],corners[j],C.blue,1.5,'5 4');}
 s+=poly([[-1,0,-1],[1,0,-1],[1,0,1],[-1,0,1]],'#aebcc8',.12,'#b8c2cc');
 s+=poly([[-1,h,-1],[1,h,-1],[1,h,1],[-1,h,1]],C.fill,.14,'#d0ac63');
 for(const [i,a] of ['X','Y','Z'].entries()){const start=[0,0,0],end=[0,0,0];start[i]=-1.16;end[i]=i===1?1.85:1.5;s+=line(start,end,C[a],1.8);const e=proj(end),v=unit([e[0]-350,e[1]-414]),n=[-v[1],v[0]];s+=`<polygon points="${fmt(e)} ${fmt([e[0]-v[0]*11+n[0]*4,e[1]-v[1]*11+n[1]*4])} ${fmt([e[0]-v[0]*11-n[0]*4,e[1]-v[1]*11-n[1]*4])}" fill="${C[a]}"/>`;s+=text(e[0]-7,e[1]+(a==='Y'?-10:25),a,25,C[a]);}
 for(const x of [-.72,0,.72])for(const z of [-.78,-.30,.30,.78])s+=point([x,h,z],6,C.fill,C.gold,1.5);
 s+=point([0,0,0],6,'white',C.ink,1.8)+point([0,h,0],5.5,'#205789','white',1.5);
 s+=point([0,cy,0],12,'none',C.pivot,3);
 const p=proj([0,h,0]),o=proj([0,0,0]);s+=text(o[0]+20,o[1]+25,initial?'O：原点':'O = C：原点',21,initial?C.ink:C.pivot)+text(p[0]+20,p[1]-15,initial?'P = C':'P',23,initial?C.pivot:C.ink)+text(p[0]-28,(p[1]+o[1])/2,'h',22,C.sub);
 s+=text(30,641,initial?'XZWの基準断面：Y = h':'XZWの基準断面：Y = 0',24,C.blue);
 s+=text(30,682,initial?'各軸へ切り替えても、中心Cを維持':'中心を移しても、駒の4D座標は固定',21,C.sub);
 return s;
}
async function run(){let s=text(30,45,'原点Oは固定し、超平面の操作中心Cを移す',31)+text(30,85,'初期位置は、ご回答の「駒配置面の中心P」。図はXYZ空間（W = 0）で比較。',22,C.sub);
 s+=`<g transform="translate(0,115)">${panel(true)}</g><g transform="translate(720,115)">${panel(false)}</g>`;
 s+=text(30,845,'紫の輪：表示範囲と回転の共通中心C　　青の点線：Cを中心とする説明用の表示範囲',21,C.sub);
 s+=text(30,886,'駒は両方とも (x, h, z, 0)。この移動だけではXYZ断面 W = 0 の式は変わりません。',21,C.sub);
 s+=text(30,927,'hと箱の寸法は説明用。画面・実装仕様の数値ではありません。',19,C.sub);
 const dest=path.join(__dirname,'pivot-offset-confirmed');fs.writeFileSync(dest+'.svg',`<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="960" viewBox="0 0 1440 960"><style>text{font-family:Meiryo,'Yu Gothic UI',sans-serif}</style><rect width="100%" height="100%" fill="white"/>${s}</svg>`);
 // Analytic slice checks, not product tests: n dot (q-C)=0.
 const centers=[[0,h,0,0],[0,0,0,0]],normals={XYZ:[0,0,0,1],XYW:[0,0,1,0],XZW:[0,1,0,0],YZW:[1,0,0,0]};
 const offsets=centers.map(c=>Object.fromEntries(Object.entries(normals).map(([name,n])=>[name,dot(n,c)])));
 if(offsets[0].XZW!==h||offsets[1].XZW!==0||offsets.some(o=>o.XYZ!==0))throw new Error('Offset mismatch');
 for(const cy of [0,h]){const rotatedNormal=[0,-1,0,0],origin=[0,cy,0,0],q=[.4,h,.8,0];const distance=dot(rotatedNormal,q.map((v,i)=>v-origin[i]));if(Math.abs(distance+(h-cy))>1e-12)throw new Error('YW rotation mismatch');}
 fs.writeFileSync(dest+'-record.json',JSON.stringify({scope:'Concept diagram only; no game code changes.',userClarification:'Initial pivot is piece-plane center P at Y=h',O:[0,0,0,0],P:[0,'h',0,0],pieces:'(x,h,z,0), fixed',centerC:'shared display-frame center and rotation pivot; initially P, origin command goes to O',presetOffsets:offsets,samples:'illustrative positions only',h:'unspecified; .65 used only for drawing',operation:'Move view frame and pivot together; preserve orientation on origin translation. Axis preset selection preserves C.',YW90:'H: Y=C_y; at C=P all sample centers remain in exact slice, at C=O no sample centers remain if h is nonzero',XW90:'H: X=0 for both centers; Y offset does not distinguish this rotation.'},null,2));
 const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--disable-background-networking','--disable-extensions']});const page=await browser.newPage({viewport:{width:1440,height:960},deviceScaleFactor:1.5});await page.goto('file:///'+dest.replaceAll('\\','/')+'.svg');await page.screenshot({path:dest+'.png'});await browser.close();console.log(dest+'.png');
}run().catch(e=>{console.error(e);process.exit(1)});
