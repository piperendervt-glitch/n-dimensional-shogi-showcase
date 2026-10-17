using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg1App : MonoBehaviour
    {
        EnvironmentRig rig;CfgPreviewRenderer previewRenderer;SliceAnimator slice;
        SettingsDraft draft;PreviewSession preview;PreviewPreset preset;QueryResult result;
        ValidationReport validation;CancellationTokenSource queryCancel;Task<QueryResult> pendingQuery;readonly LatestQueryGate queryGate=new LatestQueryGate();long requestId;
        Canvas canvas;RectTransform root,menu,settings,detailHost,pieceHost,modal,previewRect;
        ScrollRect detailScroll,pieceScroll;readonly Dictionary<string,float> scrolls=new Dictionary<string,float>();
        Text headerStatus,counts,previewCounts,statusText,sliceText;RawImage previewImage;RenderTexture previewTexture;
        readonly List<CfgMiniView> minis=new List<CfgMiniView>();
        readonly Dictionary<string,Button> actions=new Dictionary<string,Button>();
        string category="駒の動き設定",selectedDefinition="king-step",selectedPiece="subject",tool="選択",notice="";
        string runRoot,runtimeRoot,savePath,lastSavedTrial="";Side placementSide=Side.A;QN selectedCell;
        bool opened,geometryDirty=true,quitting;string historyOwner="draft";
        DraftData savedDraftBaseline;bool savedBaselinePersisted;string detailContext="",coordinateRaw="0,0,0,0";bool coordinateValid=true;
        float yaw=35,pitch=29,distance=.72f;Vector3 pan;Vector2 pointerStart,lastPointer;long pointerRevision;int pointerButton=-1;bool pointerMoved;
        string[] categories={"ルールプリセット","盤・初期配置","駒の動き設定","対局ルール","CPU設定","表示・操作","保存・読み込み"};
        readonly List<string> eventTrace=new List<string>();
        readonly InputOwnership ownership=new InputOwnership();
        long pointerTrial,pointerDraft,cameraRevision,pointerCamera;float startYaw,startPitch,startDistance;Vector3 startPan;
        internal SliceAnimator Slice=>slice;internal InputOwnership Ownership=>ownership;internal float UiScale=>canvas.scaleFactor;
        internal void InvalidateGeometry()=>geometryDirty=true;
        internal void Trace(string name,string value)=>Record(name,value);
        [Serializable] sealed class RunConfig {public string runRoot;}
        void Start()
        {
            try
            {
                var args=System.Environment.GetCommandLineArgs();var resource=Resources.Load<TextAsset>("cfg1-run");
                runRoot=Arg("--cfg1-run-root",resource==null?"":JsonUtility.FromJson<RunConfig>(resource.text).runRoot);
                if(string.IsNullOrWhiteSpace(runRoot)||!Directory.Exists(runRoot))throw new InvalidOperationException("今回runの保存先が指定されていません。--cfg1-run-root を指定してください。");
                runtimeRoot=Path.Combine(runRoot,"runtime-data");Directory.CreateDirectory(runtimeRoot);
                savePath=Path.Combine(runtimeRoot,"preview-preset.xml");
                Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
                var fonts=Font.GetOSInstalledFontNames();string fontName=new[]{"Yu Gothic UI","Yu Gothic","Meiryo","MS Gothic"}.FirstOrDefault(n=>fonts.Contains(n));
                if(fontName==null)throw new InvalidOperationException("既存の日本語OSフォントが見つかりません。新規取得・フォントコピーは行いません。");
                CfgUi.Font=Font.CreateDynamicFontFromOSFont(fontName,18);
                if(CfgUi.Font==null)throw new InvalidOperationException("日本語OSフォントの動的生成に失敗しました。");
                rig=ShowcaseEnvironment.Build();rig.Debug.enabled=false; // CFG1 owns input; Legacy diagnostics keep their original entry.
                previewRenderer=new CfgPreviewRenderer(rig);slice=new SliceAnimator();draft=SettingsDraft.CreateExperiment();savedDraftBaseline=draft.Current;preview=PreviewSession.CreateExperiment();
                CreateCanvas();CreateMenu();CreateSettings();ShowMenu();QueueQuery();
                Record("Started", "font="+fontName+"; "+Screen.width+"x"+Screen.height+"; dpi="+Screen.dpi+"; no match controller");
                File.WriteAllText(Path.Combine(runtimeRoot,"startup.json"),"{\"font\":\""+fontName+"\",\"width\":"+Screen.width+",\"height\":"+Screen.height+",\"reportedDpi\":"+Screen.dpi.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"matchConnected\":false}");
                if(args.Contains("--cfg1-smoke"))StartCoroutine(Smoke());
            }
            catch(Exception e){Debug.LogException(e);notice=e.Message;enabled=false;}
        }
        string Arg(string key,string fallback){var a=System.Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        void Record(string name,string data="")
        {var row=DateTime.UtcNow.ToString("o")+"\t"+name+"\t"+data.Replace('\n',' ');eventTrace.Add(row);Debug.Log("CFG1 "+name+" "+data);}
        void CreateCanvas()
        {
            var go=new GameObject("CFG1 Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1536,1024);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            root=(RectTransform)go.transform;
            var es=new GameObject("CFG1 EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            es.GetComponent<StandaloneInputModule>().forceModuleActive=true;
        }
        Button Button(Transform parent,string id,string title,float x,float y,float w,float h,Action action,bool accent=false)
        {var b=CfgUi.Button(parent,title,x,y,w,h,()=>{Record("UI",id);try{action();}catch(Exception e){Notice(e.Message);Debug.LogException(e);}},accent);actions[id]=b;return b;}
        void CreateMenu()
        {
            menu=CfgUi.Panel(root,"Start menu",495,180,550,650,new Color(.04f,.06f,.09f,.95f));
            CfgUi.Label(menu,"四 次 元 将 棋",35,34,480,65,38);CfgUi.Label(menu,"4D 操作・設定 Windows 試作",35,108,480,35,20);
            string[] labels={"プレイヤー対プレイヤー","プレイヤー対CPU","CPU対CPU","オプション","ゲーム終了"};
            for(int i=0;i<labels.Length;i++){int index=i;Button(menu,"menu-"+i,labels[i],35,172+i*75,480,57,()=>{if(index<3)Notice(PreviewPreset.ApplyUnavailableReason);else if(index==3)OpenSettings();else RequestQuit();},i==3);}
            CfgUi.Label(menu,"対局3モード：CFG-2待ち・未接続\nオプションで編集と独立した試し盤を利用できます。",35,565,480,65,16);
        }
        void CreateSettings()
        {
            settings=CfgUi.Panel(root,"Settings R0-R7",8,8,1520,1008,new Color(.025f,.04f,.06f,.91f));
            CfgUi.Label(settings,"四次元将棋",16,10,300,40,26);CfgUi.Label(settings,"設定（オプション）",550,10,600,40,29);
            headerStatus=CfgUi.Label(settings,"",16,53,1480,35,16);
            var nav=CfgUi.Panel(settings,"R1 Categories",8,96,190,824);
            for(int i=0;i<categories.Length;i++){string c=categories[i];Button(nav,"category-"+i,c,8,15+i*70,174,57,()=>SelectCategory(c),c==category);}
            CfgUi.Label(nav,"N=4 / 試験用\n製品ルール未採用\n\n前方：論理Z\n陣営A +Z / B −Z\n\nL・対局・CPU\nCFG-2以降",12,535,166,230,17);
            pieceHost=CfgUi.Panel(settings,"R2 Pieces",206,96,180,824);
            var center=CfgUi.Panel(settings,"R3-R5 Preview",394,96,734,824);
            CfgUi.Label(center,"駒の動きプレビュー  ｜  独立試し盤",12,10,706,32,22);
            sliceText=CfgUi.Label(center,"",12,48,706,44,17);
            previewRect=CfgUi.Rect(center,"R4 Exact section",10,98,544,540);previewImage=previewRect.gameObject.AddComponent<RawImage>();previewImage.color=Color.white;
            var pointer=previewRect.gameObject.AddComponent<CfgPointerSurface>();pointer.Down=e=>RunPointer(()=>PreviewDown(e));pointer.Drag=e=>RunPointer(()=>PreviewDrag(e));pointer.Up=e=>RunPointer(()=>PreviewUp(e));pointer.Scroll=e=>RunPointer(()=>PreviewScroll(e));
            var gizmo=CfgUi.Panel(center,"Main XYZW Gizmo",564,98,160,85);
            CfgUi.Label(gizmo,"主4D",4,2,48,20,13);
            string[] axisNames={"W","Z","Y","X"};var tips=new[]{new Vector2(80,70),new Vector2(20,60),new Vector2(80,17),new Vector2(139,43)};
            var axisColors=new[]{new Color(.73f,.45f,1),new Color(.3f,.63f,1),new Color(.5f,.86f,.48f),new Color(1,.45f,.36f)};
            for(int i=0;i<4;i++){string axis=axisNames[i];CfgUi.Line(gizmo,new Vector2(80,43),tips[i],axisColors[i],2);var b=Button(gizmo,"axis-"+axis,axis,tips[i].x-14,tips[i].y-13,28,26,()=>RequestView(axis));var label=b.GetComponentInChildren<Text>();label.color=axisColors[i];label.fontSize=14;}
            for(int i=0;i<4;i++)
            {
                var r=CfgUi.Panel(center,"Hyperplane "+i,564,191+i*111,160,105);
                var view=r.gameObject.AddComponent<CfgMiniView>();view.Initialize(this,(ReferenceView)i);minis.Add(view);
            }
            previewCounts=CfgUi.Label(center,"G / P：未評価 ｜ L：未評価",12,646,710,48,17);
            CfgUi.Label(center,"右：3D回転 / 中：パン / ホイール：ズーム / 小窓の軸ドラッグ：4D移動",12,697,710,23,14);
            Button(center,"tool-select","選択 / P試し移動",12,722,164,39,()=>SetTool("選択"));
            Button(center,"tool-place","駒を置く",183,722,112,39,()=>SetTool("配置"));Button(center,"tool-erase","駒を消す",302,722,112,39,()=>SetTool("消去"));
            Button(center,"trial-clear","試し盤クリア",421,722,143,39,()=>{preview.Clear();historyOwner="trial";AfterTrial();});
            Button(center,"trial-undo","試し盤Undo",570,722,151,39,()=>{preview.Undo();AfterTrial();});
            Button(center,"side","配置陣営 A / B",12,770,164,38,()=>{placementSide=placementSide==Side.A?Side.B:Side.A;Notice("配置陣営 "+placementSide);});
            Button(center,"grid","グリッド表示",183,770,143,38,()=>{previewRenderer.Grid=!previewRenderer.Grid;geometryDirty=true;});
            Button(center,"candidates","候補表示",333,770,126,38,()=>{previewRenderer.Candidates=!previewRenderer.Candidates;geometryDirty=true;});
            Button(center,"trial-redo","試し盤Redo",466,770,126,38,()=>{preview.Redo();AfterTrial();});
            Button(center,"trial-reset","試験配置",599,770,122,38,()=>{preview.Place(new TrialPiece("subject",selectedDefinition,Side.A,default));selectedPiece="subject";AfterTrial();});
            detailHost=CfgUi.Panel(settings,"R6 Details",1136,96,376,824);
            statusText=CfgUi.Label(settings,"",16,925,1490,26,16,new Color(1,.82f,.48f));
            Button(settings,"back","メインメニューに戻る",16,960,280,38,RequestBack);
            Button(settings,"save","設定を保存",516,960,250,38,()=>Save(false));
            Button(settings,"save-as","別名で保存",778,960,250,38,()=>SaveAsDialog());
            Button(settings,"apply","設定を適用して戻る",1040,960,454,38,()=>Notice(PreviewPreset.ApplyUnavailableReason),true);
            BuildPieces();BuildDetails();
        }
        void ShowMenu()
        {opened=false;settings.gameObject.SetActive(false);menu.gameObject.SetActive(true);if(rig!=null){rig.Camera.targetTexture=null;rig.Camera.transform.position=new Vector3(.65f,1.4f,-.84f);rig.Camera.transform.LookAt(new Vector3(0,.96f,0));}Record("Menu","no CPU, no match state");}
        void OpenSettings()
        {opened=true;menu.gameObject.SetActive(false);settings.gameObject.SetActive(true);if(previewTexture==null){previewTexture=new RenderTexture(1088,1080,24){name="CFG1 preview"};previewTexture.Create();previewImage.texture=previewTexture;}rig.Camera.targetTexture=previewTexture;UpdateCamera();geometryDirty=true;QueueQuery();Record("OptionsOpened");}
        void SelectCategory(string next)
        {SaveScroll();category=next;for(int i=0;i<categories.Length;i++)actions["category-"+i].GetComponent<Image>().color=categories[i]==next?CfgUi.Blue:new Color(.10f,.14f,.20f,.98f);BuildDetails();Notice("カテゴリ："+next);}
        void SaveScroll(){if(detailScroll!=null)scrolls[category+"/"+selectedDefinition]=detailScroll.verticalNormalizedPosition;}
        void BuildPieces()
        {
            float previous=pieceScroll==null?1:pieceScroll.verticalNormalizedPosition;CfgUi.Clear(pieceHost);CfgUi.Label(pieceHost,"駒の一覧",10,10,160,32,21);
            var definitions=draft.Current.Definitions;pieceScroll=CfgUi.Scroll(pieceHost,"Piece list",6,52,168,698,out var content,definitions.Count*58);
            for(int i=0;i<definitions.Count;i++){var d=definitions[i];Button(content,"piece-"+d.Id,d.Name,4,i*58,150,52,()=>SelectDefinition(d.Id),d.Id==selectedDefinition);}
            pieceScroll.verticalNormalizedPosition=previous;
            Button(pieceHost,"previous-piece","前の駒",7,760,78,42,()=>SelectOffset(-1));Button(pieceHost,"next-piece","次の駒",92,760,78,42,()=>SelectOffset(1));
        }
        void SelectOffset(int step){var ds=draft.Current.Definitions;int i=ds.FindIndex(d=>d.Id==selectedDefinition);SelectDefinition(ds[(i+step+ds.Count)%ds.Count].Id);}
        void SelectDefinition(string id)
        {SaveScroll();selectedDefinition=id;if(preview.Find(selectedPiece)!=null)preview.ReplaceDefinition(selectedPiece,id);BuildPieces();BuildDetails();QueueQuery();geometryDirty=true;Record("SelectDefinition",id);}
        void Edit(Action<RawPieceDefinition> action)
        {draft.Edit(d=>action(d.Definitions.Single(p=>p.Id==selectedDefinition)));historyOwner="draft";QueueQuery();BuildDetails();UpdateHeader();}
        void EditRaw(Action<RawPieceDefinition> action)
        {draft.Edit(d=>action(d.Definitions.Single(p=>p.Id==selectedDefinition)));historyOwner="draft";QueueQuery();UpdateHeader();}
        void BuildDetails()
        {
            float previous=detailScroll==null?1:detailScroll.verticalNormalizedPosition;string key=category+"/"+selectedDefinition;if(detailContext!=key){previous=scrolls.TryGetValue(key,out float remembered)?remembered:1;}detailContext=key;
            CfgUi.Clear(detailHost);CfgUi.Label(detailHost,category,12,10,348,35,22);detailScroll=CfgUi.Scroll(detailHost,"Independent detail scroll",8,53,360,756,out var c,1180);
            if(category=="駒の動き設定")BuildPieceDetails(c);
            else if(category=="保存・読み込み"||category=="ルールプリセット")BuildSaveDetails(c);
            else if(category=="対局ルール")
            {CfgUi.Label(c,"試し盤の走り遮蔽（定義・版）",6,0,325,40,19);BuildOcclusion(c,55);CfgUi.Label(c,"王手回避・完全合法性・終局・成り・打ち・反復：CFG-2以降、未評価。\n\nこの選択は独立試し盤のH(1,1) / H(2,2)だけへ反映します。一歩は終点占有だけを評価します。",6,168,325,280,18);}
            else if(category=="表示・操作")
            {CfgUi.Label(c,"4D：主W/Z/Y/Xで基準断面へ。\n小窓の軸ドラッグ：世界4D軸に平行移動。\n\n3D：右ドラッグ回転／中ドラッグパン／ホイールズーム。\n\n自由角度の直接編集は後段。中間姿勢は実切断します。\n\n空中中心と木製盤は独立。W復帰は位置を保持。\n\n実装選択：窓半幅2.5セル、90度区間0.45秒。操作感は人間確認待ち。",6,0,325,500,18);}
            else if(category=="盤・初期配置")
            {CfgUi.Label(c,"本番初期配置の編集：未接続\nCFG-2の製品ルール採用待ちです。\n\n独立試し盤：N=4、各軸−4〜4\n前方：論理Z（Core F）\n陣営Aは+、Bは−。\n\n試し配置は本番初期配置へ転用しません。全4Dの駒・障害物を照会します。",6,0,325,420,18);BuildCoordinateControls(c,440);}
            else CfgUi.Label(c,"CPU設定：未接続\n汎用CPU・予算・評価・対局方式はCFG-2以降です。\n\nオプションや試し盤の表示だけでCPUを起動しません。",6,0,325,300,19);
            detailScroll.verticalNormalizedPosition=previous;
        }
        void BuildPieceDetails(RectTransform c)
        {
            var d=draft.Current.Definitions.Single(x=>x.Id==selectedDefinition);bool supported=d.Kind=="step/1"||d.Kind=="h11/1"||d.Kind=="h22/1";
            CfgUi.Label(c,d.Name,6,0,325,55,21);CfgUi.Label(c,"ID: "+d.Id+"\n方式: "+d.Kind,6,61,325,48,15);
            if(!supported){CfgUi.Label(c,"既存成分／成り・跳び：読取・未対応\n共通G/Pを別の動きへ置換しません。\nL（完全合法手）：未評価",6,130,325,150,19);return;}
            CfgUi.Label(c,d.Kind=="step/1"?"一歩：各軸の変位 −1 / 0 / +1":"走り：最大座標変位 m（1〜8）",6,120,325,43,17);
            if(d.Kind=="step/1")
            {
                Button(c,"pattern-axis","軸方向のみ",6,170,100,38,()=>Edit(x=>x.MaxAxes="1"));Button(c,"pattern-all","全近傍",112,170,100,38,()=>Edit(x=>x.MaxAxes="4"));
                Button(c,"pattern-custom","最大2軸",218,170,110,38,()=>Edit(x=>x.MaxAxes="2"));
                CfgUi.Label(c,"同時に動かす最大軸数",6,224,225,34,17);CfgUi.Input(c,d.MaxAxes,244,222,80,36,v=>EditRaw(x=>x.MaxAxes=v),true);
            }
            else {CfgUi.Label(c,"最大座標変位 m",6,180,225,34,17);CfgUi.Input(c,d.MaxDistance,244,178,80,36,v=>EditRaw(x=>x.MaxDistance=v),true);}
            CfgUi.Label(c,"使用軸（Nは4のまま）",6,280,325,35,18);int.TryParse(d.AxesMask,out int mask);
            string[] names={"X","Y","Z","W"};int[] indices={1,2,0,3};for(int i=0;i<4;i++){int bit=1<<indices[i];CfgUi.Toggle(c,names[i],(mask&bit)!=0,6+i*80,321,80,v=>Edit(x=>{int.TryParse(x.AxesMask,out int m);x.AxesMask=(v?m|bit:m&~bit).ToString();}));}
            CfgUi.Toggle(c,"前：陣営符号 × 論理Z > 0",d.Forward,6,369,325,v=>Edit(x=>x.Forward=v));
            CfgUi.Toggle(c,"横：論理Zの変位 = 0",d.Sideways,6,408,325,v=>Edit(x=>x.Sideways=v));CfgUi.Toggle(c,"後：陣営符号 × 論理Z < 0",d.Backward,6,447,325,v=>Edit(x=>x.Backward=v));
            CfgUi.Toggle(c,"空セルへ移動",d.AllowEmpty,6,497,325,v=>Edit(x=>x.AllowEmpty=v));CfgUi.Toggle(c,"敵駒を捕獲（試し盤のみ）",d.AllowCapture,6,536,325,v=>Edit(x=>x.AllowCapture=v));
            if(d.Kind!="step/1")foreach(var toggle in c.GetComponentsInChildren<Toggle>())toggle.interactable=false;
            CfgUi.Label(c,"味方セルへ重ねることはできません。\n王手回避・成り打ち・L：未評価",6,582,325,65,17);
            BuildOcclusion(c,663);
            counts=CfgUi.Label(c,"",6,785,325,170,18);UpdateCounts();
            Button(c,"draft-undo","設定Undo",6,970,155,40,()=>{draft.Undo();QueueQuery();BuildDetails();});Button(c,"draft-redo","設定Redo",173,970,155,40,()=>{draft.Redo();QueueQuery();BuildDetails();});
            Button(c,"restore-piece","この駒を試験原本へ戻す",6,1023,322,40,()=>{var original=SettingsDraft.CreateExperiment().Current.Definitions.FirstOrDefault(x=>x.Id==selectedDefinition);if(original!=null){historyOwner="draft";draft.Edit(data=>data.Definitions[data.Definitions.FindIndex(x=>x.Id==selectedDefinition)]=original.Clone());QueueQuery();BuildDetails();}});
            CfgUi.Label(c,"復帰元：cfg1-preview-fixture/1\n原本は保護。変更対象はDraftだけです。",6,1074,325,75,16);
        }
        void BuildOcclusion(RectTransform c,float y)
        {
            CfgUi.Label(c,"現在："+draft.Current.Occlusion,6,y,325,30,15);
            Button(c,"occlusion-lattice","格子直線 / 1",6,y+38,155,41,()=>{historyOwner="draft";draft.Edit(d=>d.Occlusion="lattice-line/1");QueueQuery();BuildDetails();});
            Button(c,"occlusion-supercover","閉Supercover / 1",173,y+38,155,41,()=>{historyOwner="draft";draft.Edit(d=>d.Occlusion="supercover-closed/1");QueueQuery();BuildDetails();});
        }
        void BuildSaveDetails(RectTransform c)
        {
            CfgUi.Label(c,"編集・試し盤用の名前付き保存",6,0,325,39,19);CfgUi.Input(c,draft.Name,6,50,325,42,v=>{historyOwner="draft";draft.Edit(d=>d.Name=v);QueueQuery();UpdateHeader();},true);
            CfgUi.Label(c,"保存で対局へ適用されません。\n不正入力は下書き形式へ別保存します。\n試し配置と表示状態はルールと分離して同梱します。",6,107,325,130,18);
            Button(c,"load","保存済み設定を読む",6,252,325,44,()=>RequestLoad(savePath));Button(c,"save-draft","生入力を下書き保存",6,310,325,44,()=>SaveDraft());
            CfgUi.Label(c,"今回run内の保存ファイル",6,378,325,32,19);int y=421;
            foreach(var file in Directory.GetFiles(runtimeRoot,"*.xml").Take(12)){string path=file;Button(c,"load-"+Path.GetFileName(file),Path.GetFileName(file),6,y,325,40,()=>RequestLoad(path));y+=48;}
            CfgUi.Label(c,"N=2標準互換・本番適用・MatchSnapshot：CFG-2以降。名前だけで標準互換とは扱いません。",6,y+16,325,130,17);
        }
        void BuildCoordinateControls(RectTransform c,float y)
        {
            CfgUi.Label(c,"選択セル (X,Y,Z,W)",6,y,325,35,18);
            CfgUi.Input(c,coordinateRaw,6,y+46,325,40,v=>{coordinateRaw=v;var a=v.Split(',');int[] n=new int[4];coordinateValid=a.Length==4;for(int i=0;i<4&&coordinateValid;i++)coordinateValid=int.TryParse(a[i],out n[i])&&n[i]>=-4&&n[i]<=4;if(coordinateValid){selectedCell=new QN(n[2],n[0],n[1],n[3]);Notice("選択セル "+v);}else Notice("座標入力不正：−4〜4の整数をX,Y,Z,Wの順で4つ入力してください。配置は不可です。");},true);
            Button(c,"coordinate-place","選択セルへ試し配置",6,y+103,325,42,()=>{if(!coordinateValid)throw new ArgumentException("不正な座標のため配置できません。入力は保持しています。");PlaceAt(selectedCell);});
            CfgUi.Label(c,"断面外を含む明示座標の配置です。\n盤域外・重複セルは拒否します。",6,y+161,325,80,17);
        }
        void QueueQuery()
        {
            queryCancel?.Cancel();queryCancel?.Dispose();queryCancel=new CancellationTokenSource();result=null;queryGate.Invalidate();geometryDirty=true;
            if(!draft.TryCompile(out preset,out validation)){Notice("入力不正："+string.Join(" / ",validation.Errors));UpdateCounts();return;}
            var snapshot=new PreviewSession(preview.Bounds,preview.Pieces);var captured=preset;string id=selectedPiece;var token=queryCancel.Token;
            var stamp=new QueryStamp(draft.SessionId,draft.Revision,preview.Revision,id,++requestId);queryGate.Begin(stamp);
            pendingQuery=Task.Run(()=>PreviewQuery.Execute(captured,snapshot,id,token,int.MaxValue,stamp));UpdateCounts();UpdateHeader();
        }
        void Update()
        {
            if(slice==null)return;
            if(pendingQuery!=null&&pendingQuery.IsCompleted)
            {var task=pendingQuery;pendingQuery=null;if(task.IsCompletedSuccessfully&&queryGate.TryAccept(task.Result)){result=task.Result;geometryDirty=true;Record("Query",result.Status+" G="+result.G.Count+" P="+result.P.Count);}else if(task.IsFaulted){Notice(task.Exception.GetBaseException().Message);}UpdateCounts();}
            long before=slice.ViewRevision;slice.Advance(Time.unscaledDeltaTime);if(before!=slice.ViewRevision)geometryDirty=true;
            if(opened&&geometryDirty)
            {
                previewRenderer.Rebuild(slice.Frame,slice.ViewRevision,preview.Pieces.Select(p=>new CfgVisualPiece{Id=p.Id,DefinitionId=p.DefinitionId,Side=p.Side,Position=p.Position}),result?.G,result?.P,preview.Find(selectedPiece)?.Position);
                geometryDirty=false;foreach(var mini in minis)mini.Refresh(slice.Frame);UpdateCounts();
                sliceText.text=(slice.IsAnimating?"回転中（任意中間姿勢）":slice.StableView.ToString())+" ｜ 厳密4Dセル断面\n中心(F,U,V,W) "+slice.Frame.Center;
            }
            bool textInput=EventSystem.current?.currentSelectedGameObject?.GetComponent<InputField>()!=null;
            if(opened&&!textInput&&modal==null)
            {
                bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
                if(ownership.Owner==InputOwner.None&&ctrl&&Input.GetKeyDown(KeyCode.Z)){if(historyOwner=="trial")preview.Undo();else draft.Undo();QueueQuery();BuildDetails();}
                if(ownership.Owner==InputOwner.None&&ctrl&&Input.GetKeyDown(KeyCode.Y)){if(historyOwner=="trial")preview.Redo();else draft.Redo();QueueQuery();BuildDetails();}
                if(Input.GetKeyDown(KeyCode.Escape)){ownership.Cancel();pointerButton=-1;SetTool("選択");Notice("一時操作を終了しました。下書きは保持しています。");}
            }
        }
        void UpdateHeader()
        {if(headerStatus!=null)headerStatus.text=draft.Name+"  ｜ N=4 ｜ "+(draft.IsDirty?"設定：未保存":"設定：保存済み")+" ｜ 未適用（対局未接続） ｜ "+(lastSavedTrial==preview.Hash?"試し配置：保存済み":"試し配置：変更あり");}
        void UpdateCounts()
        {
            void Set(string detail,string summary){if(counts!=null)counts.text=detail;if(previewCounts!=null)previewCounts.text=summary;}
            if(preset==null){Set("現在入力：不正／未対応\nG / P：未評価\nL：未評価","現在入力：不正／未対応 ｜ G / P / L：未評価");return;}
            if(result==null){Set("G / P：計算中\nL：未評価","G / P：計算中 ｜ L：未評価");return;}
            if(result.Status!=QueryStatus.Complete&&result.Status!=QueryStatus.Partial){Set("G / P：未評価\n"+result.Reason+"\nL：未評価","G / P：未評価 ｜ "+result.Status+" ｜ L：未評価");return;}
            string state=result.Status==QueryStatus.Complete?"計算完了":result.Status==QueryStatus.Partial?"一部計算":result.Status.ToString();
            Set(state+"\n理論候補（盤端なし）："+(result.TheoreticalCount?.ToString()??"未評価")+"\n全4D：G "+result.G.Count+" / P "+result.P.Count+" セル\n表示窓内：G "+previewRenderer.VisibleG+" / P "+previewRenderer.VisibleP+" セル\nL（合法手・着手数）：未評価"+(preset.Find(selectedDefinition)?.AxesMask!=15?"\n非対称な軸制限の実験設定":""),
                state+" ｜ 全4D G "+result.G.Count+" / P "+result.P.Count+" ｜ 表示窓 G "+previewRenderer.VisibleG+" / P "+previewRenderer.VisibleP+"\n理論候補（盤端なし） "+(result.TheoreticalCount?.ToString()??"未評価")+" ｜ L（完全合法手）：未評価");
        }
        void Notice(string text){notice=text;if(statusText!=null)statusText.text=text;Record("Notice",text);}
        void SetTool(string value){tool=value;historyOwner="trial";Notice("試し盤ツール："+value+" / 配置陣営 "+placementSide);}
        void AfterTrial(){QueueQuery();UpdateHeader();geometryDirty=true;Record("Trial",preview.Hash);}
        void PlaceAt(QN q)
        {preview.Place(new TrialPiece("trial-"+Guid.NewGuid().ToString("N"),selectedDefinition,placementSide,q));historyOwner="trial";AfterTrial();}
        void RunPointer(Action action)
        {try{action();}catch(ArgumentException e){Notice(e.Message);Record("InputRejected",e.GetType().Name);}catch(InvalidOperationException e){Notice(e.Message);Record("InputRejected",e.GetType().Name);}}
        void RequestView(string axis)
        {if(ownership.Owner!=InputOwner.None){Notice("操作中です。現在のドラッグを完了または取消してください。");return;}slice.Request(SliceAxes.ViewForAxis((LogicalAxis)Enum.Parse(typeof(LogicalAxis),axis)));geometryDirty=true;Record("SliceRequest",axis);}
        internal void Translate(LogicalAxis axis,double delta)
        {slice.Translate(axis,delta);geometryDirty=true;Record("AxisDelta",axis+" "+delta);}
        internal VecN SliceCenter=>slice.Frame.Center;
        internal void RestoreCenter(VecN center){slice.SetCenter(center);geometryDirty=true;}
        internal void InputNotice(string message)=>Notice(message);
        void PreviewDown(PointerEventData e)
        {
            if(modal!=null)return;InputOwner owner=e.button==PointerEventData.InputButton.Right?InputOwner.CameraOrbit:e.button==PointerEventData.InputButton.Middle?InputOwner.CameraPan:InputOwner.Board;
            if(!ownership.TryBegin(owner,e.pointerId,()=>{yaw=startYaw;pitch=startPitch;distance=startDistance;pan=startPan;UpdateCamera();pointerButton=-1;}))return;
            startYaw=yaw;startPitch=pitch;startDistance=distance;startPan=pan;
            pointerButton=(int)e.button;pointerStart=lastPointer=e.position;pointerRevision=slice.ViewRevision;pointerTrial=preview.Revision;pointerDraft=draft.Revision;pointerCamera=cameraRevision;pointerMoved=false;historyOwner="trial";Record("PointerDown",e.button+" revision="+pointerRevision);
        }
        void PreviewDrag(PointerEventData e)
        {
            if(pointerButton<0||ownership.PointerId!=e.pointerId)return;var delta=e.position-lastPointer;lastPointer=e.position;pointerMoved|=(e.position-pointerStart).sqrMagnitude>16;
            if(pointerButton==1){yaw+=delta.x*.25f;pitch=Mathf.Clamp(pitch-delta.y*.2f,-75,75);UpdateCamera();}
            else if(pointerButton==2){pan+=(-rig.Camera.transform.right*delta.x-rig.Camera.transform.up*delta.y)*distance*.001f;UpdateCamera();}
        }
        void PreviewUp(PointerEventData e)
        {
            if(ownership.PointerId!=e.pointerId||pointerButton!=(int)e.button)return;
            int button=pointerButton;pointerButton=-1;ownership.Release(ownership.Owner,e.pointerId);if(button!=0||pointerMoved||modal!=null)return;
            if(pointerRevision!=slice.ViewRevision||pointerTrial!=preview.Revision||pointerDraft!=draft.Revision||pointerCamera!=cameraRevision||slice.IsAnimating){Notice("表示・設定・試し盤更新後の古いクリックを拒否しました。");return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(previewRect,e.position,null,out var local);var rect=previewRect.rect;
            var ray=rig.Camera.ViewportPointToRay(new Vector3((local.x-rect.xMin)/rect.width,(local.y-rect.yMin)/rect.height,0));
            if(!previewRenderer.Pick(ray,pointerRevision,out var q,tool=="選択"))return;selectedCell=q;
            if(tool=="配置")PlaceAt(q);else if(tool=="消去"){preview.Erase(q);AfterTrial();}
            else if(preview.At(q)!=null&&preview.At(q).Side!=preview.Find(selectedPiece)?.Side&&result?.P.Contains(q)==true&&preset!=null){preview.TryMove(preset,selectedPiece,q,out string captureReason);Notice(captureReason);AfterTrial();}
            else if(preview.At(q)!=null){selectedPiece=preview.At(q).Id;selectedDefinition=preview.At(q).DefinitionId;BuildPieces();BuildDetails();QueueQuery();}
            else if(preset!=null&&result!=null&&result.Status==QueryStatus.Complete){preview.TryMove(preset,selectedPiece,q,out string reason);Notice(reason);AfterTrial();}
            Record("CellPick",q+" revision="+pointerRevision);
        }
        void PreviewScroll(PointerEventData e){if(modal!=null||ownership.Owner!=InputOwner.None)return;distance=Mathf.Clamp(distance-e.scrollDelta.y*.035f,.24f,2);UpdateCamera();Record("CameraZoom",distance.ToString());}
        void UpdateCamera(){var rotation=Quaternion.Euler(pitch,yaw,0);var target=CfgPreviewRenderer.AirAnchor+pan;rig.Camera.transform.position=target+rotation*new Vector3(0,0,-distance);rig.Camera.transform.LookAt(target);cameraRevision++;}
        void OnApplicationFocus(bool focused){if(focused)return;ownership.FocusLost();pointerButton=-1;foreach(var mini in minis)mini.CancelDrag();Record("FocusLost","pointer ownership cancelled, uncommitted drag rolled back");}
        ViewSnapshot CaptureView()
        {var f=slice.Frame;double[] V(VecN v)=>new[]{v.X,v.Y,v.Z,v.W};return new ViewSnapshot(V(f.Center),V(f.AxisX).Concat(V(f.AxisY)).Concat(V(f.AxisZ)).Concat(V(f.Normal)).ToArray(),new double[]{yaw,pitch,distance,pan.x,pan.y,pan.z});}
        void Save(bool saveAs)
        {
            if(slice.IsAnimating)throw new InvalidOperationException("回転完了後に保存してください。現在の下書きと旧保存版は保持しています。");
            if(!draft.TryCompile(out _,out _)){if(saveAs){SettingsStore.SaveDraft(savePath,draft,preview,CaptureView(),true);savedDraftBaseline=draft.Current;savedBaselinePersisted=true;lastSavedTrial=preview.Hash;UpdateHeader();Notice("不正生入力を別IDの下書きとして保存しました。未適用です。");}else SaveDraft();return;}
            string destination=savePath;if(!saveAs&&File.Exists(destination)&&SettingsStore.Load(destination).Kind==SavedSettingsKind.Draft)destination=Path.Combine(runtimeRoot,"preview-preset-"+draft.Id+".xml");
            SettingsStore.SavePreset(destination,draft,preview,CaptureView(),saveAs);savePath=destination;savedDraftBaseline=draft.Current;savedBaselinePersisted=true;lastSavedTrial=preview.Hash;UpdateHeader();Notice("編集・試し盤用設定を保存しました。対局へは未適用です。");Record("Saved",savePath);
        }
        void SaveDraft(string destination=null)
        {if(slice.IsAnimating)throw new InvalidOperationException("回転完了後に下書き保存してください。");string path=destination??Path.Combine(runtimeRoot,"draft.xml");SettingsStore.SaveDraft(path,draft,preview,CaptureView());savedDraftBaseline=draft.Current;savedBaselinePersisted=true;lastSavedTrial=preview.Hash;UpdateHeader();Notice("生入力を下書き形式で保存しました。実行可能プリセットとは別です。");Record("DraftSaved",path);}
        void RequestLoad(string path)
        {
            if(!draft.IsDirty&&lastSavedTrial==preview.Hash){Load(path);return;}
            ShowDialog("未保存の設定・試し配置を保存してから読み込めます。",out var p);
            Button(p,"load-save-first","下書きを保存して読む",25,95,550,43,()=>{string current=Path.Combine(runtimeRoot,"draft.xml");if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(current),StringComparison.OrdinalIgnoreCase))current=Path.Combine(runtimeRoot,"draft-before-load-"+Guid.NewGuid().ToString("N")+".xml");SaveDraft(current);Load(path);CloseDialog();});
            Button(p,"load-discard","未保存の編集を破棄して読む",25,151,550,43,()=>{Load(path);CloseDialog();});Button(p,"load-cancel","編集を続ける",25,207,550,43,CloseDialog);
        }
        void Load(string path)
        {
            var loaded=SettingsStore.Load(path);ValidateRestorableView(loaded.View);
            var defs=loaded.Draft.Current.Definitions;
            if(defs.Count==0||defs.Select(d=>d.Id).Distinct().Count()!=defs.Count||loaded.Preview.Pieces.Any(p=>!defs.Any(d=>d.Id==p.DefinitionId)))throw new ArgumentException("駒IDの構造・参照が不正な下書きはこの画面では読込できません。元ファイルと現在の編集を保持しました。");
            queryCancel?.Cancel();ownership.Cancel();
            RestoreView(loaded.View);draft=loaded.Draft;preview=loaded.Preview;savedDraftBaseline=draft.Current;savedBaselinePersisted=true;savePath=loaded.Kind==SavedSettingsKind.Draft?Path.Combine(runtimeRoot,"preview-preset.xml"):path;lastSavedTrial=preview.Hash;
            if(!draft.Current.Definitions.Any(d=>d.Id==selectedDefinition))selectedDefinition=draft.Current.Definitions[0].Id;
            selectedPiece=preview.Find(selectedPiece)?.Id??preview.Find("subject")?.Id??preview.Pieces.FirstOrDefault()?.Id??"subject";
            if(preview.Find(selectedPiece)!=null)selectedDefinition=preview.Find(selectedPiece).DefinitionId;
            BuildPieces();BuildDetails();QueueQuery();UpdateHeader();Notice("読込完了。保存内容を編集・試し盤へ復元しました。対局へは未適用です。");Record("Loaded",path);
        }
        void RestoreView(ViewSnapshot view)
        {
            // Saved intermediate frames are validated, then restored without pretending to be a reference view.
            var c=view.Center;var b=view.Basis;VecN V(double[] a,int o)=>new VecN(a[o],a[o+1],a[o+2],a[o+3]);
            slice.Restore(new SliceFrame(V(c,0),V(b,0),V(b,4),V(b,8),V(b,12)));
            var camera=view.Camera;yaw=(float)camera[0];pitch=(float)camera[1];distance=(float)camera[2];pan=new Vector3((float)camera[3],(float)camera[4],(float)camera[5]);UpdateCamera();geometryDirty=true;
        }
        void ValidateRestorableView(ViewSnapshot view)
        {var c=view.Center;var b=view.Basis;VecN V(double[] a,int o)=>new VecN(a[o],a[o+1],a[o+2],a[o+3]);new SliceAnimator().Restore(new SliceFrame(V(c,0),V(b,0),V(b,4),V(b,8),V(b,12)));if(view.Camera[2]<.24||view.Camera[2]>2||Math.Abs(view.Camera[1])>75)throw new ArgumentException("保存カメラはCFG1操作範囲外です。旧編集状態を保持しました。");}
        void SaveAsDialog()
        {
            ShowDialog("別名で保存：新しいIDを作成します。",out var panel);string name=draft.Name+"（コピー）";CfgUi.Input(panel,name,25,85,550,42,v=>name=v);
            Button(panel,"confirm-save-as","保存",25,150,260,42,()=>{draft.Edit(d=>d.Name=name);string oldPath=savePath;try{savePath=Path.Combine(runtimeRoot,"preset-"+Guid.NewGuid().ToString("N")+".xml");Save(true);}catch{savePath=oldPath;throw;}CloseDialog();});Button(panel,"cancel-save-as","編集を続ける",310,150,265,42,CloseDialog);
        }
        void RequestBack()
        {
            ShowDialog((draft.IsDirty?"未保存の変更があります。":"設定は保存済みです。")+" 対局へは未適用です。",out var p);
            Button(p,"back-save","下書きを保存して戻る",25,95,550,43,()=>{SaveDraft();CloseDialog();ShowMenu();});
            Button(p,"back-keep","編集を保持してメニューへ",25,148,270,43,()=>{CloseDialog();ShowMenu();});
            Button(p,"back-discard","設定の未保存分を破棄",307,148,268,43,()=>{draft=new SettingsDraft(savedDraftBaseline,savedBaselinePersisted);QueueQuery();BuildDetails();CloseDialog();ShowMenu();});
            Button(p,"back-cancel","編集を続ける",25,207,550,43,CloseDialog);
        }
        void ShowDialog(string title,out RectTransform panel)
        {
            CloseDialog();modal=CfgUi.Panel(root,"Modal input owner",0,0,1536,1024,new Color(0,0,0,.64f));panel=CfgUi.Panel(modal,"Dialog",468,345,600,300);
            CfgUi.Label(panel,title,25,20,550,66,20);ownership.Cancel();pointerButton=-1;foreach(var mini in minis)mini.CancelDrag();
        }
        void CloseDialog(){if(modal!=null)Destroy(modal.gameObject);modal=null;}
        void RequestQuit()
        {
            if(quitting)return;ShowDialog("終了します。保存した設定は今回run内に残ります。",out var p);
            Button(p,"quit-save","下書き保存して終了",25,95,550,43,()=>{SaveDraft();Quit();});Button(p,"quit-without-save","保存せず終了",25,151,550,43,Quit);Button(p,"quit-cancel","戻る",25,207,550,43,CloseDialog);
        }
        void Quit(){quitting=true;queryCancel?.Cancel();Record("Quit","no match outcome created");FlushTrace();Application.Quit();}
        void FlushTrace(){if(!string.IsNullOrEmpty(runtimeRoot))File.WriteAllLines(Path.Combine(runtimeRoot,"events-"+System.Diagnostics.Process.GetCurrentProcess().Id+".tsv"),eventTrace);}
        void OnDestroy(){queryCancel?.Cancel();queryCancel?.Dispose();previewRenderer?.Dispose();if(previewTexture!=null){previewTexture.Release();Destroy(previewTexture);}FlushTrace();}
    }
}
