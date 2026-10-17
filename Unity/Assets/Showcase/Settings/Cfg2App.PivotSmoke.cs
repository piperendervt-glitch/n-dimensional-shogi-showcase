using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
        [Serializable] sealed class PivotEvidenceState
        {
            public string label,positionHash,logicalHash,selectedPiece;public double height,halfWindow,cellScale,maxPixelError;
            public double[] center,basis,anchor,camera;public int visiblePieces,fixedBoardPieces,meshVertices;
            public string[] pieceIds,miniConditions;public long viewRevision,cameraRevision,layoutRevision;
        }
        PivotEvidenceState PivotDump(string label)
        {
            var s=CapturePivotView();return new PivotEvidenceState{label=label,positionHash=session?.State.Hash??preview.Hash,
                logicalHash=PivotSettingsStore.LogicalContentHash(draft,options),selectedPiece=s.SelectedPieceId,height=s.Height,halfWindow=s.HalfWindow,cellScale=s.CellScale,
                center=s.Center,basis=s.Basis,anchor=s.Anchor,camera=s.Camera,visiblePieces=previewRenderer.VisiblePieces,fixedBoardPieces=visuals.VisibleBoardPieces,
                pieceIds=previewRenderer.PieceSections.Keys.OrderBy(x=>x).ToArray(),miniConditions=minis.Select(m=>m.FixedCoordinates).ToArray(),
                viewRevision=slice.ViewRevision,cameraRevision=cameraRevision,layoutRevision=layoutRevision};
        }
        void WritePivotDump(string label)
        {File.WriteAllText(Path.Combine(smokeImageRoot,label+"-state.json"),JsonUtility.ToJson(PivotDump(label),true));}
        static bool SameNumbers(double[] a,double[] b,double epsilon=1e-8)=>a.Length==b.Length&&a.Zip(b,(x,y)=>Math.Abs(x-y)).All(x=>x<=epsilon);
        bool SamePivot(PivotEvidenceState a,PivotEvidenceState b)=>SameNumbers(a.center,b.center)&&SameNumbers(a.basis,b.basis)&&SameNumbers(a.anchor,b.anchor)&&SameNumbers(a.camera,b.camera)&&a.height==b.height&&a.halfWindow==b.halfWindow&&a.cellScale==b.cellScale;
        Vector3[] PieceWorldVertices(string id)
        {var v=previewRenderer.PieceSections[id];return v.Object.GetComponent<MeshFilter>().sharedMesh.vertices.Select(p=>v.Object.transform.TransformPoint(p)).ToArray();}
        static double MeshSetError(Vector3[] a,Vector3[] b)
        {if(a.Length==0||b.Length==0)return double.PositiveInfinity;return Math.Max(a.Max(p=>b.Min(q=>Vector3.Distance(p,q))),b.Max(p=>a.Min(q=>Vector3.Distance(p,q))));}
        void CheckMeshCoordinates(string label,bool expectEmpty=false)
        {
            var source=session!=null&&!opened?session.ToPreview():preview;var f=slice.Frame;var chart=slice.DisplayState;
            double worst=0;int count=0;
            foreach(var item in source.Pieces)
            {
                var view=previewRenderer.PieceSections[item.Id];var section=view.Last;var mesh=view.Object.GetComponent<MeshFilter>().sharedMesh;
                SmokeCheck(mesh.vertexCount==(section.Status==SectionStatus.Volume3D?section.Vertices.Length:0),label+" "+item.Id+" one section mesh including absence");
                if(section.Status!=SectionStatus.Volume3D){SmokeCheck(!view.Renderer.enabled,label+" "+item.Id+" absent color depth and shadow renderer");continue;}
                var vertices=mesh.vertices;int forward=item.Side==Side.A?1:-1;
                for(int i=0;i<vertices.Length;i++)
                {
                    var r=section.Vertices[i].LocalPosition;
                    // Independent equation from original local solid coordinates,
                    // not a second call to renderer.MapPoint/Frame.Project.
                    var q=new VecN(item.Position.F+r.X*forward,item.Position.U+r.Y*forward,item.Position.V+chart.EmbeddingHeight+r.Z,item.Position.W+r.W)-f.Center;
                    double u=q.X*f.AxisX.X+q.Y*f.AxisX.Y+q.Z*f.AxisX.Z+q.W*f.AxisX.W;
                    double v=q.X*f.AxisY.X+q.Y*f.AxisY.Y+q.Z*f.AxisY.Z+q.W*f.AxisY.W;
                    double w=q.X*f.AxisZ.X+q.Y*f.AxisZ.Y+q.Z*f.AxisZ.Z+q.W*f.AxisZ.W;
                    var expected=new Vector3((float)(chart.Anchor.X+chart.CellScale*v),(float)(chart.Anchor.Y+chart.CellScale*w),(float)(chart.Anchor.Z+chart.CellScale*u));
                    var actual=view.Object.transform.TransformPoint(vertices[i]);
                    worst=Math.Max(worst,Vector2.Distance(rig.Camera.WorldToScreenPoint(expected),rig.Camera.WorldToScreenPoint(actual)));count++;
                }
                SmokeCheck(view.Renderer.enabled&&view.Object.GetComponents<MeshRenderer>().Length==1,label+" "+item.Id+" single renderer for color depth shadows reflections");
            }
            if(expectEmpty)
            {
                SmokeCheck(source.Pieces.Count==40&&source.Pieces.All(x=>Math.Abs(x.Position.F)>.36)&&Math.Abs(f.Normal.X+1)<1e-9&&Math.Abs(f.Center.X)<1e-9,label+" independent empty XYW fixture: all F extents exclude F=0");
                SmokeCheck(count==0&&previewRenderer.VisiblePieces==0&&!previewRenderer.PickPieces(rig.Camera.ScreenPointToRay(new Vector2(Screen.width/2,Screen.height/2)),slice.ViewRevision,out _,out _),label+" expected empty slice removes all piece triangles shadow casters and picks");
            }
            else SmokeCheck(count>0&&worst<.05,label+" independent local-solid to screen coordinate error < 0.05 px ("+worst.ToString("R")+")","actual Player mesh vertices versus independent coordinate equation");
            if(source.Find(selectedPiece)!=null&&previewRenderer.TryGetCellSection(source.Find(selectedPiece).Position,out var cellSection))
            {
                var grid=GameObject.Find("CFG1 exact cell sections");var gridMesh=grid.GetComponent<MeshFilter>().sharedMesh;var gridColors=gridMesh.colors;
                var actualLines=gridMesh.vertices.Where((x,i)=>Math.Abs(gridColors[i].r-1)<.001f&&Math.Abs(gridColors[i].g-.76f)<.001f&&Math.Abs(gridColors[i].b-.26f)<.001f).Select(x=>grid.transform.TransformPoint(x)).ToArray();
                SmokeCheck(actualLines.Length>0,label+" actual selected-color frame lines exist");
                var cell=source.Find(selectedPiece).Position;
                foreach(var vertex in cellSection.Vertices)
                {
                    var q=new VecN(cell.F+vertex.LocalPosition.X,cell.U+vertex.LocalPosition.Y,cell.V+chart.EmbeddingHeight+vertex.LocalPosition.Z,cell.W+vertex.LocalPosition.W)-f.Center;
                    var expected=new Vector3((float)(chart.Anchor.X+chart.CellScale*VecN.Dot(q,f.AxisY)),(float)(chart.Anchor.Y+chart.CellScale*VecN.Dot(q,f.AxisZ)),(float)(chart.Anchor.Z+chart.CellScale*VecN.Dot(q,f.AxisX)));
                    SmokeCheck(actualLines.Any(x=>Vector3.Distance(x,expected)<1e-6),label+" actual selected-color frame vertex shares independent C/B/A transform");
                }
            }
            var dump=PivotDump(label);dump.maxPixelError=worst;dump.meshVertices=count;File.WriteAllText(Path.Combine(smokeImageRoot,label+"-mesh.json"),JsonUtility.ToJson(dump,true));
        }
        void PivotGesture(Vector2 delta,bool control=false,bool shift=false,PointerEventData.InputButton button=PointerEventData.InputButton.Left,bool cancel=false,int partitions=1)
        {
            var rect=SmokeBoardSurface;var center=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var e=SmokePointer2(center,551,button);BeginPointer(e,control,shift);
            for(int i=1;i<=partitions;i++){e.position=center+delta*canvas.scaleFactor*i/partitions;PreviewDrag(e);}
            if(cancel)ownership.Cancel();else PreviewUp(e);RenderGeometry();
            Record("PivotSimulatedGesture","control="+control+";shift="+shift+";button="+button+";canvasDelta="+delta+";parts="+partitions+";cancel="+cancel);
        }
        IEnumerator PivotSteps(string phase)
        {
            yield return new WaitForSecondsRealtime(2);
            if(phase=="pivot-recovery"||phase=="pivot-recovery-reload")
            {yield return PivotRecoverySteps(phase);yield break;}
            SmokeCheck(config!=null&&startupLoadError=="","PV17 real Windows startup and valid logical settings");
            if(phase=="pivot-reload")
            {
                var expected=JsonUtility.FromJson<PivotEvidenceState>(File.ReadAllText(Path.Combine(runtimeRoot,"pivot-restart-expected.json")));
                var actual=PivotDump("reloaded-before-ui");
                SmokeCheck(SamePivot(expected,actual)&&expected.positionHash==actual.positionHash&&expected.logicalHash==actual.logicalHash&&expected.selectedPiece==actual.selectedPiece,"PV13 separate Player process restores arbitrary basis C A h camera selection and bound trial");
                OpenSettings();yield return null;RenderGeometry();CheckMeshCoordinates("reloaded-general");WritePivotDump("reloaded-general");yield return SmokeCapture("pivot-reloaded");
                var matchExpected=JsonUtility.FromJson<PivotEvidenceState>(File.ReadAllText(Path.Combine(runtimeRoot,"pivot-match-restart-expected.json")));
                StartMatch(MatchMode.PvP);yield return null;
                SmokeCheck(RestoreSavedMatchView(File.ReadAllText(Path.Combine(runtimeRoot,"pivot-match-restart-file.txt"))),"PV13 separate process loads position-bound match display");RenderGeometry();
                var matchActual=PivotDump("match-reloaded");SmokeCheck(SamePivot(matchExpected,matchActual)&&matchExpected.positionHash==matchActual.positionHash,"PV13 match display arbitrary C B A camera reload leaves logical position unchanged");
                CheckMeshCoordinates("match-reloaded");yield return SmokeCapture("match-reloaded");
                yield break;
            }
            yield return SmokeCapture("approved-menu");OpenSettings();yield return null;RenderGeometry();
            var initial=slice.DisplayState;var p=new VecN(0,0,3,0);
            SmokeCheck((slice.Center-p).Length<1e-9&&initial.EmbeddingHeight==3&&initial.HalfWindow==4.5,"PV01 initial C=P height=3 and finite half-window=4.5");
            SmokeCheck(Math.Abs(initial.Anchor.Y-SectionView.Anchor.y)<1e-8,"PV01 initial A is the wooden board surface, so pieces at Y=h rest on it");
            SmokeCheck(visuals.VisibleBoardPieces==0&&previewRenderer.VisiblePieces==preview.Pieces.Count,"PV06 settings uses only one real piece section path");
            SmokeCheck(planeMinis.Select(x=>x.Plane).SequenceEqual(new[]{Cfg2Plane.XY,Cfg2Plane.XZ,Cfg2Plane.XW,Cfg2Plane.YZ}),"PV12 settings right views are XY XZ XW YZ 2D diagrams");
            CheckMeshCoordinates("initial-P");WritePivotDump("initial-P");yield return SmokeCapture("approved-settings");
            string hash=preview.Hash,logic=config.RuleHash;var gp=result.P.ToArray();var before=PieceWorldVertices("subject");
            slice.SetCenter(default);RenderGeometry();var after=PieceWorldVertices("subject");
            SmokeCheck(MeshSetError(before,after)<1e-6,"PV02 XYZ P to O keeps actual piece mesh at height h");
            SmokeCheck((slice.Center-default(VecN)).Length<1e-10&&Math.Abs(slice.DisplayState.Anchor.Y-(SectionView.Anchor.y-3*SectionView.Cell))<1e-7,"PV02 C and A return to O together, s*h below the board");
            WritePivotDump("origin-O");yield return SmokeCapture("origin-O");
            slice.SetOrientation(SliceAxes.Reference(ReferenceView.XZW,slice.Center));RenderGeometry();
            SmokeCheck(previewRenderer.VisiblePieces==0&&previewRenderer.PieceSections.Values.All(v=>v.Last.Status==SectionStatus.Empty&&v.Object.GetComponent<MeshFilter>().sharedMesh.vertexCount==0),"PV04 YW90 at O has no initial elevated piece, including stale triangles");
            WritePivotDump("YW90-at-O-empty");yield return SmokeCapture("YW90-at-O-empty");
            slice.Restore(initial);slice.SetOrientation(SliceAxes.Reference(ReferenceView.XZW,slice.Center));RenderGeometry();
            SmokeCheck(previewRenderer.VisiblePieces==1,"PV04 YW90 at P actually intersects the initial piece");CheckMeshCoordinates("YW90-at-P");
            slice.Restore(initial);slice.SetOrientation(SliceAxes.Reference(ReferenceView.YZW,slice.Center));RenderGeometry();
            SmokeCheck(previewRenderer.VisiblePieces==1&&Math.Abs(slice.Frame.Normal.Y+1)<1e-10,"PV03 XW90 point condition X=0 and real wooden solid intersection");
            before=PieceWorldVertices("subject");var anchorBefore=slice.DisplayState.Anchor;
            slice.Translate(LogicalAxis.W,.75);RenderGeometry();after=PieceWorldVertices("subject");
            Record("TangentMeshInvariant","beforeVertices="+before.Length+";afterVertices="+after.Length+";bidirectionalError="+MeshSetError(before,after).ToString("R")+";anchorDelta="+(slice.DisplayState.Anchor.X-anchorBefore.X).ToString("R"));
            SmokeCheck(MeshSetError(before,after)<1e-6&&Math.Abs(slice.DisplayState.Anchor.X-anchorBefore.X-.75*SectionView.Cell)<1e-7,"PV05 XW90 W tangent shift updates A in display X and preserves actual mesh point set independent of triangle ordering");
            CheckMeshCoordinates("XW90-W-shift");SmokeCheck(SmokePointForCell2(default,out _),"PV06 exact known mesh triangle pick after XW90 W tangent shift");WritePivotDump("XW90-W-shift");yield return SmokeCapture("XW90-W-shift");
            slice.Restore(initial);RenderGeometry();var poseStart=PivotDump("before-drag");
            PivotGesture(new Vector2(117,-63),true,false,partitions:1);var one=PivotDump("one-event");slice.Restore(initial);RenderGeometry();
            PivotGesture(new Vector2(117,-63),true,false,partitions:17);var many=PivotDump("seventeen-events");
            SmokeCheck(SameNumbers(one.basis,many.basis)&&SameNumbers(one.anchor,many.anchor)&&SameNumbers(one.center,many.center),"PV11 Ctrl drag uses initial pose and total displacement independent of event partition","simulated modifiers through common pointer handler, not OS input");
            SmokeCheck(slice.StableView==null,"PV09 general 4D pose is not labelled as a reference pose");
            CheckMeshCoordinates("general-rotation");SmokeCheck(SmokePointForCell2(default,out _),"PV06 exact known mesh triangle pick at composite arbitrary pose");WritePivotDump("general-rotation");yield return SmokeCapture("general-rotation");
            var keep=PivotDump("cancel-start");PivotGesture(new Vector2(80,40),true,true,cancel:true);
            SmokeCheck(SamePivot(keep,PivotDump("cancel-end")),"PV10 cancellation restores C B A and camera together");
            var basisBefore=slice.Frame;PivotGesture(new Vector2(70,30));
            SmokeCheck((slice.Frame.AxisY-basisBefore.AxisY).Length<1e-12&&cameraRevision>one.cameraRevision&&preview.Hash==hash,"PV11 ordinary left drag changes camera only and never commits a move");
            var cameraBefore=CapturePivotView().Camera;PivotGesture(new Vector2(30,-20),false,true);
            SmokeCheck(!SameNumbers(cameraBefore,CapturePivotView().Camera),"PV11 Shift left drag pans camera");
            PivotGesture(new Vector2(-20,10),button:PointerEventData.InputButton.Middle);
            var scroll=SmokePointer2(Vector2.zero);scroll.scrollDelta=new Vector2(0,1);float dist=distance;PreviewScroll(scroll);
            SmokeCheck(distance<dist,"PV11 wheel zoom changes camera distance");
            SmokeCheck(preview.Hash==hash&&config.RuleHash==logic&&result.P.SequenceEqual(gp),"PV07 every display gesture preserves logical position rule hash and full-4D P");
            slice.Restore(initial);yaw=32;pitch=29;distance=.82f;pan=Vector3.zero;UpdateCamera();RenderGeometry();
            // Existing real UI pick, using a single known mesh point and no surrounding search.
            selectedPiece="";QueueQuery();RenderGeometry();int pickTrace=eventTrace.Count;
            SmokeClickCell(default,"PV06 exact subject pick");yield return null;
            SmokeCheck(selectedPiece=="subject"&&eventTrace.Skip(pickTrace).Any(t=>t.Contains("CellPick")),"PV06 actual uGUI down/up changes unselected state to subject through real section triangles");
            foreach(var view in new[]{ReferenceView.XYZ,ReferenceView.XYW,ReferenceView.XZW,ReferenceView.YZW})
            {slice.Restore(initial);slice.SetOrientation(SliceAxes.Reference(view,slice.Center));RenderGeometry();CheckMeshCoordinates("trial-"+view);SmokeCheck(SmokePointForCell2(default,out _),"PV06 exact known mesh triangle pick at trial "+view);}
            slice.Restore(initial);slice.SetOrientation(SliceRotations.RotateDrag(slice.Frame,45,0));RenderGeometry();CheckMeshCoordinates("trial-45");SmokeCheck(SmokePointForCell2(default,out _),"PV06 exact known mesh triangle pick at 45 degrees");
            slice.Restore(initial);RenderGeometry();yield return PivotInputChecks();
            var screen=RectTransformUtility.WorldToScreenPoint(null,previewRect.TransformPoint(previewRect.rect.center));var stale=SmokePointer2(screen,702);
            BeginPointer(stale,false,false);layoutRevision++;int traces=eventTrace.Count;PreviewUp(stale);
            SmokeCheck(eventTrace.Skip(traces).Any(t=>t.Contains("StalePointerRejected")),"PV10 stale layout pointer is rejected");
            SmokeAction("back");SmokeAction("menu-0");yield return null;RenderGeometry();
            SmokeCheck(previewRenderer.VisiblePieces==40&&visuals.VisibleBoardPieces==0&&visuals.VisibleBoardCandidates==0,"PV06 normal40 play has 40 single elevated sections and zero fixed-board copies");
            CheckMeshCoordinates("play-XYZ");WritePivotDump("play-XYZ");yield return SmokeCapture("approved-play");
            var tips=minis.Select(m=>m.AxisScreenDirection(LogicalAxis.X)).ToArray();var oldCamera=cameraRevision;
            PivotGesture(new Vector2(90,45));RefreshAuxiliaryViews();
            SmokeCheck(gizmos.All(g=>g.CameraRevision==cameraRevision)&&minis.All(m=>m.CameraRevision==cameraRevision)&&cameraRevision>oldCamera&&minis.Select((m,i)=>(m.AxisScreenDirection(LogicalAxis.X)-tips[i]).sqrMagnitude).Any(x=>x>1e-4),"PV08 camera-only change refreshes main gizmos and all mini axes");
            string matchHash=session.State.Hash;var legal=session.QueryLegal().Moves.Select(m=>m.Key).ToArray();
            var matchStart=slice.DisplayState;
            foreach(var view in new[]{ReferenceView.XYZ,ReferenceView.XYW,ReferenceView.XZW,ReferenceView.YZW})
            {
                slice.Request(view);yield return SmokeWait(()=>!slice.IsAnimating,"pivot baseline "+view);RenderGeometry();
                SmokeCheck(minis.All(m=>m.ViewRevision==slice.ViewRevision)&&minis.Single(m=>m.Reference==view).IsCoincident,"PV09 mini finite coincidence follows actual "+view+" pose");
                CheckMeshCoordinates("play-"+view,view==ReferenceView.XYW);WritePivotDump("play-"+view);
            }
            SmokeCheck(session.State.Hash==matchHash&&session.QueryLegal().Moves.Select(m=>m.Key).SequenceEqual(legal),"PV07 play rotations preserve match hash and complete moves");
            slice.Restore(matchStart);slice.SetOrientation(SliceRotations.RotateDrag(slice.Frame,17,-8));slice.SetCenter(slice.Center+slice.Frame.AxisX*.2);RenderGeometry();
            SmokeCheck(SaveStableView(),"PV13 match display saves with the actual match configuration and position");
            File.WriteAllText(Path.Combine(runtimeRoot,"pivot-match-restart-expected.json"),JsonUtility.ToJson(PivotDump("match-saved-general"),true));
            File.WriteAllText(Path.Combine(runtimeRoot,"pivot-match-restart-file.txt"),"pivot-match-"+session.Snapshot.MatchId+".xml");
            slice.Restore(matchStart);RenderGeometry();manualCamera=true;var manualPosition=rig.Camera.transform.position;
            yield return SmokeOneHumanMove("pivot manual-camera preservation");yield return SmokeWait(()=>session.CanHumanAct,"manual camera handover");
            SmokeCheck((rig.Camera.transform.position-manualPosition).sqrMagnitude<1e-10,"PV11 manual camera survives subsequent real turn handover");
            ReturnFromMatch();OpenSettings();yield return null;
            slice.SetOrientation(SliceRotations.RotateDrag(slice.Frame,33,-19));slice.SetCenter(slice.Center+slice.Frame.AxisY*.4+slice.Frame.Normal*.05);
            yaw=48;pitch=24;distance=.9f;pan=new Vector3(.03f,.02f,-.01f);UpdateCamera();RenderGeometry();
            SmokeCheck(SaveStableView(),"PV13 arbitrary pose and moved C/A are saved atomically with settings and preview");
            File.WriteAllText(Path.Combine(runtimeRoot,"pivot-restart-expected.json"),JsonUtility.ToJson(PivotDump("saved-general"),true));
            WritePivotDump("saved-general");yield return SmokeCapture("saved-general");
        }
    }
}
