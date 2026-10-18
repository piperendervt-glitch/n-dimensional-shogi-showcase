using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        PreviewSession preview; QueryResult result; IReadOnlyList<QN> attackCells=Array.Empty<QN>();
        MatchSession session; ValidationReport validation;
        Canvas canvas; CanvasGroup inputGate; RectTransform root,menu,settings,matchHud,detailHost,pieceHost,previewRect,planeRect,resultPanel;
        Text headerStatus,headerDimensions,hudDimensions,counts,statusText,clockNear,clockFar,matchStatus,resultText; ScrollRect detailScroll;
        RawImage previewImage; RenderTexture previewTexture;
        readonly List<Cfg2MiniView> minis=new List<Cfg2MiniView>(); Cfg2PlaneView planeView; Cfg2ProjectionView projectionView; RectTransform projectionRect;
        RectTransform settingsHeading,navPanel,centerPanel,projectionControls,planeMiniHost,trialTools,displayToggles,settingsGizmo; Text settingsTitle; Button backButton;
        readonly List<RectTransform> helperPanels=new List<RectTransform>();
        readonly Dictionary<string,Button> actions=new Dictionary<string,Button>();
        readonly Dictionary<string,InputField> inputs=new Dictionary<string,InputField>();
        readonly Dictionary<string,float> detailPositions=new Dictionary<string,float>();
        readonly List<string> eventTrace=new List<string>(); readonly InputOwnership ownership=new InputOwnership();
        readonly string[] categories={"ルールプリセット","盤・初期配置","駒の動き設定","対局ルール","CPU設定","表示・操作"};
        string runRoot,runtimeRoot,validPath,draftPath,trialPath,recordRoot,notice="",startupLoadError="",selectedDefinition="king-step",selectedPiece="subject",tool="選択";
        int category=2,detailTab,planeTab; Side placementSide=Side.A; QN selectedCell;
        bool opened,geometryDirty=true,quitting,showCandidates=true,showAttacks,showGrid=true,showHelpers=true;
        bool settingsDirty,stableDirty; float saveDue; string detailContext="",lastRecordedMatch="";
        float yaw=32,pitch=29,distance=.58f; Vector3 pan;
        float trialYaw,trialPitch,trialDistance;Vector3 trialPan;
        long cameraRevision,layoutRevision; string historyOwner="draft";
        int previousWidth,previousHeight; bool manualCamera;
        // The piece plane Y=h is displayed on the wooden board surface, so the orbit pivot is the board itself.
        // A layout copied along Y stacks 9 layers; its display is raised by the 4 layers below the plane (board min −4),
        // so the lowest layer rests on the wood and the ±4.5-cell window shows the whole stack.
        const int LayersBelowPlane=4;
        static Vector3 AnchorFor(int? copyAxis)=>SectionView.Anchor+new Vector3(0,copyAxis==2?LayersBelowPlane*SectionView.Cell:0,0);
        Vector3 DisplayAnchor=>AnchorFor(CurrentCopyAxis);
        Vector3 CameraBase=>DisplayAnchor;
        SliceAnimator NewPivotSlice()=>NewPivotSlice(CurrentDimensions,CurrentCopyAxis);
        static SliceAnimator NewPivotSlice(int dimensions,int? copyAxis){var a=AnchorFor(copyAxis);return new SliceAnimator(PivotViewState.CreateInitial(new Vec3(a.x,a.y,a.z),SectionView.Cell,dimensions:dimensions));}
        // N and copy axis of the draft, read once per draft revision: Current clones the whole draft.
        SettingsDraft draftRead;long draftReadRevision;int draftReadDimensions=4;int? draftReadCopyAxis;
        void ReadDraft()
        {
            if(ReferenceEquals(draftRead,draft)&&draftReadRevision==draft.Revision)return;
            var d=draft.Current;draftRead=draft;draftReadRevision=draft.Revision;
            draftReadDimensions=int.TryParse(d.Dimensions,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)&&n>=BoundsN.MinDimensions&&n<=BoundsN.MaxDimensions?n:4;
            var axis=DraftData.CopyAxisOf(d.InitialLayout);draftReadCopyAxis=axis.HasValue&&axis.Value<draftReadDimensions?axis:null;
        }
        // Copy axis of the initial layout on screen: the match's while playing, the next match's (the draft's) otherwise.
        int? DraftCopyAxis{get{if(draft==null)return null;ReadDraft();return draftReadCopyAxis;}}
        int? CurrentCopyAxis=>session!=null&&!opened?session.Config.Preset.LayoutCopyAxis:DraftCopyAxis;
        // N of the board on screen: the match's while playing, the next match's (the draft's) otherwise; 4 before the draft exists.
        int DraftDimensions{get{if(draft==null)return 4;ReadDraft();return draftReadDimensions;}}
        int CurrentDimensions=>session!=null&&!opened?session.Config.Dimensions:DraftDimensions;
        // Hidden axis Ctrl+drag rotates against: W unless a number key picked another axis of this space (4=W, 5=V ... 9=R, 0=Q, -=P).
        // It belongs to one space: a new match and a change of N start again from W.
        LogicalAxis rotationAxis=LogicalAxis.W;
        int RotationCoreAxis{get{int core=SliceAxes.CoreAxis(rotationAxis);return core<CurrentDimensions?core:3;}}
        static string AppTitle(int dimensions)=>"N次元将棋（"+dimensions+"次元）";
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,EntryPoint="SetWindowTextW")] static extern bool SetWindowText(IntPtr window,string text);
#endif
        // The window title names the current N; the product name (and with it the save folder) stays as it was.
        void UpdateWindowTitle()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try{var window=GetActiveWindow();if(window!=IntPtr.Zero)SetWindowText(window,AppTitle(CurrentDimensions));}
            catch(Exception e){Debug.LogWarning("CFG2 window title: "+e.Message);}
