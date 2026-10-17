using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg1App
    {
        [Serializable] sealed class SmokeReport
        {public string status="RUNNING",phase,error,input="simulated uGUI events / actual Windows Player",unity,device,ruleHash,trialHash;public int width,height;public float dpi;public List<string> checks=new List<string>();public double sectionMilliseconds;public long managedBytes;}
        SmokeReport smoke;
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("SMOKE: "+name);smoke.checks.Add(name);Record("SmokePass",name);}
        IEnumerator WaitQuery()
        {float started=Time.realtimeSinceStartup;while(pendingQuery!=null){if(Time.realtimeSinceStartup-started>30)throw new TimeoutException("Query timed out");yield return null;}yield return null;}
        IEnumerator WaitSlice()
        {float started=Time.realtimeSinceStartup;while(slice.IsAnimating){if(Time.realtimeSinceStartup-started>20)throw new TimeoutException("Slice timed out");yield return null;}yield return null;}
        IEnumerator Capture(string name)
        {yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(runRoot,"images",name+".png"));yield return null;}
        IEnumerator Smoke()
        {
            string phase=Arg("--cfg1-smoke-phase","initial");smoke=new SmokeReport{phase=phase,unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height,dpi=Screen.dpi};
            var routine=SmokeSteps(phase);bool failed=false;
            // Flatten nested coroutines so failures are captured in the same report.
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            while(stack.Count>0)
            {
                object current=null;bool more=false;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
                catch(Exception e){smoke.error=e.ToString();smoke.status="FAIL";failed=true;Debug.LogException(e);break;}
                if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(!failed)smoke.status="PASS";smoke.ruleHash=preset?.RuleHash;smoke.trialHash=preview.Hash;smoke.sectionMilliseconds=previewRenderer.LastBuildMilliseconds;smoke.managedBytes=GC.GetTotalMemory(false);
            string suffix=phase+"-"+Screen.width+"x"+Screen.height;
            File.WriteAllText(Path.Combine(runRoot,"smoke-"+suffix+".json"),JsonUtility.ToJson(smoke,true));FlushTrace();
            Record("SmokeFinished",smoke.status);yield return Capture("smoke-"+suffix+"-final");Application.Quit(failed?1:0);
        }
        // These events exercise the actual uGUI handlers. They are simulated dispatch,
        // not physical mouse/keyboard evidence. Scene setup and cleanup are explicit.
        Vector2 SmokeScreenCenter(RectTransform rect)
        {return RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));}
        PointerEventData SmokePointer(Vector2 point,int id=71,PointerEventData.InputButton button=PointerEventData.InputButton.Left)
        {return new PointerEventData(EventSystem.current){pointerId=id,button=button,position=point,pressPosition=point};}
        bool SmokePointForCell(Q4 cell,out Vector2 screen)
        {
            screen=default;var frame=slice.Frame;
            var logical=CfgPreviewRenderer.V(cell)+new Vec4(0,0,.08,0);
            var world=CfgPreviewRenderer.AirAnchor+SectionView.MapVector(frame.Project(logical))*SectionView.Cell;
            var viewport=rig.Camera.WorldToViewportPoint(world);if(viewport.z<=0)return false;
            var rect=previewRect.rect;
            var origin=RectTransformUtility.WorldToScreenPoint(null,previewRect.TransformPoint(new Vector3(rect.xMin+viewport.x*rect.width,rect.yMin+viewport.y*rect.height,0)));
            // A bounded search around the projected cell checks the real mesh picker;
            // no logical cell is substituted for the actual hit result.
            for(int radius=0;radius<=8;radius++)for(int x=-radius;x<=radius;x++)for(int y=-radius;y<=radius;y++)
            {
                if(radius>0&&Math.Abs(x)!=radius&&Math.Abs(y)!=radius)continue;
                var point=origin+new Vector2(x,y)*6*UiScale;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(previewRect,point,null,out var local);
                float u=(local.x-rect.xMin)/rect.width,v=(local.y-rect.yMin)/rect.height;if(u<=0||u>=1||v<=0||v>=1)continue;
                if(!previewRenderer.Pick(rig.Camera.ViewportPointToRay(new Vector3(u,v,0)),slice.ViewRevision,out var hit,true)||hit!=cell)continue;
                var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(SmokePointer(point),rays);
                if(rays.Count==0||rays[0].gameObject!=previewRect.gameObject)continue;
                screen=point;return true;
            }
            return false;
        }
        void SmokeClickCell(Q4 cell,string purpose)
        {
            if(!SmokePointForCell(cell,out var point))throw new InvalidOperationException("SMOKE: no visible pickable pixel for "+purpose+" "+cell);
            var e=SmokePointer(point);e.pointerCurrentRaycast=new RaycastResult{gameObject=previewRect.gameObject,screenPosition=point};
            ExecuteEvents.Execute(previewRect.gameObject,e,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(previewRect.gameObject,e,ExecuteEvents.pointerUpHandler);
            Record("SmokeSimulatedPointer",purpose+" cell="+cell+" screen="+point);
        }
        IEnumerator SmokeBoardInputPaths()
        {
            string before=preview.Hash;var source=preview.Find("subject").Position;
            SmokeClickCell(preview.Find("friend").Position,"select same-side wooden piece");yield return WaitQuery();
            Check(selectedPiece=="friend"&&selectedDefinition=="pawn-step","simulated uGUI pointer selects a real wooden piece and its definition");
            SmokeClickCell(source,"reselect subject");yield return WaitQuery();
            Check(selectedPiece=="subject"&&selectedDefinition=="king-step","simulated uGUI pointer restores subject selection");
            Q4 destination=default;bool found=false;
            foreach(var candidate in result.P.Where(q=>preview.At(q)==null&&q.W==0))if(SmokePointForCell(candidate,out _)){destination=candidate;found=true;break;}
            if(!found)throw new InvalidOperationException("SMOKE: no visible empty P candidate for actual picker");
            SmokeClickCell(destination,"P trial movement");yield return WaitQuery();
            Check(preview.Find("subject").Position==destination&&preview.Pieces.Count==3,"simulated uGUI pointer P movement changes the selected trial piece");
            actions["trial-undo"].onClick.Invoke();yield return WaitQuery();
            Check(preview.Hash==before,"simulated trial Undo button restores pointer movement");
            SmokeClickCell(preview.Find("enemy").Position,"P enemy capture");yield return WaitQuery();
            Check(preview.Find("enemy")==null&&preview.Pieces.Count==2,"simulated uGUI pointer captures an enemy through the real pick and P handler");
            actions["trial-undo"].onClick.Invoke();yield return WaitQuery();
            Check(preview.Hash==before&&selectedPiece=="subject","simulated trial Undo button restores pointer capture");
        }
        IEnumerator SmokeCameraAndMiniInputs()
        {
            var saved=CaptureView();string logical=preview.Hash,rule=preset.RuleHash;var frame=slice.Frame;var candidates=result.G.ToArray();
            var point=SmokeScreenCenter(previewRect);var cameraBefore=rig.Camera.transform.position;
            var orbit=SmokePointer(point,81,PointerEventData.InputButton.Right);
            ExecuteEvents.Execute(previewRect.gameObject,orbit,ExecuteEvents.pointerDownHandler);orbit.position+=new Vector2(25,-16);
            ExecuteEvents.Execute(previewRect.gameObject,orbit,ExecuteEvents.dragHandler);ExecuteEvents.Execute(previewRect.gameObject,orbit,ExecuteEvents.pointerUpHandler);
            Check(rig.Camera.transform.position!=cameraBefore&&ownership.Owner==InputOwner.None,"simulated uGUI right drag rotates the camera and releases ownership");
            var panBefore=pan;var move=SmokePointer(point,82,PointerEventData.InputButton.Middle);
            ExecuteEvents.Execute(previewRect.gameObject,move,ExecuteEvents.pointerDownHandler);move.position+=new Vector2(18,12);
            ExecuteEvents.Execute(previewRect.gameObject,move,ExecuteEvents.dragHandler);ExecuteEvents.Execute(previewRect.gameObject,move,ExecuteEvents.pointerUpHandler);
            Check(pan!=panBefore,"simulated uGUI middle drag pans the camera");
            float distanceBefore=distance;var wheel=SmokePointer(point,83);wheel.scrollDelta=new Vector2(0,1);ExecuteEvents.Execute(previewRect.gameObject,wheel,ExecuteEvents.scrollHandler);
            Check(distance<distanceBefore,"simulated uGUI wheel zooms the camera");
            Check(preview.Hash==logical&&preset.RuleHash==rule&&(slice.Frame.Center-frame.Center).Length<1e-12&&(slice.Frame.Normal-frame.Normal).Length<1e-12&&result.G.SequenceEqual(candidates),"camera pointer events preserve 4D frame logical coordinates and common candidates");
            var cancelPosition=rig.Camera.transform.position;var cancel=SmokePointer(point,84,PointerEventData.InputButton.Right);
            ExecuteEvents.Execute(previewRect.gameObject,cancel,ExecuteEvents.pointerDownHandler);cancel.position+=new Vector2(20,20);ExecuteEvents.Execute(previewRect.gameObject,cancel,ExecuteEvents.dragHandler);
            OnApplicationFocus(false);ExecuteEvents.Execute(previewRect.gameObject,cancel,ExecuteEvents.pointerUpHandler);
            Check((rig.Camera.transform.position-cancelPosition).sqrMagnitude<1e-10&&ownership.Owner==InputOwner.None,"simulated focus loss rolls back an active camera pointer drag");
            RestoreView(saved);yield return null;
            var origin=slice.Center;Vec4 firstDelta=default;
            for(int index=0;index<2;index++)
            {
                var handle=minis[index].GetComponentsInChildren<CfgPointerSurface>().Single(h=>h.name=="Drag world X");
                var basis=SliceAxes.Reference((ReferenceView)index);var worldAxis=SliceAxes.WorldAxis(LogicalAxis.X);
                var projected=CfgMiniGraphic.Project(new Vec3(Vec4.Dot(worldAxis,basis.AxisX),Vec4.Dot(worldAxis,basis.AxisY),Vec4.Dot(worldAxis,basis.AxisZ))*1.4).normalized;
                var e=SmokePointer(SmokeScreenCenter((RectTransform)handle.transform),90+index);
                ExecuteEvents.Execute(handle.gameObject,e,ExecuteEvents.pointerDownHandler);
                Check(ownership.Owner==InputOwner.Axis&&ownership.PointerId==e.pointerId,"simulated mini "+index+" axis starts actual pointer ownership");
                // Move far outside the handle across other UI, retaining its captured origin.
                e.position+=projected*(float)AxisDrag.PixelsPerCell+new Vector2(-projected.y,projected.x)*180;
                e.pointerEnter=previewRect.gameObject;e.pointerCurrentRaycast=new RaycastResult{gameObject=previewRect.gameObject,screenPosition=e.position};
                ExecuteEvents.Execute(handle.gameObject,e,ExecuteEvents.dragHandler);
                float lockedDistance=distance;var blocked=SmokePointer(point,99,PointerEventData.InputButton.Right);
                ExecuteEvents.Execute(previewRect.gameObject,blocked,ExecuteEvents.pointerDownHandler);blocked.scrollDelta=new Vector2(0,1);ExecuteEvents.Execute(previewRect.gameObject,blocked,ExecuteEvents.scrollHandler);
                Check(ownership.Owner==InputOwner.Axis&&ownership.PointerId==e.pointerId&&distance==lockedDistance,"simulated axis crossing keeps owner and blocks camera input "+index);
                ExecuteEvents.Execute(handle.gameObject,e,ExecuteEvents.pointerUpHandler);
                var delta=slice.Center-origin;Check((delta-worldAxis).Length<1e-5&&ownership.Owner==InputOwner.None,"simulated mini "+index+" axis completes the same one-cell world command after crossing");
                if(index==0)firstDelta=delta;else Check((delta-firstDelta).Length<1e-5,"two mini windows produce the same world-axis delta");
                RestoreCenter(origin);yield return null;
            }
            var focusHandle=minis[0].GetComponentsInChildren<CfgPointerSurface>().Single(h=>h.name=="Drag world X");
            var focus=SmokePointer(SmokeScreenCenter((RectTransform)focusHandle.transform),95);
            ExecuteEvents.Execute(focusHandle.gameObject,focus,ExecuteEvents.pointerDownHandler);focus.position+=new Vector2(40,20);ExecuteEvents.Execute(focusHandle.gameObject,focus,ExecuteEvents.dragHandler);
            OnApplicationFocus(false);ExecuteEvents.Execute(focusHandle.gameObject,focus,ExecuteEvents.pointerUpHandler);
            Check((slice.Center-origin).Length<1e-12&&ownership.Owner==InputOwner.None,"simulated mini handler focus loss rolls back and rejects late pointer-up");
            Check(preview.Hash==logical&&preset.RuleHash==rule,"all simulated mini handler events preserve trial state and rule hash");
            RestoreView(saved);yield return null;
        }
        void SmokeMalformedLoadPreservation()
        {
            string folder=Path.Combine(runtimeRoot,"smoke-fault-inputs");Directory.CreateDirectory(folder);
            string badView=Path.Combine(folder,"outside-camera.xml"),empty=Path.Combine(folder,"empty-definitions-draft.xml");
            var document=XDocument.Load(savePath);XNamespace v="urn:ndshogi:cfg1:view/1";
            var camera=document.Root.Element(v+"view").Attribute("camera").Value.Split(',');camera[2]="99";document.Root.Element(v+"view").SetAttributeValue("camera",string.Join(",",camera));document.Save(badView);
            var emptyData=draft.Current;emptyData.Definitions.Clear();SettingsStore.SaveDraft(empty,new SettingsDraft(emptyData),new PreviewSession(preview.Bounds),CaptureView());
            foreach(string path in new[]{badView,empty})
            {
                var oldDraft=draft;var oldPreview=preview;var oldResult=result;var oldFrame=slice.Frame;var oldCamera=rig.Camera.transform.position;string oldPath=savePath;byte[] bytes=File.ReadAllBytes(path);bool rejected=false;
                try{Load(path);}catch(ArgumentException){rejected=true;}
                Check(rejected&&ReferenceEquals(draft,oldDraft)&&ReferenceEquals(preview,oldPreview)&&ReferenceEquals(result,oldResult)&&(slice.Frame.Center-oldFrame.Center).Length<1e-12&&(slice.Frame.Normal-oldFrame.Normal).Length<1e-12&&rig.Camera.transform.position==oldCamera&&savePath==oldPath&&bytes.SequenceEqual(File.ReadAllBytes(path)),"UI Load boundary rejects "+Path.GetFileName(path)+" and preserves live state and source bytes");
            }
        }
        IEnumerator SmokeSteps(string phase)
        {
            yield return new WaitForSeconds(2);yield return WaitQuery();
            Check(!opened&&menu.gameObject.activeSelf,"five-item start menu active; settings not open");
            var actualMenu=menu.GetComponentsInChildren<Button>().OrderBy(b=>-((RectTransform)b.transform).anchoredPosition.y).Select(b=>b.GetComponentInChildren<Text>().text).ToArray();
            Check(actualMenu.SequenceEqual(new[]{"プレイヤー対プレイヤー","プレイヤー対CPU","CPU対CPU","オプション","ゲーム終了"}),"actual uGUI menu labels have all five required items in order");
            Check(FindObjectsByType<ShowcaseController>(FindObjectsSortMode.None).Length==0,"Legacy automatic match controller absent from new scene");
            Check(CfgUi.Font.HasCharacter('四')&&CfgUi.Font.HasCharacter('設')&&CfgUi.Font.HasCharacter('駒'),"OS font reports Japanese glyphs");
            if(phase=="initial")yield return Capture("menu-"+Screen.width+"x"+Screen.height);
            actions["menu-3"].onClick.Invoke();yield return WaitQuery();
            Check(opened&&minis.Count==4,"options opens directly with four 3D relation views");
            if(phase=="reload")
            {
                Load(Path.Combine(runtimeRoot,"smoke-preset.xml"));yield return WaitQuery();
                var expected=File.ReadAllLines(Path.Combine(runtimeRoot,"smoke-expected.txt"));
                Check(preset.RuleHash==expected[0]&&preview.Hash==expected[1],"cross-process rule and trial hashes restored");
                Check(slice.Frame.Center.ToString()==expected[2],"cross-process slice translation restored");
                Check(!draft.IsDirty&&draft.IsUnapplied&&!draft.CanApply,"saved remains unapplied after restart");
                Check(expected.Length>=5&&selectedPiece==expected[3]&&preview.Find(selectedPiece)?.DefinitionId==selectedDefinition&&result.G.Count.ToString()==expected[4],"cross-process editor selection and common query remain synchronized");
                yield return Capture("reload-"+Screen.width+"x"+Screen.height);yield break;
            }
            Check(result.Status==QueryStatus.Complete&&result.G.Count==80&&result.P.Count==80,"common query initial comparison fixture 80 G/P");
            Check(previewRenderer.VisibleG==26,"exact XYZ section contains 26 of 80 neighbors");
            yield return Capture("settings-initial-W0-wood-"+Screen.width+"x"+Screen.height);
            var cameraBefore=rig.Camera.transform.position;var centerBefore=slice.Frame.Center;
            var obsoleteQuery=result;
            actions["pattern-custom"].onClick.Invoke();yield return WaitQuery();Check(result.G.Count==32,"simulated UI edit changes common G to 32");
            Check(!queryGate.TryAccept(obsoleteQuery)&&ReferenceEquals(queryGate.Latest,result),"actual UI query gate rejects a delayed older DraftRevision result");
            yield return Capture("settings-max2-W0-wood-"+Screen.width+"x"+Screen.height);
            actions["pattern-axis"].onClick.Invoke();yield return WaitQuery();Check(result.G.Count==8,"simulated UI edit changes common G to 8");
            yield return Capture("settings-axis-only-W0-wood-"+Screen.width+"x"+Screen.height);
            Check(rig.Camera.transform.position==cameraBefore&&(slice.Frame.Center-centerBefore).Length<1e-12,"rule edits preserve camera and slice");
            actions["pattern-all"].onClick.Invoke();yield return WaitQuery();
            foreach(var runner in new[]{new {Id="rook-h11",Expected=16},new {Id="bishop-h22",Expected=48}})
            {
                actions["piece-"+runner.Id].onClick.Invoke();yield return WaitQuery();
                var distanceField=detailHost.GetComponentsInChildren<InputField>().Single();distanceField.text="2";yield return WaitQuery();
                Check(result.G.Count==runner.Expected&&result.P.Count==runner.Expected,"simulated runner distance edit "+runner.Id+" uses common G/P");
                actions["occlusion-supercover"].onClick.Invoke();yield return WaitQuery();
                Check(preset.OcclusionId=="supercover-closed/1"&&result.Status==QueryStatus.Complete,"simulated runner switches existing supercover version "+runner.Id);
                actions["occlusion-lattice"].onClick.Invoke();yield return WaitQuery();
            }
            actions["piece-king-step"].onClick.Invoke();yield return WaitQuery();
            var numeric=detailHost.GetComponentsInChildren<InputField>().Single();numeric.text="invalid";yield return WaitQuery();
            Check(preset==null&&result==null&&draft.Current.Definitions.First(d=>d.Id==selectedDefinition).MaxAxes=="invalid","live invalid input preserved and old query removed");
            SaveDraft();var invalid=SettingsStore.Load(Path.Combine(runtimeRoot,"draft.xml"));Check(invalid.Draft.Current.Definitions[0].MaxAxes=="invalid","invalid raw draft round trip");
            actions["save-as"].onClick.Invoke();yield return null;
            var copyName=modal.GetComponentInChildren<InputField>();copyName.text="smoke invalid draft copy";copyName.onEndEdit.Invoke(copyName.text);
            actions["confirm-save-as"].onClick.Invoke();yield return null;
            string invalidCopy=savePath;var invalidCopyBytes=File.ReadAllBytes(invalidCopy);
            Check(modal==null&&SettingsStore.Load(invalidCopy).Kind==SavedSettingsKind.Draft&&draft.Current.Definitions[0].MaxAxes=="invalid","simulated Save As dialog persists invalid raw input as a separate Draft ID");
            numeric.text="4";yield return WaitQuery();Check(result.G.Count==80,"valid live input resumes common query");
            actions["save"].onClick.Invoke();
            Check(savePath!=invalidCopy&&SettingsStore.Load(savePath).Kind==SavedSettingsKind.PreviewPreset&&invalidCopyBytes.SequenceEqual(File.ReadAllBytes(invalidCopy))&&!draft.IsDirty,"simulated normal Save after raw correction writes a Preset and preserves the invalid Draft");
            preview.Place(new TrialPiece("friend","pawn-step",Side.A,new Q4(0,1,0,0)));preview.Place(new TrialPiece("enemy","pawn-step",Side.B,new Q4(0,-1,0,0)));AfterTrial();yield return WaitQuery();
            Check(result.G.Count==80&&result.P.Count==79,"occupancy reduces P without changing G");
            yield return SmokeBoardInputPaths();
            Check(preview.TryMove(preset,"subject",new Q4(0,-1,0,0),out _)&&preview.Find("enemy")==null,"Core direct P trial capture updates only preview");
            preview.Undo();AfterTrial();yield return WaitQuery();Check(preview.Find("enemy")!=null,"Core direct trial capture undo restores enemy");
            yield return SmokeCameraAndMiniInputs();
            var logical=preview.Hash;var rule=preset.RuleHash;
            actions["axis-Z"].onClick.Invoke();yield return null;
            Check(slice.IsAnimating&&!slice.StableView.HasValue,"intermediate slice not labelled as reference");
            bool saveRejected=false;try{Save(false);}catch(InvalidOperationException){saveRejected=true;}Check(saveRejected,"saving intermediate rotation is rejected without overwriting");
            yield return new WaitForSeconds(.16f);yield return Capture("intermediate-"+Screen.width+"x"+Screen.height);yield return WaitSlice();
            Check(slice.StableView==ReferenceView.XYW,"Z reaches XYW");
            foreach(string a in new[]{"Y","X","W"}){actions["axis-"+a].onClick.Invoke();yield return WaitSlice();Check(slice.StableView==SliceAxes.ViewForAxis((LogicalAxis)Enum.Parse(typeof(LogicalAxis),a)),a+" reaches matching reference");}
            Translate(LogicalAxis.X,1);yield return null;Check((slice.Center-new Vec4(0,1,0,0)).Length<1e-10,"tangent translation moves finite window origin");
            Translate(LogicalAxis.W,1);yield return null;Check(slice.Center.W==1,"normal translation changes cutting hyperplane");
            actions["axis-Z"].onClick.Invoke();yield return WaitSlice();actions["axis-W"].onClick.Invoke();yield return WaitSlice();
            Check(slice.Center.W==1&&slice.Center.Y==1,"W return preserves translation");Check(preview.Hash==logical&&preset.RuleHash==rule,"all view operations preserve logical state and rule hash");
            var beforeDrag=slice.Center;var drag=new AxisDrag(slice,ownership);Check(drag.Begin(LogicalAxis.Y,55,0,0,0,30),"Core direct axis owner begins");drag.Update(55,0,32);OnApplicationFocus(false);Check((slice.Center-beforeDrag).Length<1e-12&&!drag.IsActive,"Core direct axis with UI focus loss rolls back world-axis drag");
            var pointer=new PointerEventData(EventSystem.current){pointerId=11,button=PointerEventData.InputButton.Left,position=new Vector2(800,500)};
            PreviewDown(pointer);draft.Edit(d=>d.Definitions[0].MaxAxes="2");PreviewUp(pointer);Check(notice.Contains("古いクリック"),"simulated direct UI handler rejects a stale draft pick");QueueQuery();yield return WaitQuery();
            actions["apply"].onClick.Invoke();Check(notice.Contains("未接続")&&!draft.CanApply,"product Apply remains blocked without mock mode");
            SaveScroll();float scroll=detailScroll.verticalNormalizedPosition;actions["category-4"].onClick.Invoke();actions["category-2"].onClick.Invoke();Check(Mathf.Abs(detailScroll.verticalNormalizedPosition-scroll)<.001,"detail scroll retained across category switch");
            Canvas.ForceUpdateCanvases();detailScroll.StopMovement();detailScroll.verticalNormalizedPosition=.23f;float editedScroll=detailScroll.verticalNormalizedPosition;
            actions["pattern-axis"].onClick.Invoke();yield return WaitQuery();Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(detailScroll.verticalNormalizedPosition-editedScroll)<.001,"same detail rebuild preserves a newly adjusted scroll position");
            actions["pattern-custom"].onClick.Invoke();yield return WaitQuery();
            savePath=Path.Combine(runtimeRoot,"smoke-preset.xml");Save(false);var savedHash=preset.RuleHash;var savedTrial=preview.Hash;var savedCenter=slice.Center;int savedG=result.G.Count;
            File.WriteAllLines(Path.Combine(runtimeRoot,"smoke-expected.txt"),new[]{savedHash,savedTrial,savedCenter.ToString(),selectedPiece,savedG.ToString()});
            draft.Edit(d=>d.Definitions[0].MaxAxes="1");preview.Clear();Load(savePath);yield return WaitQuery();
            Check(preset.RuleHash==savedHash&&preview.Hash==savedTrial&&(slice.Center-savedCenter).Length<1e-12,"save-load restores edited rules trial and view together");
            Check(!draft.IsDirty&&draft.IsUnapplied,"save clears unsaved only, never unapplied");
            Check(preview.Find(selectedPiece)?.DefinitionId==selectedDefinition&&result.G.Count==savedG,"reload keeps editor definition and queried trial piece synchronized");
            SmokeMalformedLoadPreservation();
            foreach(int i in Enumerable.Range(0,7)){actions["category-"+i].onClick.Invoke();yield return null;Check(category==categories[i],"category "+i+" reachable");}
            actions["category-2"].onClick.Invoke();yield return null;yield return Capture("settings-"+Screen.width+"x"+Screen.height);
        }
    }
}

