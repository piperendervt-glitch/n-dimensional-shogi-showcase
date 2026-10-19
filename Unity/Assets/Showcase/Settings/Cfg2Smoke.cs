using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        [Serializable] sealed class Cfg2SmokeObservation
        { public string name, layer, utc, matchId, positionHash, settingsHash, outcome; public int ply; }
        [Serializable] sealed class Cfg2LayoutMeasurement
        { public string capture,path,text;public float preferredHeight,rectHeight;public bool scrollExcluded,clipped; }
        [Serializable] sealed class Cfg2SmokeReport
        {
            public string status="RUNNING",phase,error,input="actual Windows Player; simulated uGUI events; explicitly labelled fixture/API setup; no physical-input claim",unity,device,runRoot,dataRoot;
            public int width,height,processId; public float dpi; public long managedBytes;
            public List<string> checks=new List<string>(); public List<Cfg2SmokeObservation> observations=new List<Cfg2SmokeObservation>(); public List<string> images=new List<string>();
            public List<Cfg2LayoutMeasurement> layout=new List<Cfg2LayoutMeasurement>();
        }
        [Serializable] sealed class Cfg2RestartExpected
        {
            public string settingsHash,ruleHash,previewHash,center,kingAxes,timeSeconds,cpuDelay,embedding,selectedPiece,selectedCell,planeReference;
            public double height,halfWindow,cellScale;public double[] center4,basis,anchor,camera;
        }
        Cfg2SmokeReport cfg2Smoke;
        string smokeImageRoot,smokeSuffix;
        PointerEventData smokeLastPointer;

        void SmokeCheck(bool condition,string name,string layer="Player API assertion")
        {
            if(!condition)throw new InvalidOperationException("CFG2 SMOKE: "+name);
            cfg2Smoke.checks.Add(name);
            cfg2Smoke.observations.Add(new Cfg2SmokeObservation{name=name,layer=layer,utc=DateTime.UtcNow.ToString("o"),matchId=session?.Snapshot.MatchId,positionHash=session?.State.Hash,settingsHash=config?.SettingsHash,outcome=session?.State.Outcome.ToString(),ply=session?.State.Ply??0});
            Record("SmokePass",layer+"; "+name);
        }
        IEnumerator SmokeWait(Func<bool> ready,string purpose,float seconds=20)
        {
            float start=Time.realtimeSinceStartup;
            while(!ready())
            {
                if(Time.realtimeSinceStartup-start>seconds)throw new TimeoutException("CFG2 SMOKE wait: "+purpose+"; notice="+notice+"; phase="+session?.Phase+"; fault="+session?.FaultReason);
                yield return null;
            }
            yield return null;
        }
        // layoutMustFit=false still measures every Text, but reports clipping in the log and the layout list instead of failing.
        IEnumerator SmokeCapture(string name,bool layoutMustFit=true)
        {
            yield return new WaitForEndOfFrame();
            string path=Path.Combine(smokeImageRoot,name+".png");
            ScreenCapture.CaptureScreenshot(path);cfg2Smoke.images.Add(path);yield return null;
            if(name!="final")SmokeLayoutCheck(name,layoutMustFit);
        }
        void SmokeLayoutCheck(string capture,bool mustFit=true)
        {
            Canvas.ForceUpdateCanvases();var clipped=new List<string>();int checkedCount=0,excludedCount=0;
            foreach(var label in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if(!label.isActiveAndEnabled||string.IsNullOrWhiteSpace(label.text))continue;
                var ownerCanvas=label.GetComponentInParent<Canvas>();if(ownerCanvas!=null&&!ownerCanvas.isActiveAndEnabled)continue;
                var path=new List<string>();for(var t=label.transform;t!=null&&t!=canvas.transform;t=t.parent)path.Add(t.name);path.Reverse();
                bool scroll=label.GetComponentsInParent<ScrollRect>(true).Any();float wanted=label.preferredHeight,available=label.rectTransform.rect.height;
                bool clip=!scroll&&wanted>available+3;
                cfg2Smoke.layout.Add(new Cfg2LayoutMeasurement{capture=capture,path=string.Join("/",path),text=label.text,preferredHeight=wanted,rectHeight=available,scrollExcluded=scroll,clipped=clip});
                if(scroll)excludedCount++;else checkedCount++;
                if(clip)clipped.Add(string.Join("/",path)+" wanted="+wanted+" available="+available+" text="+label.text.Replace('\n',' '));
            }
            Record("SmokeLayout",capture+"; checked="+checkedCount+"; scrollExcluded="+excludedCount+"; clips="+clipped.Count);
            if(!mustFit){if(clipped.Count>0)Record("SmokeLayoutClipsReported",capture+"; "+string.Join(" | ",clipped));return;}
            SmokeCheck(clipped.Count==0,"layout "+capture+" has no active non-scroll Text height clipping ("+checkedCount+" measured, "+excludedCount+" scroll-excluded)"+(clipped.Count==0?"":"; "+string.Join(" | ",clipped)),"actual uGUI preferredHeight versus rect height, 3px tolerance");
        }
        IEnumerator Smoke()
        {
            string phase=Arg("--cfg2-smoke","initial");
            int pid=System.Diagnostics.Process.GetCurrentProcess().Id;
            smokeSuffix=phase+"-"+Screen.width+"x"+Screen.height+"-"+pid;
            smokeImageRoot=Path.Combine(runRoot,"screenshots",smokeSuffix);Directory.CreateDirectory(smokeImageRoot);
            cfg2Smoke=new Cfg2SmokeReport{phase=phase,unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height,dpi=Screen.dpi,processId=pid,runRoot=runRoot,dataRoot=runtimeRoot};
            var stack=new Stack<IEnumerator>();stack.Push(SmokeSteps2(phase));bool failed=false;
            while(stack.Count>0)
            {
                object item=null;bool more=false;
                try{more=stack.Peek().MoveNext();if(more)item=stack.Peek().Current;}
                catch(Exception e){cfg2Smoke.status="FAIL";cfg2Smoke.error=e.ToString();failed=true;Debug.LogException(e);break;}
                if(!more){stack.Pop();continue;}if(item is IEnumerator nested){stack.Push(nested);continue;}yield return item;
            }
            if(!failed)cfg2Smoke.status="PASS";
            cfg2Smoke.managedBytes=GC.GetTotalMemory(false);Record("SmokeFinished",cfg2Smoke.status+"; checks="+cfg2Smoke.checks.Count);
            File.WriteAllText(Path.Combine(runRoot,"smoke-"+smokeSuffix+".json"),JsonUtility.ToJson(cfg2Smoke,true));FlushTrace();
            yield return SmokeCapture("final");
            File.WriteAllText(Path.Combine(runRoot,"smoke-"+smokeSuffix+".json"),JsonUtility.ToJson(cfg2Smoke,true));
            if(session!=null&&!session.IsFinished)session.Interrupt("smoke process cleanup, not a rules outcome");
            Application.Quit(failed?1:0);
        }
        void SmokeAction(string key)
        {
            if(!actions.TryGetValue(key,out var button)||button==null||!button.gameObject.activeInHierarchy||!button.interactable)
                throw new InvalidOperationException("CFG2 SMOKE unavailable UI action "+key);
            button.onClick.Invoke();Record("SmokeSimulatedButton",key);
            // Leaving a match that is still being played asks first; the checks answer it as a player who means to leave.
            if(key=="match-menu"&&leaveOverlay!=null&&leaveOverlay.gameObject.activeSelf){actions["leave-confirm"].onClick.Invoke();Record("SmokeSimulatedButton","leave-confirm");}
        }
        void SmokeInput(string key,string value)
        {
            if(!inputs.TryGetValue(key,out var field)||field==null||!field.gameObject.activeInHierarchy||!field.interactable)
                throw new InvalidOperationException("CFG2 SMOKE unavailable UI input "+key+"; available="+string.Join(",",inputs.Keys));
            field.text=value;field.onEndEdit.Invoke(value);Record("SmokeSimulatedInput",key+"="+value);
        }
        void SmokeToggle(string title,bool value)
        {
            var toggle=settings.GetComponentsInChildren<Toggle>().Single(t=>t.gameObject.name==title);
            toggle.isOn=value;Record("SmokeSimulatedToggle",title+"="+value);
        }
        PointerEventData SmokePointer2(Vector2 point,int id=171,PointerEventData.InputButton button=PointerEventData.InputButton.Left)
            =>new PointerEventData(EventSystem.current){pointerId=id,button=button,position=point,pressPosition=point};
        RectTransform SmokeBoardSurface=>opened?previewRect:matchSurface;
        bool SmokePointForCell2(QN cell,out Vector2 point)
        {
            point=default;SectionResult section;string sourceKind;
            var source=session!=null&&!opened?session.ToPreview():preview;var occupant=source.At(cell);
            if(occupant!=null)
            {
                if(!previewRenderer.PieceSections.TryGetValue(occupant.Id,out var piece)||!piece.Renderer.enabled)
                {Record("SmokePickUnavailable","piece-not-rendered;"+occupant.Id+";"+cell);return false;}
                section=piece.Last;sourceKind="piece:"+occupant.Id;
            }
            else
            {
                if(!previewRenderer.TryGetCellSection(cell,out section)){Record("SmokePickUnavailable","cell-section-missing;"+cell);return false;}
                sourceKind="cell-section";
            }
            if(section==null||section.Status!=SectionStatus.Volume3D||section.Indices.Length<3)
            {Record("SmokePickUnavailable","no-volume;"+sourceKind+";"+cell);return false;}
            // One deterministic visible-facing triangle centroid on the actual
            // section, selected geometrically before invoking any picker. There
            // is no screen-radius search and no fixed-board alternate renderer.
            Vector3 world=default;float bestFacing=float.NegativeInfinity;int triangle=-1;
            float scale=(float)slice.DisplayState.CellScale;
            for(int i=0;i<section.Indices.Length;i+=3)
            {
                Vector3 At(int n)=>previewRenderer.Anchor+SectionView.MapVector(section.Vertices[section.Indices[i+n]].Position)*scale;
                var a=At(0);var b=At(1);var c=At(2);var middle=(a+b+c)/3;
                var normal=SectionView.MapVector(section.Vertices[section.Indices[i]].Normal).normalized;
                float facing=Vector3.Dot(normal,(rig.Camera.transform.position-middle).normalized);
                if(facing>bestFacing){bestFacing=facing;world=middle;triangle=i/3;}
            }
            var viewport=rig.Camera.WorldToViewportPoint(world);
            if(triangle<0||viewport.z<=0||viewport.x<=0||viewport.x>=1||viewport.y<=0||viewport.y>=1)
            {Record("SmokePickUnavailable","point-outside-view;"+sourceKind+";"+cell+";world="+world);return false;}
            var surface=SmokeBoardSurface;var rect=surface.rect;
            var screen=opened?RectTransformUtility.WorldToScreenPoint(null,previewRect.TransformPoint(new Vector3(rect.xMin+viewport.x*rect.width,rect.yMin+viewport.y*rect.height,0))):(Vector2)rig.Camera.WorldToScreenPoint(world);
            var ray=opened?rig.Camera.ViewportPointToRay(viewport):rig.Camera.ScreenPointToRay(screen);
            if(!TryPickCell(ray,slice.ViewRevision,out var hit)||hit!=cell)
            {Record("SmokePickUnavailable","unique-mesh-point-hit-mismatch;expected="+cell+";actual="+hit+";source="+sourceKind+";triangle="+triangle+";screen="+screen);return false;}
            var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(SmokePointer2(screen),rays);
            if(rays.Count==0||rays[0].gameObject!=surface.gameObject)
            {Record("SmokePickUnavailable","unique-point-ui-covered;"+cell+";top="+(rays.Count==0?"none":rays[0].gameObject.name));return false;}
            point=screen;Record("SmokeMeshPoint",sourceKind+";cell="+cell+";triangle="+triangle+";world="+world+";screen="+screen+";view="+slice.ViewRevision);return true;
        }
        // No cell is injected into CommitCell here: mesh pick and the actual pointer
        // surface decide which immutable four-dimensional cell the UI receives.
        void SmokeClickCell(QN cell,string purpose)
        {
            Canvas.ForceUpdateCanvases();RenderGeometry();
            if(!SmokePointForCell2(cell,out var point))throw new InvalidOperationException("CFG2 SMOKE no visible mesh pick for "+purpose+" "+cell+"; selected="+selectedPiece);
            var surface=SmokeBoardSurface;var e=SmokePointer2(point);e.pointerCurrentRaycast=new RaycastResult{gameObject=surface.gameObject,screenPosition=point};
            ExecuteEvents.Execute(surface.gameObject,e,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(surface.gameObject,e,ExecuteEvents.pointerUpHandler);smokeLastPointer=e;
            Record("SmokeSimulatedPointer",purpose+"; cell="+cell+"; screen="+point);
        }
        Tuple<GameObject,PointerEventData> SmokePlaneDown(QN cell,string purpose)
        {
            Canvas.ForceUpdateCanvases();RenderGeometry();
            if(!opened||planeTab==0||!planeView.Contains(cell))throw new InvalidOperationException("CFG2 SMOKE plane cell is not in active fixed plane: "+cell);
            var point=planeView.CellScreenCenter(cell);
            if(!planeView.TryCellAt(point,null,out var hit)||hit!=cell)throw new InvalidOperationException("CFG2 SMOKE plane screen mapping failed: "+cell);
            var surface=planeView.GetComponentsInChildren<CfgPointerSurface>().Single(s=>s.gameObject.activeInHierarchy).gameObject;
            var e=SmokePointer2(point,172);var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(e,rays);
            if(rays.Count==0||rays[0].gameObject!=surface)throw new InvalidOperationException("CFG2 SMOKE plane cell is covered by another UI: "+purpose);
            e.pointerCurrentRaycast=new RaycastResult{gameObject=surface,screenPosition=point};
            ExecuteEvents.Execute(surface,e,ExecuteEvents.pointerDownHandler);
            Record("SmokeSimulatedPlaneDown",purpose+"; "+planeView.Name+"; cell="+cell+"; screen="+point);
            return Tuple.Create(surface,e);
        }
        void SmokePlaneUp(Tuple<GameObject,PointerEventData> pointer)
        {ExecuteEvents.Execute(pointer.Item1,pointer.Item2,ExecuteEvents.pointerUpHandler);Record("SmokeSimulatedPlaneUp",planeView.Name);}
        void SmokePlaneClick(QN cell,string purpose)=>SmokePlaneUp(SmokePlaneDown(cell,purpose));
        IEnumerator SmokeHumanMove(string id,QN destination,string label,Vector2? destinationCamera=null)
        {
            yield return SmokeWait(()=>session!=null&&session.CanHumanAct,label+" human turn");
            int before=session.State.Ply;var piece=session.State.Find(id);
            SmokeCheck(piece!=null&&piece.Side==session.State.Turn,label+" source belongs to active human");
            SmokeClickCell(piece.Position,label+" select");yield return null;
            SmokeCheck(selectedPiece==id&&result!=null&&result.Status==QueryStatus.Complete&&result.P.Contains(destination),label+" actual UI selection uses shared P","simulated uGUI pointer + real mesh pick");
            if(destinationCamera.HasValue)
            {
                // Keep the original occluded point as a negative fixture. A
                // declared camera orbit exposes the same logical destination;
                // neither the picker nor the expected move is substituted.
                SmokeCheck(!SmokePointForCell2(destination,out _),label+" original default-view point is correctly occluded by a nearer candidate");
                string stateHash=session.State.Hash;var candidates=result.P.ToArray();var camera=destinationCamera.Value;
                PivotGesture(new Vector2((camera.x-yaw)/.25f,(pitch-camera.y)/.20f));
                SmokeCheck(Mathf.Abs(yaw-camera.x)<.001f&&Mathf.Abs(pitch-camera.y)<.001f&&session.State.Hash==stateHash&&result.P.SequenceEqual(candidates),
                    label+" declared camera orbit preserves position and all shared-P destinations","simulated public pointer drag; fixed fixture camera, no point search");
                Record("SmokeFixtureCamera",label+";yaw="+yaw+";pitch="+pitch+";sameDestination="+destination);
            }
            SmokeClickCell(destination,label+" destination");
            SmokeCheck(session.State.Ply==before+1&&session.State.Find(id)?.Position==destination,label+" commits exactly one real move","simulated uGUI pointer + real mesh pick");
            ExecuteEvents.Execute(SmokeBoardSurface.gameObject,smokeLastPointer,ExecuteEvents.pointerUpHandler);
            SmokeCheck(session.State.Ply==before+1,label+" duplicate pointer-up does not commit again","simulated uGUI duplicate delivery");
            yield return null;
        }
        IEnumerator SmokeOneHumanMove(string label)
        {
            yield return SmokeWait(()=>session!=null&&session.CanHumanAct,label+" human turn");
            foreach(var piece in session.State.Pieces.Where(p=>p.Side==session.State.Turn&&p.Position.W==0).ToArray())
            {
                RenderGeometry();if(!SmokePointForCell2(piece.Position,out _))continue;
                SmokeClickCell(piece.Position,label+" source search");yield return null;RenderGeometry();
                if(selectedPiece!=piece.Id||result==null||result.Status!=QueryStatus.Complete)continue;
                foreach(var destination in result.P.Where(q=>q.W==0&&session.State.At(q)==null))
                {
                    if(!SmokePointForCell2(destination,out _))continue;
                    int before=session.State.Ply;SmokeClickCell(destination,label+" first visible shared-P move");
                    SmokeCheck(session.State.Ply==before+1,label+" normal40 human move commits via real UI","simulated uGUI pointer + real mesh pick");yield return null;yield break;
                }
            }
            throw new InvalidOperationException("CFG2 SMOKE no visible normal40 human move for "+label);
        }
        void SmokeFixture(MatchMode mode,Piece[] pieces,string id)
        {
            Record("SmokeFixtureSetup",id+"; declarative pieces="+string.Join(";",pieces.Select(p=>p.Id+":"+p.Side+":"+p.Kind+":"+p.Position)));
            StartMatch(mode,pieces,id);
            SmokeCheck(session!=null&&session.Snapshot.Mode==mode&&session.Snapshot.FixtureId==id,mode+" creates real explicit fixture session","fixture setup through normal session constructor");
        }
        static Piece SP(string id,Side side,PieceKind kind,int f,int u,int v=0,int w=0)=>new Piece(id,side,kind,new QN(f,u,v,w));
        IEnumerator SmokeNormalModes()
        {
            for(int i=0;i<3;i++)
            {
                ShowMenu();string hash=config.SettingsHash;SmokeAction("menu-"+i);
                var mode=(MatchMode)i;
                SmokeCheck(session!=null&&session.Snapshot.Mode==mode&&session.Snapshot.InitialState.Pieces.Count==40,mode+" menu creates normal40 initial snapshot","simulated menu button");
                SmokeCheck(session.Snapshot.Config.SettingsHash==hash,mode+" snapshot matches latest validated settings");
                SmokeCheck(session.Snapshot.InitialState.Pieces.All(p=>p.Position.V==0&&p.Position.W==0),mode+" normal initial40 coordinates preserve original placement plane");
                yield return null;yield return SmokeCapture("normal-"+mode);
                if(mode==MatchMode.PvP)
                {
                    string stateHash=session.State.Hash;var beforeMoves=session.QueryLegal().Moves.Select(m=>m.Key).ToArray();var beforeCamera=rig.Camera.transform.position;
                    RequestView("Z");yield return new WaitForSecondsRealtime(.16f);yield return SmokeCapture("match-intermediate-4d");yield return SmokeWait(()=>!slice.IsAnimating,"match view Z");
                    RequestView("W");yield return SmokeWait(()=>!slice.IsAnimating,"match view W");
                    SmokeCheck(session.State.Hash==stateHash&&session.QueryLegal().Moves.Select(m=>m.Key).SequenceEqual(beforeMoves),"match 4D display changes preserve complete all-4D move set and position hash");
                    SmokeCheck(rig.Camera.transform.position==beforeCamera,"display-only 4D view changes do not trigger handover camera motion");
                    string frozenRule=session.Config.SettingsHash;
                    draft.Edit(d=>d.Definitions.Single(x=>x.Id=="king-step").MaxAxes="2");AfterEdit();
                    SmokeCheck(config.SettingsHash!=frozenRule&&session.Config.SettingsHash==frozenRule&&session.State.Hash==stateHash,"editing next-game rule leaves active match snapshot immutable","explicit next-game Draft edit while match active");
                    draft.Edit(d=>d.Definitions.Single(x=>x.Id=="king-step").MaxAxes="1");AfterEdit();
                }
                if(mode!=MatchMode.CvC)
                {
                    yield return SmokeOneHumanMove(mode+" A");
                    if(mode==MatchMode.PvP)yield return SmokeOneHumanMove(mode+" B");
                    else yield return SmokeWait(()=>session.State.Ply>=2||session.IsFinished,"normal PvC CPU reply",30);
                }
                else
                {
                    yield return SmokeWait(()=>session.State.Ply>=2&&session.CanCpuAct||session.IsFinished,"normal CvC both CPUs and presentation complete",30);
                    SmokeCheck(session.State.Ply>=2&&!session.IsFinished,"CvC normal40 has real moves from both CPUs");
                    SmokeAction("pause");yield return null;
                    SmokeCheck(session.IsPaused,"CvC pause UI suspends session");
                    int ply=session.State.Ply;long generation=session.Generation;var clock=session.Clock;
                    yield return new WaitForSecondsRealtime(.35f);
                    SmokeCheck(session.State.Ply==ply&&Math.Abs(session.Clock.ASeconds-clock.ASeconds)<1e-6&&Math.Abs(session.Clock.BSeconds-clock.BSeconds)<1e-6,"CvC explicit pause freezes both clocks and moves","real monotonic Windows time + UI pause");
                    SmokeAction("pause");var staleRequest=session.BeginCpuRequest();var staleResult=MatchCpu.Compute(staleRequest);
                    string samePosition=session.State.Hash;long sameRevision=session.Revision;
                    SmokeAction("pause");SmokeAction("pause");
                    SmokeCheck(!session.TryApplyCpu(staleResult,out _)&&session.State.Hash==samePosition&&session.Revision==sameRevision,"CvC same-position pause/resume rejects the old CPU request generation","explicit stale CPU result injection through normal acceptance API");
                    SmokeAction("pause");
                    SmokeAction("step");yield return SmokeWait(()=>session.IsPaused&&session.State.Ply==ply+1,"CvC exact one step",30);
                    SmokeCheck(session.Generation>generation&&session.State.Ply==ply+1,"CvC step commits one ply then re-pauses with new generation","simulated step button + real CPU");
                    int stepped=session.State.Ply;yield return new WaitForSecondsRealtime(.35f);
                    SmokeCheck(session.State.Ply==stepped,"CvC step remains paused without second commit");
                    SmokeAction("pause");yield return SmokeWait(()=>session.State.Ply>stepped||session.IsFinished,"CvC resume",30);
                    SmokeCheck(session.State.Ply>stepped,"CvC resume starts new real CPU work","simulated resume button + real CPU");
                }
                SmokeCheck(session.State.Ply>=2,mode+" normal40 advances both controllers");
                var interrupted=session;SmokeAction("match-menu");yield return null;
                SmokeCheck(interrupted.State.Outcome==MatchOutcome.Interrupted&&!interrupted.Clock.Running,mode+" menu return is interruption not win or draw","simulated menu return");
                SmokeCheck(menu.gameObject.activeSelf,mode+" menu becomes available for next game");
            }
        }
        IEnumerator SmokeTerminalModes()
        {
            SmokeFixture(MatchMode.PvP,new[]{SP("a-king",Side.A,PieceKind.King,-2,-1),SP("a-rook",Side.A,PieceKind.Rook,0,-1),SP("b-pawn",Side.B,PieceKind.Pawn,0,1),SP("b-king",Side.B,PieceKind.King,2,1)},"smoke-pvp-capture-and-king/1");
            yield return null;var initialCamera=rig.Camera.transform.position;
            yield return SmokeHumanMove("a-rook",new QN(0,1,0,0),"PvP A pawn capture");
            SmokeCheck(session.State.CapturedBy(Side.A).Count==1&&session.State.CapturedBy(Side.B).Count==0,"PvP capture belongs to A tray only");
            SmokeCheck(visuals.CountFor(Side.A)==1&&visuals.CountFor(Side.B)==0,"actual captured-piece render model shows A=1 B=0");
            SmokeCheck(visuals.CapturedLabel(Side.A,"pawn-step")=="×1","A captured pawn count label is actual UI text ×1");
            var presentationClock=session.Clock;yield return new WaitForSecondsRealtime(.1f);
            SmokeCheck(session.Clock.ASeconds==presentationClock.ASeconds&&session.Clock.BSeconds==presentationClock.BSeconds,"short PvP presentation freezes both real-time clocks");
            yield return SmokeWait(()=>session.CanHumanAct,"PvP B perspective transition");
            SmokeCheck((rig.Camera.transform.position-initialCamera).sqrMagnitude>1e-6,"PvP handover moves camera to next player's viewpoint");
            SmokeCheck(clockNear.text.Contains("後手 B")&&clockFar.text.Contains("先手 A"),"PvP clocks follow current near/far player labels");
            SmokeCheck(rig.Camera.WorldToScreenPoint(visuals.TrayPosition(Side.B)).x>rig.Camera.WorldToScreenPoint(visuals.TrayPosition(Side.A)).x&&visuals.CountFor(Side.A)==1,"PvP B viewpoint puts B tray on its right without swapping captured ownership");
            yield return SmokeCapture("pvp-captured-A");
            yield return SmokeHumanMove("b-king",new QN(1,1,0,0),"PvP B real reply",new Vector2(-27,0));
            yield return SmokeHumanMove("a-rook",new QN(1,1,0,0),"PvP A royal capture");
            SmokeCheck(session.State.Outcome==MatchOutcome.KingCapture&&session.State.Winner==Side.A&&session.State.Ply==3,"PvP reaches normal king-capture outcome through both UI players");
            SmokeCheck(session.State.CapturedBy(Side.A).Count==1&&session.State.Captured.Any(c=>c.Victim.Kind==PieceKind.King),"captured king is in history but excluded from normal captured tray");
            yield return SmokeTerminalFreeze("PvP");yield return SmokeCapture("pvp-result");
            string oldId=session.Snapshot.MatchId;SmokeAction("next-match");yield return null;
            SmokeCheck(session.Snapshot.MatchId!=oldId&&session.Snapshot.InitialState.Pieces.Count==40,"PvP next-match creates fresh normal40 snapshot");SmokeAction("match-menu");

            SmokeFixture(MatchMode.PvC,new[]{SP("a-king",Side.A,PieceKind.King,-2,-1),SP("a-pawn",Side.A,PieceKind.Pawn,0,1),SP("b-rook",Side.B,PieceKind.Rook,0,-1),SP("b-king",Side.B,PieceKind.King,2,2)},"smoke-pvc-cpu-capture-and-king/1");
            yield return null;
            // Both original empty-cell destinations are exposed from +X. Keep
            // the fixture position and moves, and orbit through normal input.
            string fixtureHash=session.State.Hash;var fixtureMoves=session.QueryLegal().Moves.Select(m=>m.Key).ToArray();
            PivotGesture(new Vector2((-90-yaw)/.25f,pitch/.20f));
            SmokeCheck(Mathf.Abs(yaw+90)<.001f&&Mathf.Abs(pitch)<.001f&&session.State.Hash==fixtureHash&&session.QueryLegal().Moves.Select(m=>m.Key).SequenceEqual(fixtureMoves),
                "PvC declared side camera exposes original destinations without changing position or moves","simulated public pointer drag; fixed fixture camera");
            Record("SmokeFixtureCamera","PvC original move sequence;yaw="+yaw+";pitch="+pitch);
            var humanCamera=rig.Camera.transform.position;
            yield return SmokeHumanMove("a-king",new QN(-2,0,0,0),"PvC A first UI move");
            yield return SmokeWait(()=>session.State.Ply>=2||session.IsFinished,"PvC CPU pawn capture",30);
            SmokeCheck(session.State.Find("a-pawn")==null&&session.State.CapturedBy(Side.B).Count==1&&session.State.Find("b-rook").Position==new QN(0,1,0,0),"PvC CPU captures actual A pawn into B tray");
            SmokeCheck(visuals.CountFor(Side.B)==1&&visuals.CountFor(Side.A)==0,"actual captured-piece render model shows B=1 A=0");
            SmokeCheck(visuals.CapturedLabel(Side.B,"pawn-step")=="×1","B captured pawn count label is actual UI text ×1");
            yield return SmokeWait(()=>session.CanHumanAct,"PvC A next turn");
            SmokeCheck((rig.Camera.transform.position-humanCamera).sqrMagnitude<1e-8,"PvC keeps human A viewpoint after CPU reply");yield return SmokeCapture("pvc-captured-B");
            yield return SmokeHumanMove("a-king",new QN(-1,1,0,0),"PvC A second UI move");
            yield return SmokeWait(()=>session.IsFinished,"PvC CPU royal capture",30);
            SmokeCheck(session.State.Outcome==MatchOutcome.KingCapture&&session.State.Winner==Side.B&&session.State.Ply==4,"PvC reaches normal CPU king-capture outcome");
            SmokeCheck(session.State.CapturedBy(Side.B).Count==1,"PvC captured king does not become a drop inventory");yield return SmokeTerminalFreeze("PvC");yield return SmokeCapture("pvc-result");
            oldId=session.Snapshot.MatchId;SmokeAction("next-match");yield return null;SmokeCheck(session.Snapshot.MatchId!=oldId&&session.Snapshot.InitialState.Pieces.Count==40,"PvC next-match starts with preserved next-game settings");SmokeAction("match-menu");

            SmokeFixture(MatchMode.CvC,new[]{SP("a-king",Side.A,PieceKind.King,-2,-2),SP("a-rook",Side.A,PieceKind.Rook,0,-1),SP("b-pawn",Side.B,PieceKind.Pawn,1,2),SP("b-king",Side.B,PieceKind.King,0,1)},"smoke-cvc-natural-royal-capture/1");
            yield return SmokeWait(()=>session.IsFinished,"CvC natural royal capture",30);
            SmokeCheck(session.State.Outcome==MatchOutcome.KingCapture&&session.State.Winner==Side.A&&session.State.Ply==1,"CvC computes and commits king capture rather than replaying a scripted move");
            SmokeCheck(session.Events.Any(e=>e.Kind=="cpu-request")&&session.Events.Any(e=>e.Kind=="move"&&e.Detail.StartsWith("Cpu;",StringComparison.Ordinal)),"CvC terminal path contains real CPU request and common move commit");
            yield return SmokeTerminalFreeze("CvC");yield return SmokeCapture("cvc-result");
            oldId=session.Snapshot.MatchId;SmokeAction("next-match");yield return null;SmokeCheck(session.Snapshot.MatchId!=oldId&&session.Snapshot.InitialState.Pieces.Count==40,"CvC next-match creates fresh normal40 session");SmokeAction("match-menu");
        }
        IEnumerator SmokeTerminalFreeze(string mode)
        {
            string hash=session.State.Hash;var clock=session.Clock;int ply=session.State.Ply;
            SmokeCheck(!session.CanHumanAct&&!session.CanCpuAct&&!clock.Running,mode+" terminal disables human CPU and clocks");
            yield return new WaitForSecondsRealtime(.35f);
            SmokeCheck(session.State.Hash==hash&&session.State.Ply==ply&&session.Clock.ASeconds==clock.ASeconds&&session.Clock.BSeconds==clock.BSeconds,mode+" terminal state and clocks stay frozen in real Windows time");
        }
        IEnumerator SmokeSteps2(string phase)
        {
            if(phase=="pivot"||phase=="pivot-reload"||phase=="pivot-recovery"||phase=="pivot-recovery-reload"){yield return PivotSteps(phase);yield break;}
            if(phase=="nd"||phase=="nd-reload"){yield return ReviewSteps(phase);yield break;}
            if(phase=="guide"){yield return GuideSteps();yield break;}
            if(phase=="lesson"){yield return LessonSmokeSteps();yield break;}
            yield return new WaitForSecondsRealtime(2);
            SmokeCheck(config!=null&&menu.gameObject.activeSelf&&!opened,"normal startup restores executable setup and opens menu");
            var names=Enumerable.Range(0,5).Select(i=>actions["menu-"+i].GetComponentInChildren<Text>().text).ToArray();
            SmokeCheck(names.SequenceEqual(new[]{"人間 vs 人間","人間 vs CPU","CPU vs CPU","オプション","終了"}),"menu has five approved labels in required order","actual uGUI text; current approved reference image labels");
            SmokeCheck(CfgUi.Font.HasCharacter('四')&&CfgUi.Font.HasCharacter('設')&&CfgUi.Font.HasCharacter('駒'),"existing OS font reports Japanese glyph availability","font API, not human readability");
            if(phase=="reload")
            {
                var expected=JsonUtility.FromJson<Cfg2RestartExpected>(File.ReadAllText(Path.Combine(runtimeRoot,"smoke-restart-expected.json")));
                SmokeCheck(config.SettingsHash==expected.settingsHash&&config.RuleHash==expected.ruleHash,"new process restores last valid executable settings without silent defaults");
                SmokeCheck(draft.Current.Definitions.Single(d=>d.Id=="king-step").MaxAxes==expected.kingAxes&&options.TimeSeconds==expected.timeSeconds,"new process restores edited rule and time raw values");
                SmokeAction("menu-3");yield return null;
                SmokeCheck(preview.Hash==expected.previewHash&&slice.Center.ToString()==expected.center,"new process restores independent trial and stable 4D center");
                var restored=CapturePivotView();
                SmokeCheck(restored.EmbeddingId==expected.embedding&&restored.Height==expected.height&&restored.HalfWindow==expected.halfWindow&&restored.CellScale==expected.cellScale&&
                    restored.Center.SequenceEqual(expected.center4)&&restored.Basis.SequenceEqual(expected.basis)&&restored.Anchor.SequenceEqual(expected.anchor)&&restored.Camera.SequenceEqual(expected.camera)&&
                    restored.SelectedPieceId==expected.selectedPiece&&restored.SelectedCell.ToString()==expected.selectedCell&&restored.PlaneReferenceCell.ToString()==expected.planeReference,
                    "new process restores complete embedded view center basis anchor camera window and selection from one bundle");
                yield return SmokeCapture("settings-reloaded");SmokeAction("back");SmokeAction("menu-0");yield return null;
                SmokeCheck(session.Snapshot.Config.SettingsHash==expected.settingsHash&&session.Snapshot.InitialState.Pieces.Count==40,"restored settings create a real next-game snapshot");
                yield return SmokeOneHumanMove("reloaded PvP A");yield return SmokeCapture("reloaded-real-match");SmokeAction("match-menu");yield break;
            }
            if(phase!="initial")throw new ArgumentException("Unknown smoke phase "+phase);
            yield return SmokeCapture("menu");SmokeAction("menu-3");yield return null;
            SmokeCheck(opened&&planeMinis.Count(m=>m.gameObject.activeInHierarchy)==6&&categories.Length==6&&planeMinis.Take(4).Select(m=>m.Name).SequenceEqual(new[]{"XY","XZ","XW","YZ"}),"settings opens with six categories and the six 4D plane diagrams starting XY XZ XW YZ","simulated options button");
            foreach(int i in Enumerable.Range(0,6)){SmokeAction("category-"+i);yield return null;SmokeCheck(category==i,"settings category "+i+" is reachable","simulated category button");yield return SmokeCapture("settings-category-"+i);}
            SmokeAction("category-2");SelectDefinition("king-step");yield return null;
            foreach(int tab in Enumerable.Range(0,3)){SmokeAction("detail-tab-"+tab);yield return null;yield return SmokeCapture("settings-piece-detail-"+tab);}
            SmokeAction("detail-tab-0");yield return null;
            yield return SmokeCapture("piece-settings");
            yield return SmokeSettingsViews();
            // UI input keys are stable test hooks on the real editable controls.
            SmokeInput("max-axes","1");yield return null;
            SmokeCheck(config!=null&&preset.Find("king-step").MaxAxes==1&&result!=null&&result.G.Count==8,"real king-axis input updates executable config and shared preview G=8","simulated InputField events");
            string changedHash=config.SettingsHash;SmokeAction("back");yield return SmokeNormalModes();
            SmokeCheck(config.SettingsHash==changedHash,"all three normal modes retain edited next-game settings");
            SmokeAction("menu-3");SelectCategory(2);SelectDefinition("king-step");SmokeInput("max-axes","4");yield return null;
            SmokeCheck(config!=null&&result.G.Count==80,"real king-axis input restores G=80 for terminal fixtures","simulated InputField events");
            SmokeAction("back");yield return SmokeTerminalModes();
            yield return SmokeClockAndNoMoves();yield return SmokePreviewAndPersistence();
        }
        IEnumerator SmokeClockAndNoMoves()
        {
            OpenSettings();SelectCategory(3);yield return null;
            SmokeInput("time","0.3");yield return null;SmokeCheck(config!=null&&Math.Abs(config.TimeSeconds-.3)<1e-9,"short clock condition is explicitly configured through UI","simulated InputField; short test time, not demo default");
            SmokeFixture(MatchMode.PvP,new[]{SP("a-king",Side.A,PieceKind.King,-2,-2),SP("b-king",Side.B,PieceKind.King,2,2)},"smoke-real-time-loss/1");
            OnApplicationFocus(false);yield return SmokeWait(()=>session.IsFinished,"real monotonic timeout",5);OnApplicationFocus(true);
            SmokeCheck(session.State.Outcome==MatchOutcome.TimeLoss&&session.State.Winner==Side.B&&session.Clock.ASeconds==0,"real Windows elapsed time causes A time loss even after simulated focus callback","real monotonic Windows time + simulated application callback");
            yield return SmokeTerminalFreeze("TimeLoss");yield return SmokeCapture("time-loss-result");SmokeAction("match-menu");
            OpenSettings();SelectCategory(3);SmokeInput("time","3000000000");yield return null;
            SmokeCheck(config!=null&&config.TimeSeconds==3000000000d,"valid UI time above Int32 maximum remains an executable value","simulated InputField boundary value");
            SmokeAction("back");SmokeAction("menu-0");yield return null;
            long DisplayedSeconds(string text)
            {
                var token=text.Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Last();var tenths=token.Split('.');if(tenths.Length!=2||tenths[1].Length!=1||!char.IsDigit(tenths[1][0]))return -1;var values=tenths[0].Split(':');
                if(values.Length!=2||!long.TryParse(values[0],out var minutes)||!int.TryParse(values[1],out var seconds)||minutes<0||seconds<0||seconds>=60)return -1;
                return minutes*60+seconds;
            }
            long nearSeconds=DisplayedSeconds(clockNear.text),farSeconds=DisplayedSeconds(clockFar.text);
            Record("SmokeLargeClockText","near="+clockNear.text.Replace('\n',' ')+";far="+clockFar.text.Replace('\n',' '));
            SmokeCheck(session.Snapshot.InitialState.Pieces.Count==40&&farSeconds==3000000000L&&nearSeconds>=2999999990L&&nearSeconds<=3000000000L,"large valid clock renders nonnegative 50000000-minute range without Int32 overflow","actual clock Text parsing after simulated menu start");
            yield return SmokeCapture("large-clock-boundary");SmokeAction("match-menu");
            OpenSettings();SelectCategory(3);SmokeInput("time","600");yield return null;
            draft.Edit(d=>{var king=d.Definitions.Single(x=>x.Id=="king-step");king.AllowEmpty=false;king.AllowCapture=false;});AfterEdit();
            SmokeCheck(config!=null,"explicit no-move fixture settings validate","fixture setup through Draft and normal validation");
            SmokeFixture(MatchMode.PvP,new[]{SP("a-king",Side.A,PieceKind.King,-2,-2),SP("b-king",Side.B,PieceKind.King,2,2)},"smoke-complete-zero-moves/1");
            SmokeCheck(session.State.Outcome==MatchOutcome.NoMovesDraw&&session.State.Ply==0&&session.State.Winner==null,"complete zero-move initial state reaches normal no-moves draw");yield return SmokeCapture("no-moves-result");SmokeAction("match-menu");
            OpenSettings();draft.Edit(d=>{var king=d.Definitions.Single(x=>x.Id=="king-step");king.AllowEmpty=true;king.AllowCapture=true;});AfterEdit();yield return null;
        }
        IEnumerator SmokePreviewAndPersistence()
        {
            SelectCategory(2);SelectDefinition("king-step");yield return null;
            string trial=preview.Hash;var frame=slice.Frame;string rule=config.RuleHash;
            foreach(string axis in new[]{"Z","Y","X","W"})
            {
                RequestView(axis);yield return SmokeWait(()=>!slice.IsAnimating,"4D reference "+axis);
                SmokeCheck(preview.Hash==trial&&config.RuleHash==rule,"4D reference "+axis+" does not mutate settings or trial");
                SmokeCheck(planeMinis.All(m=>m.Revision==slice.ViewRevision),"four settings plane diagrams share current revision after "+axis);
            }
            // Stable trial fixture remains separate from all live match snapshots.
            preview.Place(new TrialPiece("smoke-friend","pawn-step",Side.A,new QN(0,1,0,0)));QueueQuery();RenderGeometry();
            SmokeCheck(preview.Find("smoke-friend")!=null&&config.RuleHash==rule,"independent trial edits do not modify executable rule config","explicit preview fixture setup");
            SmokeInput("max-axes","2");yield return null;
            SmokeCheck(result!=null&&result.G.Count==32,"final edited king setting produces shared G=32","simulated InputField events");
            yield return new WaitForSecondsRealtime(.4f);SaveStableView();
            SmokeCheck(File.Exists(validPath),"atomic pivot bundle contains valid next-match settings and trial view");
            var savedBundle=PivotSettingsStore.Load(validPath);var savedView=CapturePivotView();
            SmokeCheck(savedBundle.IsValidSettings&&savedBundle.LogicalHash==PivotSettingsStore.LogicalContentHash(draft,options)&&savedBundle.PreviewHash==preview.Hash&&savedBundle.View.Basis.SequenceEqual(savedView.Basis)&&savedBundle.View.Anchor.SequenceEqual(savedView.Anchor),"saved logical settings trial position and complete view belong to the same bundle");
            var expected=new Cfg2RestartExpected{settingsHash=config.SettingsHash,ruleHash=config.RuleHash,previewHash=preview.Hash,center=slice.Center.ToString(),kingAxes="2",timeSeconds=options.TimeSeconds,cpuDelay=options.CpuDelayMilliseconds,
                embedding=savedView.EmbeddingId,height=savedView.Height,halfWindow=savedView.HalfWindow,cellScale=savedView.CellScale,center4=savedView.Center,basis=savedView.Basis,anchor=savedView.Anchor,camera=savedView.Camera,
                selectedPiece=savedView.SelectedPieceId,selectedCell=savedView.SelectedCell.ToString(),planeReference=savedView.PlaneReferenceCell.ToString()};
            File.WriteAllText(Path.Combine(runtimeRoot,"smoke-restart-expected.json"),JsonUtility.ToJson(expected,true));
            byte[] validBytes=File.ReadAllBytes(validPath);SmokeInput("max-axes","invalid");yield return new WaitForSecondsRealtime(.4f);
            SmokeCheck(config==null&&draft.Current.Definitions.Single(d=>d.Id=="king-step").MaxAxes=="invalid","invalid raw input stays visible and disables new-game validation","simulated InputField events");
            SmokeCheck(File.Exists(draftPath)&&validBytes.SequenceEqual(File.ReadAllBytes(validPath)),"invalid autosave preserves last valid next-game file bytes");
            StartMatch(MatchMode.PvP);SmokeCheck(session==null&&opened,"invalid settings cannot start a new game using stale config","UI start API rejection");
            SmokeInput("max-axes","2");yield return new WaitForSecondsRealtime(.4f);SaveStableView();
            SmokeCheck(config.SettingsHash==expected.settingsHash,"corrected input returns to expected next-game settings");
            yield return SmokeCapture("settings-final");SmokeAction("back");yield return SmokeCapture("menu-final");
        }
        IEnumerator SmokeSettingsViews()
        {
            string hash=preview.Hash,ruleHash=config.RuleHash;var g=result.G.ToArray();var p=result.P.ToArray();
            SmokeToggle("攻撃範囲",true);yield return null;RenderGeometry();
            SmokeCheck(showAttacks&&previewRenderer.Attacks&&attackCells.Count>0&&previewRenderer.VisibleAttacks>0,"attack toggle connects full-4D attack query to actual section renderer","simulated uGUI Toggle + actual renderer");
            yield return SmokeCapture("settings-attack-overlay");
            SmokeToggle("移動候補",false);SmokeToggle("座標グリッド",false);SmokeToggle("攻撃範囲",false);yield return null;RenderGeometry();
            SmokeCheck(!previewRenderer.Candidates&&!previewRenderer.Grid&&!previewRenderer.Attacks&&preview.Hash==hash&&result.G.SequenceEqual(g)&&result.P.SequenceEqual(p),"display OFF removes rendering aids without removing pieces from engine queries","simulated uGUI Toggle + common query");
            SmokeToggle("移動候補",true);SmokeToggle("座標グリッド",true);SmokeToggle("攻撃範囲",true);
            preview.Place(new TrialPiece("plane-hidden","pawn-step",Side.B,new QN(0,0,0,1)));QueueQuery();yield return null;
            for(int tab=1;tab<=4;tab++)
            {
                // Fixed planes XY XZ XW YZ sit after the 断面/投影 tabs, so their buttons are plane-2..plane-5.
                SmokeAction("plane-"+(tab+1));yield return null;RenderGeometry();
                var source=preview.Find(selectedPiece).Position;
                int a=tab==4?2:1,b=tab==1?2:tab==3?3:0;
                bool InPlane(QN q)=>Enumerable.Range(0,4).All(i=>i==a||i==b||q[i]==source[i]);
                int expectedPieces=preview.Pieces.Count(item=>InPlane(item.Position));
                SmokeCheck(planeView.VisiblePieceCount==expectedPieces&&planeView.VisibleCandidates==result.P.Count(InPlane),"2D "+planeView.Name+" uses exact selected-piece fixed coordinates","actual 2D view model versus independent two-axis filter");
                SmokeCheck(!string.IsNullOrWhiteSpace(planeView.FixedCoordinates)&&planeMinis.Count(m=>m.gameObject.activeInHierarchy)==6,"2D "+planeView.Name+" shows hidden-axis condition and preserves the six plane diagrams");
                if(tab==1)SmokeCheck(!planeView.Contains(new QN(0,0,0,1)),"XY 2D confirmation excludes W-different piece rather than projecting it onto same cell");
                yield return SmokeCapture("settings-plane-"+planeView.Name);
            }
            yield return SmokePlaneInputs();
            SmokeAction("plane-0");preview.Erase(new QN(0,0,0,1));QueueQuery();yield return null;
            SmokeCheck(preview.Hash==hash&&config.RuleHash==ruleHash,"2D view exercise and explicit fixture cleanup preserve original trial/rules");
            preview.Place(new TrialPiece("trial-enemy","pawn-step",Side.B,new QN(0,-1,0,0)));AfterTrial();yield return null;
            string beforeCapture=preview.Hash;SmokeAction("tool-select");SmokeClickCell(new QN(0,-1,0,0),"independent trial capture");yield return null;
            SmokeCheck(preview.Find("trial-enemy")==null&&preview.Find("subject").Position==new QN(0,-1,0,0),"independent trial capture passes actual section pointer and shared P","simulated uGUI pointer + actual section picker");
            SmokeAction("trial-undo");yield return null;SmokeCheck(preview.Hash==beforeCapture,"trial Undo restores captured enemy and source cell","simulated Undo button");
            SmokeAction("trial-redo");yield return null;SmokeCheck(preview.Find("trial-enemy")==null,"trial Redo reapplies capture without touching match state","simulated Redo button");
            SmokeAction("trial-undo");preview.Erase(new QN(0,-1,0,0));AfterTrial();yield return null;
            SmokeCheck(preview.Hash==hash,"trial fixture cleanup restores original independent position");
            SmokeToggle("攻撃範囲",false);
        }
        IEnumerator SmokePlaneInputs()
        {
            SmokeAction("plane-2");yield return null;SmokeAction("tool-select");
            string original=preview.Hash;var from=preview.Find("subject").Position;var destination=new QN(0,1,0,0);
            SmokePlaneClick(destination,"2D independent trial move");yield return null;
            SmokeCheck(preview.Find("subject").Position==destination,"XY actual pointer commits selected trial piece through shared P","simulated uGUI pointer + actual fixed-plane picker");
            SmokeAction("trial-undo");yield return null;SmokeCheck(preview.Hash==original,"2D move Undo restores exact independent trial hash","simulated Undo button");
            var placed=new QN(0,2,2,0);SmokeAction("side");SmokeAction("tool-place");SmokePlaneClick(placed,"2D B placement");yield return null;
            SmokeCheck(preview.At(placed)?.Side==Side.B&&preview.At(placed)?.DefinitionId==selectedDefinition,"XY actual pointer places chosen definition for selected side B","simulated side/tool buttons + actual fixed-plane picker");
            SmokeAction("side");SmokeAction("tool-erase");SmokePlaneClick(placed,"2D erase");yield return null;
            SmokeCheck(preview.At(placed)==null,"XY actual pointer erases the intended fixed-plane cell","simulated erase tool + actual fixed-plane picker");
            SmokeAction("trial-undo");yield return null;SmokeCheck(preview.At(placed)!=null,"2D erase Undo restores placed piece");
            SmokeAction("trial-redo");yield return null;SmokeCheck(preview.Hash==original,"2D erase Redo returns to original independent trial hash");
            SmokeAction("trial-clear");yield return null;SmokeCheck(preview.Pieces.Count==0,"clear button empties independent trial only","simulated clear button");
            SmokeAction("trial-undo");yield return null;SmokeCheck(preview.Hash==original,"clear Undo restores complete independent trial");
            SmokeAction("tool-select");
            int rejected=planeView.RejectedStaleCount;var pointer=SmokePlaneDown(destination,"2D stale state test");
            preview.Place(new TrialPiece("stale-fixture","pawn-step",Side.B,new QN(0,0,0,2)));AfterTrial();string changed=preview.Hash;
            SmokePlaneUp(pointer);yield return null;
            SmokeCheck(planeView.RejectedStaleCount==rejected+1&&preview.Hash==changed&&preview.Find("subject").Position==from,"2D down/up rejects changed trial revision even with unchanged 4D view","explicit trial fixture edit between simulated pointer down/up");
            preview.Erase(new QN(0,0,0,2));AfterTrial();yield return null;
            rejected=planeView.RejectedStaleCount;pointer=SmokePlaneDown(destination,"2D stale draft test");string name=draft.Name;
            draft.Edit(d=>d.Name=name+" smoke revision");AfterEdit();SmokePlaneUp(pointer);yield return null;
            SmokeCheck(planeView.RejectedStaleCount==rejected+1&&preview.Hash==original&&preview.Find("subject").Position==from,"2D down/up rejects changed draft revision without moving the trial piece","explicit next-game Draft edit between simulated pointer down/up");
            draft.Edit(d=>d.Name=name);AfterEdit();yield return null;
            SmokeCheck(preview.Hash==original&&ownership.Owner==InputOwner.None,"2D input tests leave exact trial state and release input ownership");
            yield return SmokeCapture("settings-2d-input-complete");
        }
    }
}