#endif
        }
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
                InitializeGraphicsQuality(standalone&&Arg("--cfg2-smoke","")=="");
                GameObject.Find("CFG1 exact cell sections").layer=30;GameObject.Find("CFG2 exact cell faces").layer=30;
                previewRenderer.SetDisplayState(slice.DisplayState);
                draft=SettingsDraft.CreateMatchDemo();options=new MatchOptionsRaw();preview=PreviewSession.CreateExperiment();
                LoadStartup();if(slice.Frame.Dimensions!=DraftDimensions)slice=NewPivotSlice();trialSlice=slice;viewDimensions=DraftDimensions;viewLayout=draft.Current.InitialLayout;SyncViewToDraft();
                ValidateSettings();CreateAudio();CreateCanvas();CreateMenu();CreateSettings();CreateMatchHud();CreateHelp();CreateLeaveQuestion();ShowMenu();
                trialYaw=yaw;trialPitch=pitch;trialDistance=distance;trialPan=pan;settingsDirty=(!File.Exists(validPath)||migratedView)&&startupLoadError=="";
                Application.wantsToQuit+=PrepareShutdown;
                Record("Started","CFG2; font="+fontName+"; "+Screen.width+"x"+Screen.height+"; reportedDpi="+Screen.dpi+"; data="+runtimeRoot);
                WritePlayerIdentity("Menu");
                if(Arg("--cfg2-smoke","")!="")StartCoroutine(Smoke());
                // The first plain start opens 遊び方 once; it stays one key (F1) or one menu entry away afterwards.
                else if(standalone&&PlayerPrefs.GetInt(HelpSeenKey,0)==0)OpenHelp(0);
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
            text.text="N次元将棋を起動できませんでした。\nThe game could not start.\n\n"+e.Message+"\n\n詳細ログ / Log: "+Application.consoleLogPath+"\n\nEsc：終了 / Quit";
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
        // Options show the latest notice in their status line; the menu and the match HUD show it for a while,
        // and a new screen keeps only a notice raised by the action that opened it.
        Text hudNotice,menuNotice;float noticeTime=-100;int noticeFrame=-1;bool screenNotice;const float NoticeSeconds=10;
        void Notice(string text)
        {
            notice=text;noticeTime=Time.unscaledTime;noticeFrame=Time.frameCount;screenNotice=true;
            if(statusText!=null)statusText.text=text;if(hudNotice!=null)hudNotice.text=text;if(menuNotice!=null)menuNotice.text=text;Record("Notice",text);
        }
        void KeepScreenNotice(){if(noticeFrame!=Time.frameCount)ClearScreenNotice();}
        void ClearScreenNotice(){screenNotice=false;if(hudNotice!=null)hudNotice.text="";if(menuNotice!=null)menuNotice.text="";}
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
            result=null;attackCells=Array.Empty<QN>();
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
            if(!opened&&session==null)ps=new[]{new CfgVisualPiece{Id="menu-king",DefinitionId="king-step",Side=Side.A,Position=new QN(0,0,0,0)},new CfgVisualPiece{Id="menu-rook",DefinitionId="rook-h11",Side=Side.A,Position=new QN(-2,-2,0,0)},new CfgVisualPiece{Id="menu-bishop",DefinitionId="bishop-h22",Side=Side.A,Position=new QN(1,2,0,0)}};
            previewRenderer.SetDisplayState(slice.DisplayState);previewRenderer.Candidates=showCandidates;previewRenderer.Attacks=showAttacks;previewRenderer.Grid=showGrid;
            bool mainMenu=!opened&&session==null;
            previewRenderer.ShowPieces=!mainMenu;if(mainMenu){previewRenderer.Grid=false;previewRenderer.Candidates=false;previewRenderer.Attacks=false;}
            previewRenderer.Rebuild(slice.Frame,slice.ViewRevision,ps,result?.G,result?.P,attackCells,mainMenu?(QN?)null:source.Find(selectedPiece)?.Position);
            foreach(var section in previewRenderer.PieceSections.Values)section.Object.layer=30;
            // Only menu dressing uses the fixed board path. In play and settings
            // every piece, candidate, selected cell and hit uses one real section.
            visuals.RefreshBoard(mainMenu?ps:Array.Empty<CfgVisualPiece>());
            visuals.RefreshBoardCandidates(Array.Empty<QN>(),null,false);
            foreach(var panel in helperPanels)panel.gameObject.SetActive(showHelpers&&!(panel==settingsGizmo&&opened&&(planeTab==1||planeTab==7)));
            RefreshAuxiliaryViews();
            geometryDirty=false;UpdateCounts();
        }
        void UpdateCounts()
        {
            if(counts==null)return;
            counts.text=result==null?"駒を選択してください":result.Status!=QueryStatus.Complete?"計算中："+result.Status:
                "移動できるマス："+CurrentDimensions+"次元全体 "+result.P.Count+"／この断面 "+previewRenderer.VisibleP+"\n攻撃範囲："+CurrentDimensions+"次元全体 "+attackCells.Count+"／この断面 "+previewRenderer.VisibleAttacks;
        }
        void UpdateHeader()
        {
            RefreshMenuDimensions();
            if(headerStatus==null)return;
            if(headerDimensions!=null)headerDimensions.text=DraftDimensions+"次元";
            headerStatus.text=startupLoadError!=""?"保存読込エラー。\n元ファイルを保持中。":config!=null?"変更は次の対局へ自動反映します":"入力を確認してください。対局開始は保留中です。";
            headerStatus.color=config!=null?new Color(.7f,.81f,.89f):new Color(1,.65f,.47f);
        }
        void Update()
        {
            if(startupFailed){if(Input.GetKeyDown(KeyCode.Escape))Application.Quit(2);return;}
            if(slice==null)return;
            if(previousWidth!=Screen.width||previousHeight!=Screen.height){previousWidth=Screen.width;previousHeight=Screen.height;layoutRevision++;ownership.Cancel();pointerButton=-1;RefreshAuxiliaryViews();}
            // The canvas scaler sizes the canvas on its own schedule, so the spare width is re-read each frame (a no-op when unchanged).
            if(opened)LayoutSettings();
            if(slice.IsAnimating){slice.Advance(Time.unscaledDeltaTime);geometryDirty=true;stableDirty=true;}
            // The help and the leave question hold the match: its clock, the CPU and the hand-over wait until they close.
            matchClock?.Hold(OverlayOpen);if(!OverlayOpen)UpdateMatch();UpdateCandidateTour();StepSectionGlide();if(geometryDirty)RenderGeometry();UpdateMagnifier();
            if(settingsDirty&&Time.unscaledTime>=saveDue)AutoSave();
            if(screenNotice&&Time.unscaledTime-noticeTime>NoticeSeconds)ClearScreenNotice();
            // The view is saved once it has settled: never during a CPU move show, a turn reset or a piece glide,
            // which move C every frame without marking the slice as animating.
            if(stableDirty&&!slice.IsAnimating&&!PresentingMove&&ownership.Owner==InputOwner.None&&Time.unscaledTime>=saveDue+.5f)SaveStableView();
            bool typing=CfgUi.SelectedInput!=null;
            if(!typing&&OverlayKeys()||OverlayOpen)return;
            UpdateMenuKeys(typing);
            if(!typing&&session!=null&&!opened&&Input.GetKeyDown(KeyCode.Tab))StepCandidateTour();
            if(!typing&&ownership.Owner==InputOwner.None&&Input.GetKeyDown(KeyCode.Home)&&IsPivotHomeSurface(Input.mousePosition))
            {
                bool initial=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
                slice.SetCenter(initial?slice.DisplayState.Embedding:default);geometryDirty=true;stableDirty=true;Record("PivotHome",initial?"P":"O");
            }
            if(!typing&&ownership.Owner==InputOwner.None&&cpuShowResult==null&&resetStage==0&&Input.GetKeyDown(KeyCode.R)&&!Input.GetKey(KeyCode.LeftControl)&&!Input.GetKey(KeyCode.RightControl))
            {ResetCandidateTour();slice.Restore(NewPivotSlice().DisplayState);geometryDirty=true;stableDirty=true;Record("PivotReset","C=P;XYZ");}
            // Number keys pick the hidden axis Ctrl+drag rotates against: 4=W, 5=V, 6=U, 7=T, 8=S, 9=R, 0=Q, -=P.
            if(!typing&&ownership.Owner==InputOwner.None&&(opened||session!=null))
                for(int axis=4;axis<=BoundsN.MaxDimensions;axis++)
                {
                    var key=axis<10?KeyCode.Alpha0+axis:axis==10?KeyCode.Alpha0:KeyCode.Minus;
                    if(Input.GetKeyDown(key)){PickRotationAxis((LogicalAxis)(axis-1));break;}
                }
            if(!typing&&Input.GetKeyDown(KeyCode.Escape))EscapePressed();
            if(opened&&!typing&&ownership.Owner==InputOwner.None&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))
            {
                if(Input.GetKeyDown(KeyCode.Z))HistoryKey(false);
                if(Input.GetKeyDown(KeyCode.Y))HistoryKey(true);
            }
        }
        void PickRotationAxis(LogicalAxis logical)
        {
            if(SliceAxes.CoreAxis(logical)>=CurrentDimensions){Notice("この対局は"+CurrentDimensions+"次元です。"+SliceAxes.AxisName(logical)+"軸はありません。");return;}
            rotationAxis=logical;geometryDirty=true;Notice("Ctrl＋ドラッグの回転軸："+SliceAxes.AxisName(logical));Record("RotationAxis",logical.ToString());UpdateHelpText();
        }
        void EscapePressed(){if(ownership.Owner!=InputOwner.None){ownership.Cancel();pointerButton=-1;}else if(opened)LeaveSettings();else if(session!=null)RequestLeaveMatch();}
        // Ctrl+Z/Ctrl+Y. An Undo or Redo of the draft can change N or the initial layout like the buttons do, so the trial board and view follow it.
        void HistoryKey(bool redo)
        {
            if(historyOwner=="trial"){if(redo)preview.Redo();else preview.Undo();AfterTrial();return;}
            if(!(redo?draft.Redo():draft.Undo()))return;
            SyncViewToDraft();AfterEdit();BuildDetails();Record(redo?"DraftRedo":"DraftUndo","N="+DraftDimensions+";layout="+draft.Current.InitialLayout);
        }
        bool IsPivotHomeSurface(Vector2 point)=>minis.Any(m=>m.IsPointerOver(point))||gizmos.Any(g=>g.IsPointerOver(point))||
            (opened&&planeTab==0&&RectTransformUtility.RectangleContainsScreenPoint(previewRect,point,null));
        void LateUpdate(){if(inputGate!=null)inputGate.interactable=ownership.Owner==InputOwner.None;}
        void Quit()
        {if(quitting||!PrepareShutdown())return;Application.Quit();}
        // Unsaved settings keep the window open, except when the rejected original is being kept and nothing can be saved.
        // A match display is only a convenience: one that cannot be saved is reported and left behind.
        bool PrepareShutdown()
        {
            if(quitting)return true;
            if((settingsDirty||opened&&stableDirty)&&startupLoadError==""&&!AutoSave())return false;
            if(session!=null&&!opened&&!SaveStableView())Record("QuitWithoutMatchView",notice);
            if(session!=null)ReturnFromMatch();quitting=true;FlushVolumes();Record("Quit");FlushTrace();return true;
        }
        void OnDestroy(){Application.wantsToQuit-=PrepareShutdown;CancelCpu();FlushVolumes();previewRenderer?.Dispose();visuals?.Dispose();if(previewTexture!=null){previewTexture.Release();Destroy(previewTexture);}FlushTrace();}
    }
}
