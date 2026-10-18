const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/USER/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const output=__dirname;
const colors={ink:'#182b3c',secondary:'#526779',x:'#cc6c17',y:'#d54886',z:'#2777b5',w:'#298c86',point:'#286acb',line:'#cad3dc'};
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;');
const label=(x,y,s,size=22,color=colors.ink,extra='')=>`<text x="${x}" y="${y}" fill="${color}" font-size="${size}" ${extra}>${esc(s)}</text>`;
const line=(a,b,color,width=2,dash='',arrow=false,id='')=>`<path d="M${a[0]},${a[1]} L${b[0]},${b[1]}" fill="none" stroke="${color}" stroke-width="${width}" ${dash?`stroke-dasharray="${dash}"`:''} ${arrow?`marker-end="url(#arr-${id}-${color.slice(1)})"`:''}/>`;
function defs(id){return '<defs>'+Object.values(colors).map(c=>`<marker id="arr-${id}-${c.slice(1)}" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 Z" fill="${c}"/></marker>`).join('')+'</defs>';}
function draw(view){const id=view.toLowerCase(),hidden={XYW:'Z',XZW:'Y',YZW:'X'}[view],overlap=view==='XZW';let s=defs(id);
const O=[292,405],P=overlap?O:[292,208],right=view==='YZW'?'Z':'X',up=overlap?'Z':'Y',rc=right==='X'?colors.x:colors.z,uc=up==='Y'?colors.y:colors.z;
s+=label(28,43,view,32)+label(28,77,hidden+'を省略した座標投影（説明中の仮説）',20,colors.secondary);
// O axes use the same projected W vector in all three panels.
s+=line([68,405],[536,405],rc,2,'',true,id);
s+=line([292,469],[292,110],uc,2,'',true,id);
s+=line([419,325],[78,540],colors.w,2,'',true,id);
s+=label(544,412,right,25,rc)+label(307,116,up,25,uc)+label(47,556,'W',25,colors.w);
if(!overlap){
  // P+teW is parallel to O+teW; it is not a 4D rotation-axis definition.
  s+=line([419,128],[100,329],colors.w,2.4,'7 5',true,id);
  s+=label(30,357,'Pを通るW方向',20,colors.w);
  s+=line([330,405],[330,208],colors.secondary,1.8,'4 4');
  s+=line([323,405],[337,405],colors.secondary,1.8)+line([323,208],[337,208],colors.secondary,1.8);
  s+=label(345,310,'h',25,colors.secondary);
  s+=`<circle cx="${P[0]}" cy="${P[1]}" r="9" fill="${colors.point}" stroke="${colors.ink}" stroke-width="2"/>`;
  s+=`<circle cx="${O[0]}" cy="${O[1]}" r="9" fill="white" stroke="${colors.ink}" stroke-width="2.5"/>`;
  s+=label(348,232,'P：回転中心',22)+label(348,264,view==='XYW'?'(0, h, 0)':'(h, 0, 0)',22,colors.point);
  s+=line([301,208],[337,220],colors.line,1.4);
  s+=label(347,449,'O：盤中央',22)+label(347,482,'(0, 0, 0)',22);
  s+=label(28,604,'PとOのW座標は同じ',23)+label(28,640,'PはOからY方向へh離れる',21,colors.secondary);
}else{
  // The same projected line must not be displaced for readability.
  s+=line([419,325],[78,540],colors.w,3,'7 5');
  s+=`<circle cx="292" cy="405" r="12" fill="white" stroke="${colors.ink}" stroke-width="2.5"/><circle cx="292" cy="405" r="6" fill="${colors.point}"/>`;
  s+=line([303,396],[390,298],colors.secondary,1.6);
  s+=label(337,237,'OとPの投影',23)+label(337,269,'ともに (0, 0, 0)',21,colors.point);
  s+=label(42,180,'4Dでは別の点',24)+label(42,213,'OのY=0 ／ PのY=h',20,colors.secondary);
  s+=label(344,472,'W方向線も重なる',20,colors.w);
  s+=label(28,604,'Yを省略するため重なる',23)+label(28,640,'4DでO=Pになるわけではない',21,colors.secondary);
}
return s;}
function svg(body,w=600,h=680){return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img"><style>text{font-family:'Meiryo','Yu Gothic UI',sans-serif;font-weight:400}</style><rect width="100%" height="100%" fill="white"/>${body}</svg>`;}
async function run(){
const views=['XYW','XZW','YZW'];
for(const view of views)fs.writeFileSync(path.join(output,'origin-'+view.toLowerCase()+'.svg'),svg(draw(view)));
const head=label(30,42,'説明中に置いていた仮説：O = (0, 0, 0, 0)、P = (0, h, 0, 0)',30)+label(30,81,'座標順は (X, Y, Z, W)。h > 0 は距離の記号で、数値は未指定。W方向のずれは置いていない。',23,colors.secondary);
const tail=label(30,843,'座標投影による原点の確認図。各3D超平面で切った実断面や、回転後の表示を描いた図ではありません。',23,colors.secondary);
const body=head+views.map((v,i)=>`<g transform="translate(${i*600},112)">${draw(v)}</g>`).join('')+tail;
fs.writeFileSync(path.join(output,'origin-three-views.svg'),svg(body,1800,880));
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--disable-background-networking','--disable-extensions']});
const page=await browser.newPage({deviceScaleFactor:2});
for(const name of ['origin-xyw','origin-xzw','origin-yzw','origin-three-views']){
 const combined=name==='origin-three-views';await page.setViewportSize({width:combined?1800:600,height:combined?880:680});
 await page.goto('file:///'+path.join(output,name+'.svg').replaceAll('\\','/'));
 await page.screenshot({path:path.join(output,name+'.png')});
}
await browser.close();
const meta={scope:'Recognition diagrams only, not product design adoption or implementation.',assumption:'Y-only offset hypothesis used earlier in explanation, not a confirmed user specification.',coordinateOrder:['X','Y','Z','W'],O:[0,0,0,0],P:[0,'h',0,0],h:'positive, value not specified',projections:{XYW:{O:[0,0,0],P:[0,'h',0]},XZW:{O:[0,0,0],P:[0,0,0],omittedY:{O:0,P:'h'},projectionOverlapOnly:true},YZW:{O:[0,0,0],P:['h',0,0]}},WLines:'O+t eW and P+t eW; coincide only after XZW projection.',history:'Old computational model used P=c=0 with separate rendering anchors and did not encode this h. Previous diagonal delta line was an unspecified schematic, not evidence of W displacement.',files:['origin-xyw.png','origin-xzw.png','origin-yzw.png','origin-three-views.png'],productSourceChanges:false};
fs.writeFileSync(path.join(output,'diagram-record.json'),JSON.stringify(meta,null,2));console.log(JSON.stringify({diagrams:4,status:'rendered',scope:meta.scope}));
}
run().catch(e=>{console.error(e);process.exit(1)});
