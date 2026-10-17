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
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity.Settings
{
    // CFG2 owns only the new scene. The CFG1 and legacy demonstration entries remain available.
    public sealed partial class Cfg2App : MonoBehaviour
    {
        EnvironmentRig rig; Camera backgroundCamera; CfgPreviewRenderer previewRenderer; Cfg2Visuals visuals; SliceAnimator slice,trialSlice;
        SettingsDraft draft; MatchOptionsRaw options; MatchConfig config; PreviewPreset preset;
        PreviewSession preview; QueryResult result; IReadOnlyList<Q4> attackCells=Array.Empty<Q4>();
        MatchSession session; ValidationReport validation;
        Canvas canvas; CanvasGroup inputGate; RectTransform root,menu,settings,matchHud,detailHost,pieceHost,previewRect,planeRect,resultPanel;
        Text headerStatus,counts,statusText,clockNear,clockFar,matchStatus,resultText; ScrollRect detailScroll;
        RawImage previewImage; RenderTexture previewTexture;
        readonly List<Cfg2MiniView> minis=new List<Cfg2MiniView>(); Cfg2PlaneView planeView;
        readonly List<RectTransform> helperPanels=new List<RectTransform>();
        readonly Dictionary<string,Button> actions=new Dictionary<string,Button>();
        readonly Dictionary<string,InputField> inputs=new Dictionary<string,InputField>();
        readonly Dictionary<string,float> detailPositions=new Dictionary<string,float>();
        readonly List<string> eventTrace=new List<string>(); readonly InputOwnership ownership=new InputOwnership();
        readonly string[] categories={"ルールプリセット","盤・初期配置","駒の動き設定","対局ルール","CPU設定","表示・操作"};
        string runRoot,runtimeRoot,validPath,draftPath,trialPath,recordRoot,notice="",startupLoadError="",selectedDefinition="king-step",selectedPiece="subject",tool="選択";
        int category=2,detailTab,planeTab; Side placementSide=Side.A; Q4 selectedCell;
        bool opened,geometryDirty=true,quitting,showCandidates=true,showAttacks,showGrid=true,showHelpers=true;
        bool settingsDirty,stableDirty; float saveDue; string detailContext="",lastRecordedMatch="";
        float yaw=32,pitch=29,distance=.58f; Vector3 pan;
        float trialYaw,trialPitch,trialDistance;Vector3 trialPan;
        long cameraRevision,layoutRevision; string historyOwner="draft";
        int previousWidth,previousHeight; bool manualCamera;
        // The piece plane Y=h is displayed on the wooden board surface, so the orbit pivot is the board itself.
        Vector3 CameraBase=>SectionView.Anchor;
        SliceAnimator NewPivotSlice()=>new SliceAnimator(PivotViewState.CreateInitial(new Vec3(SectionView.Anchor.x,SectionView.Anchor.y,SectionView.Anchor.z),SectionView.Cell));
        [Serializable] sealed class RunConfig {public string runRoot;}
        bool startupFailed;
        void Start()
        {
            // A plain double-click start (no launcher identity arguments) saves under persistentDataPath.
            // Starts with --pivot-request-id keep the strict launcher/build identity check used by tests.
            bool standalone=Arg("--pivot-request-id","")=="";
            try
            {
                if(standalone){runRoot=Application.persistentDataPath;Directory.CreateDirectory(runRoot);}
                else
                {
                    var resource=Resources.Load<TextAsset>("cfg2-run");
                    runRoot=Arg("--cfg2-run-root",resource==null?"":JsonUtility.FromJson<RunConfig>(resource.text).runRoot);
                }
                if(string.IsNullOrWhiteSpace(runRoot)||!Directory.Exists(runRoot))throw new InvalidOperationException("CFG2の保存先が見つかりません。");
                if(!standalone)VerifyPivotIdentity();
                runtimeRoot=Path.Combine(runRoot,"runtime-data",Arg("--cfg2-data","user"));
                if(!Path.GetFullPath(runtimeRoot).StartsWith(Path.GetFullPath(Path.Combine(runRoot,"runtime-data"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid runtime data path");
                Directory.CreateDirectory(runtimeRoot); recordRoot=Path.Combine(runRoot,"match-records");Directory.CreateDirectory(recordRoot);
                if(standalone)PruneOldFiles();
                validPath=Path.Combine(runtimeRoot,"pivot-next-match.xml");draftPath=Path.Combine(runtimeRoot,"pivot-invalid-draft.xml");trialPath=Path.Combine(runtimeRoot,"trial-view.xml");
                Application.runInBackground=true;Application.targetFrameRate=45;QualitySettings.vSyncCount=0;
                var fonts=Font.GetOSInstalledFontNames();var fontName=new[]{"Yu Gothic UI","Yu Gothic","Meiryo","MS Gothic"}.FirstOrDefault(fonts.Contains);
                if(fontName==null)throw new InvalidOperationException("既存の日本語フォントがありません。");
                CfgUi.Font=Font.CreateDynamicFontFromOSFont(fontName,18);
                rig=ShowcaseEnvironment.Build();rig.Debug.enabled=false;
                var background=Instantiate(rig.Camera.gameObject);background.name="CFG2 settings backdrop";background.tag="Untagged";
                backgroundCamera=background.GetComponent<Camera>();backgroundCamera.depth=rig.Camera.depth-1;backgroundCamera.enabled=false;Destroy(background.GetComponent<AudioListener>());
                backgroundCamera.cullingMask&=~(1<<30);
                previewRenderer=new CfgPreviewRenderer(rig){Fill=true,StrictDefinitions=true};visuals=new Cfg2Visuals(rig);slice=NewPivotSlice();
                GameObject.Find("CFG1 exact cell sections").layer=30;GameObject.Find("CFG2 exact cell faces").layer=30;
                previewRenderer.SetDisplayState(slice.DisplayState);
                draft=SettingsDraft.CreateMatchDemo();options=new MatchOptionsRaw();preview=PreviewSession.CreateExperiment();
                LoadStartup();trialSlice=slice;ValidateSettings();CreateAudio();CreateCanvas();CreateMenu();CreateSettings();CreateMatchHud();ShowMenu();
                trialYaw=yaw;trialPitch=pitch;trialDistance=distance;trialPan=pan;settingsDirty=(!File.Exists(validPath)||migratedView)&&startupLoadError=="";
                Application.wantsToQuit+=PrepareShutdown;
                Record("Started","CFG2; font="+fontName+"; "+Screen.width+"x"+Screen.height+"; reportedDpi="+Screen.dpi+"; data="+runtimeRoot);
                WritePlayerIdentity("Menu");
                if(Arg("--cfg2-smoke","")!="")StartCoroutine(Smoke());
            }
            catch(Exception e)
            {
                Debug.LogException(e);
                if(!standalone){enabled=false;Application.Quit(2);return;}
                // A double-click start has no launcher to read the exit code, so say why on screen instead of closing.
                Application.wantsToQuit-=PrepareShutdown;startupFailed=true;ShowStartupError(e);
            }
        }
        void ShowStartupError(Exception e)
        {
            var go=new GameObject("CFG2 startup error",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var errorCanvas=go.GetComponent<Canvas>();errorCanvas.renderMode=RenderMode.ScreenSpaceOverlay;errorCanvas.sortingOrder=1000;
            var back=new GameObject("Background",typeof(RectTransform),typeof(Image));back.transform.SetParent(go.transform,false);
            var backRect=(RectTransform)back.transform;backRect.anchorMin=Vector2.zero;backRect.anchorMax=Vector2.one;backRect.offsetMin=backRect.offsetMax=Vector2.zero;back.GetComponent<Image>().color=new Color(.03f,.04f,.06f,1);
            var label=new GameObject("Message",typeof(RectTransform),typeof(Text));label.transform.SetParent(go.transform,false);
            var labelRect=(RectTransform)label.transform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;labelRect.offsetMin=new Vector2(48,48);labelRect.offsetMax=new Vector2(-48,-48);
            var text=label.GetComponent<Text>();text.font=Font.CreateDynamicFontFromOSFont(new[]{"Yu Gothic UI","Yu Gothic","Meiryo","MS Gothic","Arial"},22);text.fontSize=22;text.color=Color.white;
            text.text="四次元将棋を起動できませんでした。\nThe game could not start.\n\n"+e.Message+"\n\n詳細ログ / Log: "+Application.consoleLogPath+"\n\nEsc：終了 / Quit";
        }
        // Standalone starts keep only the newest per-launch traces and per-match view files; match records are kept.
        void PruneOldFiles()
        {
            const int keep=20;
            foreach(var pattern in new[]{"events-*.tsv","pivot-match-*.xml"})
            {
                try
                {
                    foreach(var old in new DirectoryInfo(runtimeRoot).GetFiles(pattern).OrderByDescending(f=>f.LastWriteTimeUtc).Skip(keep))old.Delete();
                }
                catch(Exception e){Debug.LogWarning("CFG2 could not prune "+pattern+": "+e.Message);}
            }
        }
        string Arg(string key,string fallback){var a=System.Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        void Record(string name,string data="")
        {var row=DateTime.UtcNow.ToString("o")+"\t"+name+"\t"+data.Replace('\n',' ');eventTrace.Add(row);Debug.Log("CFG2 "+name+" "+data);}
        void Notice(string text){notice=text;if(statusText!=null)statusText.text=text;Record("Notice",text);}
        void FlushTrace(){if(!string.IsNullOrEmpty(runtimeRoot))File.WriteAllLines(Path.Combine(runtimeRoot,"events-"+System.Diagnostics.Process.GetCurrentProcess().Id+".tsv"),eventTrace);}
        void ValidateSettings()
        {
            bool valid=MatchConfig.TryCreate(draft,options,out var next,out validation);config=valid&&startupLoadError==""?next:null;
            draft.TryCompile(out preset,out _);UpdateHeader();QueueQuery();
        }
        void AfterEdit()
        {historyOwner="draft";ValidateSettings();settingsDirty=true;saveDue=Time.unscaledTime+.25f;geometryDirty=true;}
        void Edit(Action<RawPieceDefinition> edit,bool rebuild=true)
        {draft.Edit(d=>edit(d.Definitions.Single(x=>x.Id==selectedDefinition)));AfterEdit();if(rebuild)BuildDetails();}
        void QueueQuery()
        {
            result=null;attackCells=Array.Empty<Q4>();
            var source=session!=null&&!opened?session.ToPreview():preview;
            var rule=session!=null&&!opened?session.Snapshot.Config.Preset:preset;
            if(rule!=null&&source.Find(selectedPiece)!=null)
            {
                result=PreviewQuery.Execute(rule,source,selectedPiece);
                var attacks=PreviewQuery.AttackCells(rule,source,selectedPiece);
                if(attacks.Status==QueryStatus.Complete)attackCells=attacks.Cells;
            }
            geometryDirty=true;
        }
        void RenderGeometry()
        {
            var source=session!=null&&!opened?session.ToPreview():preview;
            var ps=source.Pieces.Select(p=>new CfgVisualPiece{Id=p.Id,DefinitionId=p.DefinitionId,Position=p.Position,Side=p.Side}).ToArray();
            if(session!=null&&!opened)ps=WithGlide(ps);
            if(!opened&&session==null)ps=new[]{new CfgVisualPiece{Id="menu-king",DefinitionId="king-step",Side=Side.A,Position=new Q4(0,0,0,0)},new CfgVisualPiece{Id="menu-rook",DefinitionId="rook-h11",Side=Side.A,Position=new Q4(-2,-2,0,0)},new CfgVisualPiece{Id="menu-bishop",DefinitionId="bishop-h22",Side=Side.A,Position=new Q4(1,2,0,0)}};
            previewRenderer.SetDisplayState(slice.DisplayState);previewRenderer.Candidates=showCandidates;previewRenderer.Attacks=showAttacks;previewRenderer.Grid=showGrid;
            bool mainMenu=!opened&&session==null;
            previewRenderer.ShowPieces=!mainMenu;if(mainMenu){previewRenderer.Grid=false;previewRenderer.Candidates=false;previewRenderer.Attacks=false;}
            previewRenderer.Rebuild(slice.Frame,slice.ViewRevision,ps,result?.G,result?.P,attackCells,mainMenu?(Q4?)null:source.Find(selectedPiece)?.Position);
            foreach(var section in previewRenderer.PieceSections.Values)section.Object.layer=30;
            // Only menu dressing uses the fixed board path. In play and settings
            // every piece, candidate, selected cell and hit uses one real section.
            visuals.RefreshBoard(mainMenu?ps:Array.Empty<CfgVisualPiece>());
            visuals.RefreshBoardCandidates(Array.Empty<Q4>(),null,false);
            foreach(var panel in helperPanels)panel.gameObject.SetActive(showHelpers);
            RefreshAuxiliaryViews();
            geometryDirty=false;UpdateCounts();
        }
        void UpdateCounts()
        {
            if(counts==null)return;
            counts.text=result==null?"駒を選択してください":result.Status!=QueryStatus.Complete?"照会："+result.Status:
                "全4D  G "+result.G.Count+" / P "+result.P.Count+" / 攻撃 "+attackCells.Count+"\n表示内  G "+previewRenderer.VisibleG+" / P "+previewRenderer.VisibleP+" / 攻撃 "+previewRenderer.VisibleAttacks;
        }
        void UpdateHeader()
        {
            if(headerStatus==null)return;
            headerStatus.text=startupLoadError!=""?"保存読込エラー。\n元ファイルを保持中。":config!=null?"変更は次の対局へ自動反映します":"入力を確認してください。対局開始は保留中です。";
            headerStatus.color=config!=null?new Color(.7f,.81f,.89f):new Color(1,.65f,.47f);
        }
        void Update()
        {
            if(startupFailed){if(Input.GetKeyDown(KeyCode.Escape))Application.Quit(2);return;}
            if(slice==null)return;
            if(previousWidth!=Screen.width||previousHeight!=Screen.height){previousWidth=Screen.width;previousHeight=Screen.height;layoutRevision++;ownership.Cancel();pointerButton=-1;RefreshAuxiliaryViews();}
            if(slice.IsAnimating){slice.Advance(Time.unscaledDeltaTime);geometryDirty=true;stableDirty=true;}
            UpdateMatch();if(geometryDirty)RenderGeometry();
            if(settingsDirty&&Time.unscaledTime>=saveDue)AutoSave();
            // The view is saved once it has settled: never during a CPU move show, a turn reset or a piece glide,
            // which move C every frame without marking the slice as animating.
            if(stableDirty&&!slice.IsAnimating&&!PresentingMove&&ownership.Owner==InputOwner.None&&Time.unscaledTime>=saveDue+.5f)SaveStableView();
            bool typing=EventSystem.current?.currentSelectedGameObject?.GetComponent<InputField>()!=null;
            if(!typing&&ownership.Owner==InputOwner.None&&Input.GetKeyDown(KeyCode.Home)&&IsPivotHomeSurface(Input.mousePosition))
            {
                bool initial=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
                slice.SetCenter(initial?slice.DisplayState.Embedding:default);geometryDirty=true;stableDirty=true;Record("PivotHome",initial?"P":"O");
            }
            if(!typing&&ownership.Owner==InputOwner.None&&cpuShowResult==null&&resetStage==0&&Input.GetKeyDown(KeyCode.R)&&!Input.GetKey(KeyCode.LeftControl)&&!Input.GetKey(KeyCode.RightControl))
            {slice.Restore(NewPivotSlice().DisplayState);geometryDirty=true;stableDirty=true;Record("PivotReset","C=P;XYZ");}
            if(!typing&&Input.GetKeyDown(KeyCode.Escape)){if(ownership.Owner!=InputOwner.None){ownership.Cancel();pointerButton=-1;}else if(opened){if((!settingsDirty||AutoSave())&&SaveStableView())ShowMenu();}else if(session!=null)ReturnFromMatch();}
            if(opened&&!typing&&ownership.Owner==InputOwner.None&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))
            {
                if(Input.GetKeyDown(KeyCode.Z)){if(historyOwner=="trial"){preview.Undo();AfterTrial();}else{draft.Undo();AfterEdit();BuildDetails();}}
                if(Input.GetKeyDown(KeyCode.Y)){if(historyOwner=="trial"){preview.Redo();AfterTrial();}else{draft.Redo();AfterEdit();BuildDetails();}}
            }
        }
        bool IsPivotHomeSurface(Vector2 point)=>minis.Any(m=>m.IsPointerOver(point))||gizmos.Any(g=>g.IsPointerOver(point))||
            (opened&&planeTab==0&&RectTransformUtility.RectangleContainsScreenPoint(previewRect,point,null));
        void LateUpdate(){if(inputGate!=null)inputGate.interactable=ownership.Owner==InputOwner.None;}
        void Quit()
        {if(quitting||!PrepareShutdown())return;Application.Quit();}
        bool PrepareShutdown()
        {if(quitting)return true;if(settingsDirty&&!AutoSave())return false;if(!SaveStableView())return false;if(session!=null)ReturnFromMatch();quitting=true;Record("Quit");FlushTrace();return true;}
        void OnDestroy(){Application.wantsToQuit-=PrepareShutdown;CancelCpu();previewRenderer?.Dispose();visuals?.Dispose();if(previewTexture!=null){previewTexture.Release();Destroy(previewTexture);}FlushTrace();}
    }
}
