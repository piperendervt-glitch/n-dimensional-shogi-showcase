using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // --cfg2-smoke nd, then --cfg2-smoke nd-reload on the same fresh --cfg2-data: N changes with Undo/Redo, destroyed selections,
    // the hyperplane panels, a 3D board and saves that fail. Fixtures are labelled; nothing here claims physical input.
    public sealed partial class Cfg2App
    {
        int reviewExceptions;string reviewException="";
        void ReviewLog(string text,string stack,LogType type){if(type==LogType.Exception){reviewExceptions++;reviewException=text;}}
        void ReviewNoExceptions(int before,string name)=>SmokeCheck(reviewExceptions==before,name+(reviewExceptions==before?"":"; "+(reviewExceptions-before)+" exception(s), last: "+reviewException),"Application.logMessageReceived");
        // Everything outside a match that must follow the draft's N: trial board, cells, view and (in Options) the header.
        bool ReviewViewOn(int n)=>DraftDimensions==n&&preview.Bounds.Dimensions==n&&trialSlice.Frame.Dimensions==n&&selectedCell.Dimensions==n&&planeReferenceCell.Dimensions==n&&
            (!opened||ReferenceEquals(slice,trialSlice)&&headerDimensions.text==n+"次元");
        void ReviewSaved(int n,string label)
        {
            var b=PivotSettingsStore.Load(validPath);
            SmokeCheck(b.IsValidSettings&&b.Draft.Current.Dimensions==n.ToString()&&b.Preview.Bounds.Dimensions==n&&b.View.Center.Length==n&&b.LogicalHash==PivotSettingsStore.LogicalContentHash(draft,options)&&b.PreviewHash==preview.Hash,
                label+": the saved bundle pairs N="+n+" settings with an N="+n+" trial board and view","PivotSettingsStore.Load of the file on disk");
        }
        // Press, release and click on one control, as uGUI delivers a mouse click.
        void ReviewClick(Selectable control,string purpose)
        {
            var r=(RectTransform)control.transform;var e=SmokePointer2(RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center)),174);e.pointerPress=e.rawPointerPress=control.gameObject;
            ExecuteEvents.Execute(control.gameObject,e,ExecuteEvents.pointerDownHandler);ExecuteEvents.Execute(control.gameObject,e,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(control.gameObject,e,ExecuteEvents.pointerClickHandler);
            Record("SmokeSimulatedClick",purpose);
        }
        bool ReviewNothingSelected=>EventSystem.current==null||ReferenceEquals(EventSystem.current.currentSelectedGameObject,null);
        // Leaves the EventSystem holding a destroyed object, as a selected control that a rebuild destroyed used to, and lets Update run over it.
        IEnumerator ReviewDestroyedSelection(string label)
        {
            var es=EventSystem.current;var temp=new GameObject("Smoke destroyed selection",typeof(RectTransform));temp.transform.SetParent(canvas.transform,false);
            es.SetSelectedGameObject(temp);DestroyImmediate(temp);int before=reviewExceptions;
            SmokeCheck(!ReferenceEquals(es.currentSelectedGameObject,null)&&es.currentSelectedGameObject==null,label+": fixture leaves a destroyed object selected","explicit fixture: select, then DestroyImmediate");
            yield return null;yield return null;
            SmokeCheck(CfgUi.Selected==null&&CfgUi.SelectedInput==null&&!CfgUi.EditingText,label+": the selection helpers read a destroyed selection as none (H1)");
            ReviewNoExceptions(before,label+": Update runs over a destroyed selection without exceptions (H1)");
        }
        bool ReviewFrame(Cfg2MiniView mini,Color? axis)
        {
            var f=Cfg2Ui.Frame(mini.transform);if(f==null||!f.enabled||!f.Glow)return false;
            var c=axis.HasValue?new Color(axis.Value.r,axis.Value.g,axis.Value.b,.7f):new Color(.80f,.87f,1,.55f);
            return Mathf.Abs(f.color.r-c.r)+Mathf.Abs(f.color.g-c.g)+Mathf.Abs(f.color.b-c.b)+Mathf.Abs(f.color.a-c.a)<.01f;
        }
        // A swap panel's frame has the colour of the hidden axis it brings in; the current section's panel is plain glass.
        Color? ReviewAdded(Cfg2MiniView mini){var t=slice.TargetSection;var added=mini.Reference.Shown.Where(a=>!t.Shows(a)).ToArray();return added.Length==0?(Color?)null:Cfg2Projection.AxisColor(added[0]);}
        IEnumerator ReviewSteps(string phase)
        {
            yield return new WaitForSecondsRealtime(2);Application.logMessageReceived+=ReviewLog;
            if(phase=="nd-reload"){yield return ReviewReload();yield break;}
            SmokeCheck(config!=null&&startupLoadError==""&&DraftDimensions==4&&draft.Current.InitialLayout==DraftData.FlatLayout&&menu.gameObject.activeSelf,"nd starts on valid 4D flat settings (run it on a fresh --cfg2-data name)");
            string time=options.TimeSeconds,budget=options.CpuBudget;int start=reviewExceptions;

            // (a) Options: an N button, Undo/Redo, the CPU budget and leaving with 戻る.
            SmokeAction("menu-3");yield return null;SmokeAction("category-1");yield return null;
            SmokeCheck(new[]{"dimensions-5","category-1","back","menu-0","plane-0"}.All(k=>actions[k].navigation.mode==Navigation.Mode.None),"buttons have no keyboard navigation, so a click never leaves one selected (H1, L9)");
            ReviewClick(actions["dimensions-5"],"N=5 button");yield return null;
            SmokeCheck(ReviewViewOn(5)&&ReviewNothingSelected,"clicking N=5 moves board, view and header to 5D and leaves nothing selected (H1, H4)","simulated uGUI pointer down/up/click");
            yield return ReviewDestroyedSelection("Options at N=5");
            HistoryKey(false);yield return null;SmokeCheck(ReviewViewOn(4),"Ctrl+Z of the N change brings board, view and header back to 4D (H4)","HistoryKey: the Ctrl+Z handler");
            HistoryKey(true);yield return null;SmokeCheck(ReviewViewOn(5),"Ctrl+Y redoes the N change on board, view and header (H4)","HistoryKey: the Ctrl+Y handler");
            SmokeAction("category-5");yield return null;
            string v=SliceAxes.AxisName(LogicalAxis.A5);PickRotationAxis(LogicalAxis.A5);
            SmokeCheck(rotationAxis==LogicalAxis.A5&&helpText.text.Contains("Ctrl＋左：X"+v+"／Y"+v+"回転"),"the help names the picked rotation axis (L10)","PickRotationAxis: the 5 key handler");
            HistoryKey(false);yield return null;
            SmokeCheck(ReviewViewOn(4)&&rotationAxis==LogicalAxis.W&&helpText.text.Contains("Ctrl＋左：XW／YW回転"),"an N change by Undo returns the rotation axis and its help to W (L10)");
            SmokeAction("category-4");yield return null;SmokeInput("budget","abc");SmokeAction("category-1");yield return null;SmokeAction("dimensions-9");yield return null;
            SmokeCheck(ReviewViewOn(9)&&options.CpuBudget=="abc","a 9D N change leaves an unparsable CPU budget for the player to correct (L3)","simulated InputField + N button");
            HistoryKey(false);yield return null;
            SmokeAction("category-4");yield return null;SmokeInput("budget","1");SmokeAction("category-1");yield return null;SmokeAction("dimensions-9");yield return null;
            SmokeCheck(ReviewViewOn(9)&&options.CpuBudget=="5000000","a 9D N change raises a valid budget that is too small (L3)","simulated InputField + N button");
            HistoryKey(false);yield return null;SmokeAction("category-4");yield return null;SmokeInput("budget",budget);SmokeAction("category-1");yield return null;
            SmokeCheck(ReviewViewOn(4)&&config!=null&&options.CpuBudget==budget,"budget fixture restored on valid 4D settings");
            SmokeAction("dimensions-5");yield return null;HistoryKey(false);yield return null;SmokeAction("back");yield return null;
            SmokeCheck(!opened&&menu.gameObject.activeSelf&&!settingsDirty,"after Ctrl+Z of an N change 戻る leaves Options and saves (H4)","simulated 戻る button");
            ReviewSaved(4,"Ctrl+Z then 戻る");

            // An N button, then Esc while a destroyed object is selected.
            SmokeAction("menu-3");yield return null;SmokeAction("category-1");yield return null;
            ReviewClick(actions["dimensions-5"],"N=5 button");yield return null;
            yield return ReviewDestroyedSelection("Options before Esc");
            float yaw0=yaw;PivotGesture(new Vector2(60,0));
            SmokeCheck(Mathf.Abs(yaw-yaw0)>1,"a drag on the preview still turns the view over a destroyed selection (H1)","simulated public pointer drag");
            EscapePressed();yield return null;
            SmokeCheck(!opened&&menu.gameObject.activeSelf&&ReviewNothingSelected,"Esc leaves Options and the menu drops the destroyed selection (H1)","EscapePressed: the Esc handler");
            ReviewSaved(5,"N=5 then Esc");

            // (b) A 5D match: panels kept when the section or the rotation axis changes, a panel title click, then a board click.
            SmokeAction("menu-0");yield return null;yield return null;
            SmokeCheck(session!=null&&session.Config.Dimensions==5&&minis.Count==7&&minisLabels.Count==2&&guideText.text.Contains("Ctrl"),"a 5D match builds seven hyperplane panels for its own space (H2)");
            SmokeCheck(minis.All(m=>ReviewFrame(m,ReviewAdded(m))),"5D panels: glass frame on the current section, axis-coloured frames on swap panels (L1)");
            var built=minis.ToArray();PickRotationAxis(LogicalAxis.A5);yield return null;yield return null;
            SmokeCheck(minisActive==4&&minis.SequenceEqual(built)&&minis[1].Reference.Shows(4)&&minis.All(m=>ReviewFrame(m,ReviewAdded(m))),"a new rotation axis re-aims the same panels instead of rebuilding them (L8)");
            var notes=hudNotice.rectTransform;
            SmokeCheck(hudNotice.isActiveAndEnabled&&hudNotice.text==notice&&notes.anchoredPosition.x+notes.sizeDelta.x<=394&&-notes.anchoredPosition.y+notes.sizeDelta.y<=872,"the rotation-axis notice shows in the match's left column, clear of board and panels (M5)");
            var target=minis[1].Reference;int before=reviewExceptions;
            ReviewClick(minis[1].GetComponentsInChildren<Button>().First(),"panel title "+target.Name);
            yield return SmokeWait(()=>!slice.IsAnimating&&!slice.QueuedSection.HasValue&&slice.TargetSection==target,"section "+target.Name,10);
            SmokeCheck(minis.SequenceEqual(built)&&minis[0].Reference==target&&ReviewNothingSelected,"a panel title click turns to its section, keeps the panels and selects nothing (H1, L8)","simulated uGUI pointer down/up/click");
            ReviewNoExceptions(before,"no exception after the panel title click (H1)");
            EventSystem.current.SetSelectedGameObject(minis[2].GetComponentsInChildren<Button>().First().gameObject);RebuildMinis(true);
            SmokeCheck(ReviewNothingSelected&&!minis.Intersect(built).Any(),"a forced rebuild drops a selection held inside the panels it destroys (H1)","explicit fixture selection");
            slice.Request(ReferenceSection.Initial(5));geometryDirty=true;
            yield return SmokeWait(()=>!slice.IsAnimating&&!slice.QueuedSection.HasValue,"initial 5D section",10);
            before=reviewExceptions;yield return SmokeOneHumanMove("5D PvP A after a panel title click");ReviewNoExceptions(before,"board click after the panel title click (H1)");
            // Small 5D panel titles are new to the layout check: their clipping is reported, not failed.
            yield return SmokeCapture("nd-5d-match",false);
            var revisions=minis.Select(m=>m.CameraRevision).ToArray();SmokeAction("match-menu");yield return null;
            SmokeCheck(rotationAxis==LogicalAxis.W,"leaving a match returns the rotation axis to W (L10)");
            SmokeAction("menu-3");yield return null;UpdateCamera();yield return null;yield return null;
            SmokeCheck(minis.Select(m=>m.CameraRevision).SequenceEqual(revisions),"hidden match panels are not redrawn while Options move the camera (L8)");
            SmokeAction("back");yield return null;

            // (c) 4D with the Y-copy layout: the camera follows the raised anchor; a match display that cannot be saved holds nothing up.
            SmokeAction("menu-3");yield return null;SmokeAction("category-1");yield return null;SmokeAction("dimensions-4");yield return null;
            SmokeAction("layout-"+DraftData.CopyLayout(2).Replace('/','-'));yield return null;
            var camera=rig.Camera.transform.position;UpdateCamera();
            SmokeCheck(ReviewViewOn(4)&&CurrentCopyAxis==2&&Mathf.Abs((float)slice.DisplayState.Anchor.Y-AnchorFor(2).y)<1e-4f&&(rig.Camera.transform.position-camera).sqrMagnitude<1e-8f,
                "the Y-copy layout raises the anchor and the camera already follows it (L4)","simulated layout button; UpdateCamera recomputation");
            SmokeAction("category-3");yield return null;SmokeInput("time","0.5");SmokeAction("back");yield return null;
            SmokeAction("menu-0");yield return null;
            SmokeCheck(session!=null&&session.Config.Dimensions==4&&session.Snapshot.InitialState.Pieces.Count==360&&minis.Count==4&&minis.All(m=>ReviewFrame(m,null))&&guideText.text.Contains("Ctrl"),
                "4D Y-copy match: 360 pieces, four glass-framed panels, full guide (L1)");
            string blocked=Path.Combine(runtimeRoot,"pivot-match-"+session.Snapshot.MatchId+".xml");Directory.CreateDirectory(blocked);
            SmokeCheck(!SaveStableView()&&notice.StartsWith("表示の保存に失敗",StringComparison.Ordinal)&&hudNotice.text==notice,"a failing match display save is reported in the match HUD (M5)","explicit fixture: a directory where the display file goes");
            yield return SmokeWait(()=>session.IsFinished,"0.5 s clock runs out",15);
            string first=session.Snapshot.MatchId;SmokeAction("next-match");yield return null;
            SmokeCheck(session!=null&&session.Snapshot.MatchId!=first&&matchHud.gameObject.activeSelf,"次の対局 starts although the finished match's display cannot be saved (H3)","simulated 次の対局 button");
            Directory.Delete(blocked);
            // Checked at the end, so the steps after it still run when it fails.
            bool copySaved=SaveStableView();Record("SmokeCopyViewSaved",copySaved+"; "+notice);
            SmokeAction("match-menu");yield return null;
            SmokeAction("menu-3");yield return null;SmokeAction("category-3");yield return null;SmokeInput("time",time);SmokeAction("category-1");yield return null;
            SmokeAction("layout-"+DraftData.FlatLayout);yield return null;camera=rig.Camera.transform.position;UpdateCamera();
            SmokeCheck(ReviewViewOn(4)&&CurrentCopyAxis==null&&config!=null&&(rig.Camera.transform.position-camera).sqrMagnitude<1e-8f,"the flat layout lowers the anchor and the camera follows (L4)");
            SmokeAction("back");yield return null;ReviewSaved(4,"flat 4D restored");
            if(CpuShowEnabled)
            {
                SmokeFixture(MatchMode.CvC,new[]{SP("a-king",Side.A,PieceKind.King,-2,-2),SP("a-rook",Side.A,PieceKind.Rook,0,-1),SP("b-pawn",Side.B,PieceKind.Pawn,1,2),SP("b-king",Side.B,PieceKind.King,0,1)},"smoke-nd-final-glide/1");
                yield return SmokeWait(()=>session.IsFinished,"CvC final capture",30);
                SmokeCheck(glide!=null,"the move that ends a CPU game keeps its glide after the game ends (L2)","real CPU move; glide one frame after the end");
                yield return SmokeWait(()=>glide==null,"final glide lands",5);SmokeAction("match-menu");yield return null;
            }
            else Record("SmokeSkipped","L2 final glide: the CPU move presentation is off in this machine's preferences");

            // (d) A 3D board: Options, a match and the way back.
            SmokeAction("menu-3");yield return null;SmokeAction("category-1");yield return null;SmokeAction("dimensions-3");yield return null;RenderGeometry();SelectPlane(4);
            SmokeCheck(ReviewViewOn(3)&&!actions["plane-4"].gameObject.activeSelf&&planeTab==0,"3D Options: board, view and header in 3D, no XW plane (H2)");
            PickRotationAxis(LogicalAxis.W);
            SmokeCheck(rotationAxis==LogicalAxis.W&&notice.Contains("軸はありません"),"a 3D board refuses a W rotation axis with a notice (H2)");
            SmokeAction("category-5");yield return null;
            SmokeCheck(!helpText.text.Contains("Ctrl＋左")&&!helpText.text.Contains("隠れ軸"),"the 3D help leaves out Ctrl rotation (L10)");
            yield return SmokeCapture("nd-settings-3d");
            SmokeAction("back");yield return null;ReviewSaved(3,"3D Options left");
            SmokeAction("menu-0");yield return null;yield return null;
            SmokeCheck(session!=null&&session.Config.Dimensions==3&&slice.Frame.Dimensions==3&&minis.Count==1&&minisLabels.Count==0&&ReviewFrame(minis[0],null)&&!guideText.text.Contains("Ctrl"),
                "a 3D match shows one panel for the current section and a guide without Ctrl rotation (H2)");
            yield return SmokeCapture("nd-match-3d");
            yield return SmokeOneHumanMove("3D PvP A");
            SmokeAction("match-menu");yield return null;
            SmokeCheck(menu.gameObject.activeSelf&&session==null&&ReviewViewOn(3),"a 3D match returns to the menu with the 3D trial view (H2)");

            // (e) Closing the window during a match whose display cannot be saved. Last: the app is quitting afterwards.
            SmokeAction("menu-0");yield return null;
            blocked=Path.Combine(runtimeRoot,"pivot-match-"+session.Snapshot.MatchId+".xml");Directory.CreateDirectory(blocked);
            bool closes=PrepareShutdown();
            SmokeCheck(closes&&session==null&&eventTrace.Any(r=>r.Contains("\tQuitWithoutMatchView\t")),"closing the window is not held up by a match display that cannot be saved (H3)","PrepareShutdown: the wantsToQuit handler; explicit fixture directory");
            Directory.Delete(blocked);ReviewSaved(3,"window closed");
            SmokeCheck(copySaved,"the 360-piece Y-copy match display was saved (needs the core trial-piece cap raised above 360)");
            ReviewNoExceptions(start,"nd raised no exception");
        }
        IEnumerator ReviewReload()
        {
            int start=reviewExceptions;
            SmokeCheck(config!=null&&startupLoadError==""&&menu.gameObject.activeSelf&&ReviewViewOn(3)&&slice.Frame.Dimensions==3,"a new process restores the 3D settings, trial board and view that nd saved (H2, H4)");
            SmokeAction("menu-0");yield return null;yield return null;
            SmokeCheck(session!=null&&session.Config.Dimensions==3&&minis.Count==1,"the restored 3D settings start a 3D match");
            yield return SmokeOneHumanMove("reloaded 3D PvP A");SmokeAction("match-menu");yield return null;
            SmokeAction("menu-3");yield return null;SmokeAction("category-1");yield return null;SmokeAction("dimensions-4");yield return null;
            SmokeCheck(ReviewViewOn(4),"4D again from the restored 3D settings");
            SmokeAction("back");yield return null;ReviewSaved(4,"nd-reload back on 4D");
            ReviewNoExceptions(start,"nd-reload raised no exception");
        }
    }
}
