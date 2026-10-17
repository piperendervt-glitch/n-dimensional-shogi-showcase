using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        Task<CpuResult> pendingCpu;CancellationTokenSource cpuCancel;float cpuReadyTime;
        RectTransform matchSurface;Vector3 cameraFrom,cameraTo,targetFrom,targetTo;float presentationStart=-1;
        int shownPly=-1;Side cameraSide=Side.A;MatchMode lastMode;float presentationSeconds=.45f;
        // A PvP hand-over orbits slowly enough to read whose turn it is.
        const float TurnChangeSeconds=1.6f;
        // CvC shows each CPU move in its hyperplane: slow 4D turn, slide of C along the normal, brief selection, then the move.
        const double CpuTurnSeconds=1.6;const float CpuSlideSeconds=.8f,CpuHoldSeconds=.5f;
        CpuResult cpuShowResult;int cpuShowStage;double cpuShowLift;VecN cpuShowFromCell,cpuShowToCell;Vec3 cpuShowAnchor;float cpuShowStart;VecN cpuShowFrom,cpuShowTo;ReferenceSection cpuShowView;
        // The space and section the hyperplane panels were built for.
        int minisDimensions;ReferenceSection minisSection;
        // In CPU games every hand-over returns the 4D view to the initial state (C=P, no rotation).
        int resetStage;float resetStart,resetSeconds;VecN resetFrom;Vec3 resetAnchor;
        // A committed CPU move glides its piece from origin to destination; a captured piece stays until it is reached.
        sealed class PieceGlide {public string Id;public VecN From,To;public float Start,Seconds;public CfgVisualPiece Victim;}
        PieceGlide glide;bool playViewApplied;Text pauseLabel;
        const float GlideSecondsPerCell=.28f,GlideMinSeconds=.6f,GlideMaxSeconds=1.6f;
        // Display preferences of this machine, not match rules: kept out of the settings hash and match records.
        static bool Pref(string key)=>PlayerPrefs.GetInt(key,1)!=0;
        static void SetPref(string key,bool value){PlayerPrefs.SetInt(key,value?1:0);PlayerPrefs.Save();}
        const string CpuShowKey="cfg2.cpu-move-4d-turn",TurnResetKey="cfg2.cpu-game-turn-reset";
        bool CpuShowEnabled{get=>Pref(CpuShowKey);set=>SetPref(CpuShowKey,value);}
        bool TurnResetEnabled{get=>Pref(TurnResetKey);set=>SetPref(TurnResetKey,value);}
        void CreateMatchHud()
        {
            matchHud=CfgUi.Rect(root,"Match view",0,0,1536,1024);
            matchSurface=CfgUi.Rect(matchHud,"Actual match board input",0,0,1536,1024);var input=matchSurface.gameObject.AddComponent<Image>();input.color=Color.clear;AttachPointer(matchSurface);
            CfgUi.Label(matchHud,"N次元将棋",25,17,380,50,36,Gold);CfgUi.Label(matchHud,"N-DIMENSIONAL SHOGI",28,70,370,23,14,Gold);hudDimensions=CfgUi.Label(matchHud,"",215,24,160,40,26,Gold);
            var far=Cfg2Ui.Panel(matchHud,"Far clock",28,128,337,105,Dark);clockFar=CfgUi.Label(far,"",17,14,303,80,25);
            var near=Cfg2Ui.Panel(matchHud,"Near clock",28,872,352,117,Dark);clockNear=CfgUi.Label(near,"",17,15,318,86,27,Gold);
            Button(matchHud,"match-menu","☰ メニュー",1360,16,150,40,ReturnFromMatch);
            CreateGizmo(matchHud,1310,70,"match-",200,175);
            RebuildMinis();
            matchStatus=CfgUi.Label(matchHud,"",30,248,330,100,17,Muted);
            var guide=Cfg2Ui.Panel(matchHud,"Mouse guide",394,957,796,49,new Color(.02f,.03f,.045f,.78f));
            var guideText=CfgUi.Label(guide,"ドラッグ：視点回転　ホイール：ズーム　Ctrl＋ドラッグ：高次元回転（4〜9・0・−キーで回す軸）　R：表示リセット",10,7,776,35,15,new Color(.91f,.91f,.88f));guideText.alignment=TextAnchor.MiddleCenter;
            Button(matchHud,"pause","一時停止",1202,877,142,43,TogglePause);
            Button(matchHud,"step","1手進める",1352,877,158,43,StepCpu);
            Button(matchHud,"retry","処理を再試行",1202,928,308,43,()=>{CancelCpu();session?.RetryProcessing();});
            resultPanel=Cfg2Ui.Panel(matchHud,"Game result",515,772,565,212,Dark,true);resultText=CfgUi.Label(resultPanel,"",20,16,525,82,26,Gold);
            Button(resultPanel,"next-match","次の対局",20,127,251,56,()=>StartMatch(lastMode));Button(resultPanel,"result-menu","メニュー",285,127,260,56,ReturnFromMatch);resultPanel.gameObject.SetActive(false);
        }
        void StartMatch(MatchMode mode,IReadOnlyList<Piece> fixture=null,string fixtureId=null)
        {
            CancelActiveInput();
            ValidateSettings();if(config==null){Notice("入力を確認してください："+string.Join(" / ",validation.Errors));OpenSettings();return;}
            if(settingsDirty&&!AutoSave())return;if(!SaveStableView())return;
            if(session==null){trialYaw=yaw;trialPitch=pitch;trialDistance=distance;trialPan=pan;trialSelectedPiece=selectedPiece;trialSelectedCell=selectedCell;trialPlaneReference=planeReferenceCell;}
            if(session!=null){session.Interrupt("次局開始");SaveMatchRecord();}CancelCpu();ownership.Cancel();
            session=fixture==null?MatchSession.Create(config,mode,new StopwatchClock()):MatchSession.CreateFixture(config,mode,new StopwatchClock(),fixture,fixtureId);
            lastMode=mode;opened=false;settings.gameObject.SetActive(false);menu.gameObject.SetActive(false);matchHud.gameObject.SetActive(true);resultPanel.gameObject.SetActive(false);
            backgroundCamera.enabled=false;
            rig.Camera.targetTexture=null;slice=NewPivotSlice();previewRenderer.SetDisplayState(slice.DisplayState);RebuildMinis();hudDimensions.text=session.Config.Dimensions+"次元";UpdateWindowTitle();selectedPiece="";selectedCell=default;shownPly=-1;presentationStart=-1;cameraSide=Side.A;manualCamera=false;
            yaw=27;pitch=31;distance=1.04f;pan=Vector3.zero;SetMatchCamera(Side.A,false);QueueQuery();RefreshMatch();SaveMatchRecord();
            // The launcher's saved view belongs to the first match of this process only.
            string restoredView=playViewApplied?"":Arg("--pivot-play-view","");if(restoredView!=""){playViewApplied=true;RestoreSavedMatchView(restoredView);}
            Record("MatchStarted",mode+";"+session.Snapshot.MatchId+";fixture="+session.Snapshot.FixtureId+";rules="+session.Config.RuleHash+";initial="+session.Snapshot.InitialHash);
            WritePlayerIdentity("Play");
        }
        // In 4D the four panels are the four reference views, as always. In higher N the top panel is the section the display is
        // heading to and the others are the 3(N-3) sections one axis swap away, framed in the colour of the axis they swap in:
        // one column per hidden axis up to 6D, three columns beyond.
        void RebuildMinis()
        {
            foreach(var mini in minis){helperPanels.Remove((RectTransform)mini.transform);Destroy(mini.gameObject);}minis.Clear();
            int n=slice.Frame.Dimensions;var current=slice.TargetSection;minisDimensions=n;minisSection=current;
            const float x=1310,top=254,width=200,bottom=866,gap=6;
            if(n==4){for(int i=0;i<4;i++)CreateMini(matchHud,x,top+i*133,width,125,ReferenceSection.Of((ReferenceView)i),null);return;}
            var neighbours=current.Neighbours;int columns=Mathf.Min(n-3,3);int rows=Mathf.CeilToInt(neighbours.Count/(float)columns);
            float currentHeight=rows<=3?110:84;float rowHeight=Mathf.Clamp((bottom-top-currentHeight-gap)/rows-gap,28,110),columnWidth=(width-gap*(columns-1))/columns;
            CreateMini(matchHud,x,top,width,currentHeight,current,null);
            for(int i=0;i<neighbours.Count;i++)
            {
                bool columnPerAxis=columns==n-3;int column=columnPerAxis?i/3:i%columns,row=columnPerAxis?i%3:i/columns;
                var section=neighbours[i];int added=section.Shown.First(axis=>!current.Shows(axis));
                CreateMini(matchHud,x+column*(columnWidth+gap),top+currentHeight+gap+row*(rowHeight+gap),columnWidth,rowHeight,section,Cfg2Projection.AxisColor(added));
            }
        }
        void EnsureMinis()
        {
            if(matchHud==null)return;int n=slice.Frame.Dimensions;
            if(minis.Count>0&&minisDimensions==n&&(n==4||minisSection==slice.TargetSection))return;
            RebuildMinis();
        }
        void ReturnFromMatch()
        {
            CancelActiveInput();
            if(session==null){ShowMenu();return;}CancelCpu();session.Interrupt();SaveMatchRecord();Record("MatchLeft",session.State.Outcome+";"+session.State.Hash);session=null;
            selectedPiece=preview.Find(trialSelectedPiece)!=null?trialSelectedPiece:preview.Pieces.FirstOrDefault()?.Id??"";selectedCell=trialSelectedCell;planeReferenceCell=trialPlaneReference;slice=trialSlice;yaw=trialYaw;pitch=trialPitch;distance=trialDistance;pan=trialPan;RebuildMinis();ShowMenu();
        }
        void SaveMatchRecord()
        {if(session!=null)try{MatchRecordStore.Save(Path.Combine(recordRoot,session.Snapshot.MatchId+".xml"),session);}catch(Exception e){Notice("棋譜の保存に失敗："+e.Message);}}
        void TogglePause()
        {
            if(session==null)return;CancelCpu();if(session.IsPaused)session.ResumeCvc();else session.PauseCvc();
            presentationStart=-1;RefreshMatch();SaveMatchRecord();Record("CvCPause",session.Phase.ToString());
        }
        void StepCpu(){if(session==null)return;CancelCpu();session.StepCvc();RefreshMatch();Record("CvCStep",session.Generation.ToString());}
        void CancelCpu()
        {cpuCancel?.Cancel();cpuCancel?.Dispose();cpuCancel=null;pendingCpu=null;cpuShowResult=null;cpuShowStage=0;resetStage=0;glide=null;if(slice!=null)slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;if(session!=null)session.CpuClockHeld=false;}
        void ApplyCpu(CpuResult cpu)
        {
            TrialPiece victim=cpu.Move.HasValue?session.ToPreview().At(cpu.Move.Value.To):null;
            bool accepted=session.TryApplyCpu(cpu,out var reason);Record("CpuResult",cpu.Status+";accepted="+accepted+";"+reason);
            if(!accepted)return;
            selectedPiece="";QueueQuery();SaveMatchRecord();
            if(!cpu.Move.HasValue)return;
            // Without the glide the piece is already on its new cell; with it, the knock waits for the landing.
            if(!CpuShowEnabled){PlayPieceSound();return;}
            var move=cpu.Move.Value;var from=CfgPreviewRenderer.V(move.From);var to=CfgPreviewRenderer.V(move.To);
            glide=new PieceGlide{Id=move.PieceId,From=from,To=to,Start=Time.unscaledTime,Seconds=Mathf.Clamp((float)(to-from).Length*GlideSecondsPerCell,GlideMinSeconds,GlideMaxSeconds),
                Victim=victim==null?null:new CfgVisualPiece{Id=victim.Id,DefinitionId=victim.DefinitionId,Position=victim.Position,Side=victim.Side}};
            geometryDirty=true;
        }
        bool PresentingMove=>cpuShowResult!=null||resetStage!=0||glide!=null;
        float GlideProgress=>glide==null?1:Mathf.Clamp01((Time.unscaledTime-glide.Start)/glide.Seconds);
        CfgVisualPiece[] WithGlide(CfgVisualPiece[] pieces)
        {
            if(glide==null)return pieces;float t=GlideProgress,s=t*t*(3-2*t);
            foreach(var piece in pieces)if(piece.Id==glide.Id)piece.Glide=glide.From+(glide.To-glide.From)*s;
            return glide.Victim==null||t>=.98f?pieces:pieces.Concat(new[]{glide.Victim}).ToArray();
        }
        bool BeginCpuShow(CpuResult cpu)
        {
            if(!CpuShowEnabled||cpu.Status!=MatchQueryStatus.Complete||!cpu.Move.HasValue||ownership.Owner!=InputOwner.None)return false;
            var move=cpu.Move.Value;var embedding=slice.DisplayState.Embedding;
            var from=CfgPreviewRenderer.V(move.From)+embedding;var to=CfgPreviewRenderer.V(move.To)+embedding;
            cpuShowView=SliceAxes.SectionContaining(from,to,slice.StableSection??ReferenceSection.Initial(slice.Frame.Dimensions));
            cpuShowFromCell=from;cpuShowToCell=to;
            cpuShowResult=cpu;cpuShowStage=1;slice.SecondsPerLeg=CpuTurnSeconds;if(slice.Request(cpuShowView))geometryDirty=true;
            Record("CpuShow",cpuShowView.Name+";"+move);return true;
        }
        void UpdateCpuShow()
        {
            if(cpuShowResult==null)return;
            // The player took the view mid-show: keep their view and commit at once.
            if(ownership.Owner!=InputOwner.None){FinishCpuShow();return;}
            if(cpuShowStage==1)
            {
                if(slice.IsAnimating||slice.QueuedView.HasValue)return;
                // C slides along the hidden axes of the shown section until the moving piece's cell lies in it.
                slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;cpuShowFrom=slice.Center;cpuShowTo=slice.Center;
                foreach(int hidden in cpuShowView.Hidden)cpuShowTo=SliceAxes.WithCoordinate(cpuShowTo,hidden,SliceAxes.Coordinate(cpuShowFromCell,hidden));
                // Unity up is the section's third axis. A step that dips below the board surface would vanish inside the
                // wooden board, so the whole section display is raised until both ends of the step sit on or above it.
                cpuShowAnchor=slice.DisplayState.Anchor;var state=slice.DisplayState;double board=SectionView.Anchor.y;
                double lowest=Math.Min(VecN.Dot(slice.Frame.AxisZ,cpuShowFromCell-cpuShowTo),VecN.Dot(slice.Frame.AxisZ,cpuShowToCell-cpuShowTo));
                cpuShowLift=Math.Max(0,-(lowest+(cpuShowAnchor.Y-board)/state.CellScale))*state.CellScale;
                cpuShowStage=2;cpuShowStart=Time.unscaledTime;
            }
            if(cpuShowStage==2)
            {
                float t=(cpuShowTo-cpuShowFrom).Length<1e-9&&cpuShowLift<1e-9?1:Mathf.Clamp01((Time.unscaledTime-cpuShowStart)/CpuSlideSeconds),s=t*t*(3-2*t);
                slice.Place(cpuShowFrom+(cpuShowTo-cpuShowFrom)*s,cpuShowAnchor+new Vec3(0,cpuShowLift*s,0));geometryDirty=true;
                if(t<1)return;
                cpuShowStage=3;cpuShowStart=Time.unscaledTime;selectedPiece=cpuShowResult.Move.Value.PieceId;QueueQuery();
            }
            if(cpuShowStage==3&&Time.unscaledTime-cpuShowStart>=CpuHoldSeconds)FinishCpuShow();
        }
        void FinishCpuShow()
        {var cpu=cpuShowResult;cpuShowResult=null;cpuShowStage=0;slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;stableDirty=true;ApplyCpu(cpu);}
        void BeginTurnReset()
        {
            resetStage=0;if(session.Snapshot.Mode==MatchMode.PvP||!TurnResetEnabled)return;
            var initial=NewPivotSlice().DisplayState;var now=slice.DisplayState;
            if(!slice.IsAnimating&&slice.StableSection==ReferenceSection.Initial(slice.Frame.Dimensions)&&(now.Frame.Center-initial.Frame.Center).Length<1e-9&&(now.Anchor-initial.Anchor).Length<1e-9)return;
            resetStage=1;resetFrom=now.Frame.Center;resetAnchor=now.Anchor;resetStart=Time.unscaledTime;slice.SecondsPerLeg=CpuTurnSeconds;
            bool turning=slice.Request(ReferenceView.XYZ)||slice.IsAnimating;resetSeconds=turning?(float)CpuTurnSeconds:1f;geometryDirty=true;Record("TurnReset","start");
        }
        // The 4D turn back to XYZ and the glide of C and its display anchor to the initial pose run together,
        // so the view travels from where it was to the start instead of snapping. True when nothing is left to do.
        bool UpdateTurnReset()
        {
            if(resetStage==0)return true;
            var initial=NewPivotSlice().DisplayState;
            float t=Mathf.Clamp01((Time.unscaledTime-resetStart)/resetSeconds),s=t*t*(3-2*t);
            slice.Place(resetFrom+(initial.Frame.Center-resetFrom)*s,resetAnchor+(initial.Anchor-resetAnchor)*s);geometryDirty=true;
            if(t<1||slice.IsAnimating||slice.QueuedView.HasValue)return false;
            slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;slice.Restore(initial);resetStage=0;stableDirty=true;Record("TurnReset","done");return true;
        }
        void CancelTurnReset(){if(resetStage==0)return;resetStage=0;slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;}
        void UpdateMatch()
        {
            if(session==null||opened)return;
            // The CPU's clock stops once its move is computed: the display delay and the move presentation are free.
            session.CpuClockHeld=cpuShowResult!=null||pendingCpu!=null&&pendingCpu.IsCompleted;
            session.Tick();
            if(session.IsFinished)
            {
                CancelCpu();if(lastRecordedMatch!=session.Snapshot.MatchId){lastRecordedMatch=session.Snapshot.MatchId;SaveMatchRecord();Record("MatchEnded",session.State.Outcome+";winner="+session.State.Winner+";"+session.State.Hash);}
            }
            if(glide!=null){geometryDirty=true;if(GlideProgress>=1){glide=null;PlayPieceSound();}}
            // The hand-over waits until the moving piece has arrived.
            if(session.PresentationPaused&&glide==null)
            {
                if(ownership.Owner!=InputOwner.None)
                {CancelTurnReset();session.CompletePresentation();presentationStart=-1;Record("PresentationCompleted","input owner retained the view");}
                else
                {
                    if(presentationStart<0){presentationStart=Time.unscaledTime;bool pvp=session.Snapshot.Mode==MatchMode.PvP;presentationSeconds=pvp?TurnChangeSeconds:.45f;if(!manualCamera)SetMatchCamera(pvp?session.State.Turn:Side.A,true);BeginTurnReset();}
                    float t=manualCamera?1:Mathf.Clamp01((Time.unscaledTime-presentationStart)/presentationSeconds);float smooth=t*t*(3-2*t);
                    if(!manualCamera){
                    // Orbit around the board instead of cutting straight through it.
                    var target=Vector3.Lerp(targetFrom,targetTo,smooth);var from=cameraFrom-targetFrom;var to=cameraTo-targetTo;
                    float fromDistance=Mathf.Max(.001f,from.magnitude),toDistance=Mathf.Max(.001f,to.magnitude);
                    float orbitYaw=Mathf.LerpAngle(Mathf.Atan2(-from.x,-from.z)*Mathf.Rad2Deg,Mathf.Atan2(-to.x,-to.z)*Mathf.Rad2Deg,smooth);
                    float orbitPitch=Mathf.Lerp(Mathf.Asin(Mathf.Clamp(from.y/fromDistance,-1,1))*Mathf.Rad2Deg,Mathf.Asin(Mathf.Clamp(to.y/toDistance,-1,1))*Mathf.Rad2Deg,smooth);
                    rig.Camera.transform.position=target+Quaternion.Euler(orbitPitch,orbitYaw,0)*new Vector3(0,0,-Mathf.Lerp(fromDistance,toDistance,smooth));rig.Camera.transform.LookAt(target);cameraRevision++;RefreshAuxiliaryViews();}
                    bool resetDone=UpdateTurnReset();
                    if(t>=1&&resetDone){session.CompletePresentation();presentationStart=-1;Record("PresentationCompleted",session.Phase+";cameraSide="+cameraSide+(manualCamera?";manual camera retained":""));}
                }
            }
            UpdateCpuShow();
            if(pendingCpu!=null&&pendingCpu.IsCompleted&&Time.unscaledTime>=cpuReadyTime)
            {
                var task=pendingCpu;pendingCpu=null;
                if(task.IsFaulted){session.SuspendProcessing(task.Exception.GetBaseException().Message);Record("CpuFault",session.FaultReason);}
                else if(task.IsCanceled){Record("CpuCancelled");}
                else if(!BeginCpuShow(task.Result))ApplyCpu(task.Result);
                cpuCancel?.Dispose();cpuCancel=null;
            }
            if(session.CanCpuAct&&pendingCpu==null&&cpuShowResult==null&&!session.HasCpuRequest)
            {
                var request=session.BeginCpuRequest();cpuCancel=new CancellationTokenSource();var token=cpuCancel.Token;
                pendingCpu=Task.Run(()=>MatchCpu.Compute(request,token));cpuReadyTime=Time.unscaledTime+session.Config.CpuDelayMilliseconds/1000f;
                Record("CpuStarted",request.Stamp.MatchId+";revision="+request.Stamp.Revision+";generation="+request.Stamp.Generation+";seed="+session.Config.CpuSeed+";budget="+session.Config.CpuBudget);
            }
            RefreshMatch();
        }
        void RefreshMatch()
        {
            if(session==null)return;var s=session.State;var clock=session.Clock;
            string TimeText(double v){long t=(long)Math.Ceiling(Math.Max(0,v));return (t/60).ToString("00")+":"+(t%60).ToString("00");}
            string Label(Side side)=> (side==Side.A?"先手 A":"後手 B")+(s.Turn==side&&!session.IsFinished?"  手番":"")+"\n"+(session.Snapshot.Controller(side)==MatchController.Human?"PLAYER":"CPU")+"    "+TimeText(side==Side.A?clock.ASeconds:clock.BSeconds);
            clockNear.text=Label(cameraSide);clockFar.text=Label(RuleCore.Other(cameraSide));
            bool cvc=session.Snapshot.Mode==MatchMode.CvC;actions["pause"].gameObject.SetActive(cvc&&!session.IsFinished);actions["step"].gameObject.SetActive(cvc&&!session.IsFinished);
            if(pauseLabel==null)pauseLabel=actions["pause"].GetComponentInChildren<Text>();pauseLabel.text=session.IsPaused?"再開":"一時停止";actions["step"].interactable=session.Phase==MatchPhase.CvcPaused;
            actions["retry"].gameObject.SetActive(session.Phase==MatchPhase.ProcessingPaused);
            matchStatus.text=session.Phase==MatchPhase.ProcessingPaused?"処理を停止しています\n"+session.FaultReason:session.IsPaused?"一時停止中":session.PresentationPaused?(resetStage!=0?"手番交代：初期表示へ戻しています":"手番交代中"):cpuShowResult!=null?"CPU 着手："+cpuShowView.Name+" 超平面":session.CanCpuAct?"CPU 思考中":selectedPiece!=""&&result!=null?"着手候補 "+result.P.Count+"手":"";
            if(shownPly!=s.Ply)
            {
                shownPly=s.Ply;geometryDirty=true;
                visuals.RefreshCaptured(s.Captured.Where(c=>c.Victim.Kind!=PieceKind.King).GroupBy(c=>new{c.Captor,c.Victim.Kind}).Select(g=>new Cfg2CapturedPiece{Owner=g.Key.Captor,DefinitionId=MatchConfig.DefinitionId(g.Key.Kind),Count=g.Count()}));
            }
            resultPanel.gameObject.SetActive(session.IsFinished&&s.Outcome!=MatchOutcome.Interrupted);
            if(session.IsFinished)
            {
                string why=s.Outcome==MatchOutcome.KingCapture?"王捕獲":s.Outcome==MatchOutcome.NoMovesDraw?"着手可能手なし":s.Outcome==MatchOutcome.TimeLoss?"時間切れ":"中断";
                resultText.text=(s.Winner.HasValue?(s.Winner==Side.A?"先手 A":"後手 B")+" の勝ち":"引分")+"\n"+why;
            }
        }
        void SetMatchCamera(Side side,bool animate)
        {
            cameraSide=side;var target=CameraBase+new Vector3(0,.015f,0);var rot=Quaternion.Euler(31,side==Side.A?27:207,0);var position=target+rot*new Vector3(0,0,-1.04f);
            yaw=side==Side.A?27:207;pitch=31;distance=1.04f;pan=target-CameraBase;
            cameraFrom=rig.Camera.transform.position;targetFrom=cameraFrom+rig.Camera.transform.forward*1.04f;cameraTo=position;targetTo=target;
            if(!animate){rig.Camera.transform.position=position;rig.Camera.transform.LookAt(target);cameraRevision++;RefreshAuxiliaryViews();}
        }
    }
}
