using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;

namespace Showcase.Unity.Settings
{
    // --cfg2-smoke lesson: the whole lecture taken as a player would, through the real UI wherever a click can reach. Each step is
    // checked to light what it talks about, to keep the caption off it and to let clicks through only there; moves a step does not
    // want are refused; skipping sets up each lesson's board; the lecture keeps no record and leaves the options alone.
    public sealed partial class Cfg2App
    {
        GameObject SmokeTopAt(Vector2 screen){var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(SmokePointer2(screen),rays);return rays.Count>0?rays[0].gameObject:null;}
        Rect SpotlightToScreen(Rect local)
        {var r=spotlight.rectTransform;Vector2 a=r.TransformPoint(local.min),b=r.TransformPoint(local.max);return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));}
        bool InCaption(GameObject top)=>top!=null&&top.transform.IsChildOf(lessonCaption);
        void SmokeLessonLight(LessonStep step)
        {
            SmokeCheck(lessonText.IndexOf(' ')<0&&PunctuationLineStarts(lessonBody)==0,"lesson "+step.Id+" has no ASCII space and starts no subtitle line with 、 。 or ）");
            var target=LessonTarget();
            if(!target.HasValue)
            {
                SmokeCheck(step.DimAll?spotlight.Dim>.5f&&!spotlight.HasHole:spotlight.Dim<.05f,"lesson "+step.Id+(step.DimAll?" dims the whole screen":" leaves the screen undimmed"));
                return;
            }
            var t=target.Value;var hole=SpotlightToScreen(spotlight.Hole);
            SmokeCheck(spotlight.HasHole&&spotlight.Dim>.5f&&hole.xMin<=t.xMin+2&&hole.yMin<=t.yMin+2&&hole.xMax>=t.xMax-2&&hole.yMax>=t.yMax-2,"lesson "+step.Id+" lights its target "+t+" (hole "+hole+")");
            var caption=ScreenRect(lessonCaption).Value;bool board=t.width>Screen.width*.4f;
            SmokeCheck(board||Overlap(caption,t)<1,"lesson "+step.Id+" keeps the caption off what it lights");
            // A click in the hole is not taken by the dim; one beside it is.
            var inside=new[]{t.center,new Vector2(t.center.x,t.yMax-t.height*.25f),new Vector2(t.center.x,t.yMin+t.height*.25f)}.Select(SmokeTopAt).ToArray();
            SmokeCheck(inside.Any(top=>top!=spotlight.gameObject&&!InCaption(top)),"lesson "+step.Id+" lets a click through the lit hole");
            var outside=new[]{new Vector2(Screen.width*.02f,Screen.height*.5f),new Vector2(Screen.width*.98f,Screen.height*.5f),new Vector2(Screen.width*.5f,Screen.height*.98f),new Vector2(Screen.width*.5f,Screen.height*.02f)}
                .Where(p=>!hole.Contains(p)&&!caption.Contains(p)).ToArray();
            SmokeCheck(outside.Length>0&&outside.All(p=>SmokeTopAt(p)==spotlight.gameObject),"lesson "+step.Id+" dims and blocks the rest of the screen");
        }
        void SmokeLessonButton(string id)
        {
            var r=(RectTransform)actions[id].transform;var top=SmokeTopAt(ScreenRect(r).Value.center);
            SmokeCheck(top!=null&&top.transform.IsChildOf(r),"lesson button "+id+" takes the click (top: "+(top!=null?top.name:"none")+")");
            SmokeAction(id);
        }
        // A blue cell the step would play that a real click reaches: nothing (the dim, the hint bar, the caption) is over it.
        QN SmokeLessonCell(Piece piece,LessonStep step)
        {
            RenderGeometry();var embedding=slice.DisplayState.Embedding;
            foreach(var q in result.P.Where(q=>step.Refuse?.Invoke(piece,q)==null&&InSlice(slice.Frame,CfgPreviewRenderer.V(q)+embedding)))
                if(SmokePointForCell2(q,out var point)&&SmokeTopAt(point)==matchSurface.gameObject)return q;
            throw new InvalidOperationException("CFG2 SMOKE no reachable lesson cell for "+step.Id);
        }
        void SmokeLessonClick(QN cell,string purpose)
        {
            Canvas.ForceUpdateCanvases();RenderGeometry();
            var top=SmokePointForCell2(cell,out var point)?SmokeTopAt(point):null;
            SmokeCheck(top==matchSurface.gameObject,purpose+": a real click on "+cell+" reaches the board (top: "+(top!=null?top.name:"none")+")");
            SmokeClickCell(cell,purpose);
        }
        void SmokeLessonRefused(QN q,string purpose,string says)
        {
            int ply=session.State.Ply;CommitCell(q,slice.ViewRevision,session.Revision,session.Generation);
            SmokeCheck(session.State.Ply==ply&&lessonSaid.Contains(says)&&hudNotice.text=="","lesson refuses "+purpose+" and says so in the caption only ("+lessonSaid+")");
        }
        IEnumerator SmokeLessonAct(LessonStep step)
        {
            var piece=session.State.Find(lessonPiece);
            switch(step.Id)
            {
                case "select-pawn":case "select-rook":case "select-king":case "win-select":
                    SmokeLessonClick(piece.Position,"lesson "+step.Id);break;
                case "move-pawn":SmokeLessonClick(SmokeLessonCell(piece,step),"lesson "+step.Id);break;
                case "move-w":
                {
                    var embedding=slice.DisplayState.Embedding;
                    var flat=result.P.First(q=>q.W==piece.Position.W&&InSlice(slice.Frame,CfgPreviewRenderer.V(q)+embedding));
                    SmokeLessonRefused(flat,"a rook move that keeps W","W方向");
                    var to=SmokeLessonCell(piece,step);SmokeLessonClick(to,"lesson "+step.Id);
                    SmokeCheck(session.State.Find(lessonPiece).Position.W!=0,"lesson move-w plays the rook along W");break;
                }
                case "escape":
                    // The rook's W line is hidden from this section; the refusal is tried through the board API.
                    SmokeLessonRefused(piece.Position.WithAxis(3,1),"a king step that stays on the rook's line","狙われて");
                    SmokeLessonClick(SmokeLessonCell(piece,step),"lesson "+step.Id);break;
                case "win-capture":
                    SmokeLessonRefused(piece.Position.WithAxis(3,-1),"a rook move that does not take the king","王");
                    SmokeLessonClick(session.State.Find(lessonTarget).Position,"lesson "+step.Id);break;
                case "orbit":PivotGesture(new Vector2(240,-50));break;
                case "zoom":PreviewScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,4)});break;
                case "reset-camera":case "back":case "rotate-back":ResetView();break;
                case "tour":case "win-tour":SmokeLessonButton("candidate-tour");break;
                case "finder":SmokeLessonButton("finder-Rook");break;
                case "panel-click":
                {
                    var title=minis[1].GetComponentInChildren<Button>();var top=SmokeTopAt(ScreenRect(title.transform).Value.center);
                    SmokeCheck(top!=null&&top.transform.IsChildOf(minis[1].transform),"lesson panel-click lets the panel title take the click");
                    title.onClick.Invoke();break;
                }
                case "rotate":PivotGesture(new Vector2(160,40),control:true);break;
                default:SmokeLessonButton("lesson-next");break;
            }
            yield return null;
        }
        // What each step should have left behind once the next one starts, beyond the step's own Done.
        void SmokeLessonState(string id)
        {
            if(id=="select-pawn")SmokeCheck(session.Config.Dimensions==4&&session.State.Pieces.Count==40&&LessonGame(session),"the lecture plays on a 4D opening board of its own");
            if(id=="replied")SmokeCheck(session.State.Ply==2&&cameraSide==Side.A&&session.State.Turn==Side.A,"B answered by itself and the camera stayed on A's side");
            if(id=="count")SmokeCheck(HiddenCandidateCount()>0&&lessonBody.text.Contains("W方向"),"lesson count names the rook's hidden W moves ("+HiddenCandidateCount()+")");
            if(id=="vanished")SmokeCheck(!SeesPiece(lessonPiece)&&session.State.Find(lessonPiece).Position.W!=0,"the rook moved along W is gone from the reset board");
            if(id=="found")SmokeCheck(SeesPiece(lessonPiece)&&selectedPiece==lessonPiece&&selectionText.text.Contains("飛車"),"駒をさがす brought the rook back and selected it");
            if(id=="threat")SmokeCheck(threatText.Contains("飛車")&&hintText.text.Contains("王が狙われています"),"lesson 6 opens with the king warning naming the rook");
            if(id=="escaped")SmokeCheck(threatText==""&&session.State.Ply==2,"the king escaped and B answered");
            if(id=="won")SmokeCheck(session.IsFinished&&session.State.Winner==Side.A&&!resultPanel.gameObject.activeSelf,"taking B's king wins, with the lesson's own words instead of the result panel");
            if(id=="five")SmokeCheck(session.Config.Dimensions==5&&minis.Count>4,"lesson 8 shows a 5D board with its larger column");
        }
        IEnumerator LessonSmokeSteps()
        {
            yield return new WaitForSecondsRealtime(2);
            SmokeCheck(menu.gameObject.activeSelf&&actions.ContainsKey("menu-lesson"),"the menu has an 操作レクチャー entry");
            yield return SmokeCapture("lesson-menu");
            int records=Directory.GetFiles(recordRoot).Length;string settingsHash=config.SettingsHash;int n=DraftDimensions;
            SmokeAction("menu-lesson");yield return null;yield return null;
            SmokeCheck(LessonActive&&session!=null&&Step.Id=="welcome","操作レクチャー starts the lecture");
            var clockStart=session.Clock;
            for(int guard=0;guard<80&&LessonActive;guard++)
            {
                var step=Step;int index=lesson;
                // The hole and the caption ease into place.
                yield return new WaitForSecondsRealtime(1.1f);
                SmokeLessonLight(step);
                yield return SmokeCapture("lesson-"+(index+1).ToString("00")+"-"+step.Id);
                if(step.Id=="replied")SmokeCheck(Math.Abs(session.Clock.ASeconds-clockStart.ASeconds)<.5&&Math.Abs(session.Clock.BSeconds-clockStart.BSeconds)<.5,"the lecture's clocks stand still");
                yield return SmokeLessonAct(step);
                yield return SmokeWait(()=>!LessonActive||lesson!=index,"lesson step "+step.Id+" moves on",20);
                if(LessonActive)SmokeLessonState(Step.Id);
            }
            SmokeCheck(!LessonActive&&session==null&&menu.gameObject.activeSelf,"the last step's メニューへ returns to the menu");
            SmokeCheck(Directory.GetFiles(recordRoot).Length==records,"lesson games leave no match record");
            SmokeCheck(config.SettingsHash==settingsHash&&DraftDimensions==n,"the lecture leaves the options as they were");

            // From the help, then skipping lesson by lesson: each lesson sets up the board it talks about.
            SmokeAction("menu-help");yield return null;
            SmokeCheck(actions["help-lesson"].gameObject.activeInHierarchy,"遊び方 opened from the menu offers the lecture");
            SmokeAction("help-lesson");yield return null;yield return null;
            SmokeCheck(LessonActive&&!OverlayOpen&&Step.Id=="welcome","the help's 操作レクチャー starts the lecture");
            SmokeAction("lesson-next");yield return null;
            SmokeAction("lesson-skip");yield return null;SmokeCheck(Step.Id=="orbit","次のレッスンへ moves to lesson 2");
            SmokeAction("lesson-skip");yield return null;SmokeCheck(Step.Id=="select-rook"&&SeesPiece(lessonPiece),"skipped into, lesson 3 has the rook on the board");
            SmokeAction("lesson-skip");yield return null;yield return null;
            SmokeCheck(Step.Id=="vanished"&&session.Snapshot.FixtureId=="lesson-opening-rook-away/1"&&!SeesPiece(lessonPiece),"skipped into, lesson 4 sets up a rook already moved along W");
            yield return new WaitForSecondsRealtime(1.1f);yield return SmokeCapture("lesson-skip-vanished");
            SmokeAction("lesson-next");yield return null;SmokeAction("lesson-skip");yield return null;SmokeCheck(Step.Id=="panels","次のレッスンへ moves to lesson 5");
            SmokeAction("lesson-next");yield return null;SmokeAction("lesson-skip");yield return null;yield return null;
            SmokeCheck(Step.Id=="threat"&&threatText.Contains("飛車"),"lesson 6 starts on its own board");
            SmokeAction("lesson-next");yield return null;SmokeAction("lesson-next");yield return null;SmokeAction("lesson-skip");yield return null;yield return null;
            SmokeCheck(Step.Id=="win-select"&&session.Snapshot.FixtureId=="lesson-win/1","lesson 7 starts on its own board");
            SmokeAction("lesson-skip");yield return null;yield return null;
            SmokeCheck(Step.Lesson==8&&!actions["lesson-skip"].gameObject.activeSelf,"the last lesson offers no 次のレッスンへ");
            EscapePressed();yield return null;
            SmokeCheck(leaveOverlay.gameObject.activeSelf&&leaveTitle.text.Contains("レクチャー"),"Esc during the lecture asks first, in the lecture's words");
            yield return SmokeCapture("lesson-leave-question");
            SmokeAction("leave-cancel");yield return null;SmokeCheck(LessonActive&&!OverlayOpen,"続ける keeps the lecture");
            SmokeAction("lesson-quit");yield return null;
            SmokeCheck(!LessonActive&&session==null&&menu.gameObject.activeSelf,"やめる ends the lecture at the menu");
            SmokeCheck(Directory.GetFiles(recordRoot).Length==records,"skipped lessons leave no match record either");
        }
    }
}
