using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;

namespace Showcase.Unity.Settings
{
    // --cfg2-smoke guide: the help pages, the menu's N buttons, 駒をさがす, the tour of hidden candidates (and playing one of them),
    // the notice for a click that does nothing, the king warning and the question before leaving a match. Run on a fresh --cfg2-data.
    public sealed partial class Cfg2App
    {
        IEnumerator GuideSteps()
        {
            yield return new WaitForSecondsRealtime(2);
            SmokeCheck(menu.gameObject.activeSelf&&actions.ContainsKey("menu-help")&&DraftDimensions==4,"guide starts on the 4D menu with a 遊び方 entry");
            yield return SmokeCapture("guide-menu");
            SmokeAction("menu-help");yield return null;
            SmokeCheck(helpOverlay.gameObject.activeSelf,"遊び方 opens from the menu");
            for(int i=0;i<HelpPages.Length;i++){SmokeAction("help-tab-"+i);yield return null;yield return SmokeCapture("guide-help-"+i);}
            SmokeAction("help-close");yield return null;SmokeCheck(!OverlayOpen,"遊び方 closes");
            SmokeAction("menu-dimensions-up");yield return null;SmokeCheck(DraftDimensions==5,"the menu's ＋ raises N to 5");
            yield return SmokeCapture("guide-menu-5d");
            SmokeAction("menu-dimensions-down");yield return null;SmokeCheck(DraftDimensions==4,"the menu's − brings N back to 4");

            SmokeAction("menu-0");yield return null;yield return null;
            SmokeCheck(session!=null&&session.Config.Dimensions==4&&session.CanHumanAct,"a 4D PvP match starts");
            yield return SmokeCapture("guide-match-start");
            SmokeAction("finder-King");yield return null;RenderGeometry();
            var king=session.State.Find(selectedPiece);
            SmokeCheck(king!=null&&king.Kind==PieceKind.King&&king.Side==Side.A&&result!=null,"駒をさがす selects A's king");
            int hidden=HiddenCandidateCount();
            SmokeCheck(hidden>0&&hidden<result.P.Count,"the king has candidates both in and outside the section ("+hidden+" of "+result.P.Count+" hidden)");
            yield return SmokeCapture("guide-king-selected");
            int stops=0,covered=0;
            for(int guard=0;guard<40;guard++)
            {
                StepCandidateTour();
                yield return SmokeWait(()=>tourStage==0&&!slice.IsAnimating&&!slice.QueuedSection.HasValue,"a tour leg",10);RenderGeometry();
                if(!TourActive)break;
                stops++;var stop=tourStops[tourIndex];
                SmokeCheck(stop.Cells.All(c=>InSlice(slice.Frame,c))&&PieceInSlice(slice.Frame,PieceWorld(king)),"tour stop "+stops+" ("+stop.Name+") shows the king and its "+stop.Cells.Count+" cells");
                covered+=stop.Cells.Count;
                yield return SmokeCapture("guide-tour-"+stops);
            }
            SmokeCheck(stops>0&&covered==hidden,"the tour shows every hidden candidate ("+covered+"/"+hidden+" in "+stops+" stops)");
            SmokeCheck(HiddenCandidateCount()==hidden,"the Tab after the last stop returns to the starting view");

            StepCandidateTour();yield return SmokeWait(()=>tourStage==0&&!slice.IsAnimating&&!slice.QueuedSection.HasValue,"first tour leg again",10);RenderGeometry();
            var cell=tourStops[tourIndex].Cells[0]-slice.DisplayState.Embedding;var q=new QN(Enumerable.Range(0,4).Select(i=>(int)Math.Round(cell.Axis(i))).ToArray());
            int ply=session.State.Ply;CommitCell(q,slice.ViewRevision,session.Revision,session.Generation);yield return null;
            SmokeCheck(session.State.Ply==ply+1,"a hidden candidate shown by the tour is played by clicking its cell");
            yield return SmokeCapture("guide-moved");
            yield return SmokeWait(()=>session.CanHumanAct,"the hand-over to B",10);
            var aPiece=session.State.Pieces.First(p=>p.Side==Side.A);CommitCell(aPiece.Position,slice.ViewRevision,session.Revision,session.Generation);yield return null;
            SmokeCheck(notice.Contains("相手の駒"),"clicking the other side's piece says so");

            SmokeAction("match-menu");yield return null;
            SmokeCheck(session!=null&&leaveOverlay.gameObject.activeSelf,"☰ メニュー during a match asks before leaving");
            yield return SmokeCapture("guide-leave-question");
            double before=session.Clock.ASeconds+session.Clock.BSeconds;yield return new WaitForSecondsRealtime(1.5f);
            SmokeAction("leave-cancel");yield return null;yield return null;
            SmokeCheck(session!=null&&!OverlayOpen,"続ける keeps the match");
            SmokeCheck(before-(session.Clock.ASeconds+session.Clock.BSeconds)<.5,"the 1.5 s the question was up are not taken from the clock");
            actions["match-menu"].onClick.Invoke();yield return null;SmokeAction("leave-confirm");yield return null;
            SmokeCheck(session==null&&menu.gameObject.activeSelf,"中断して戻る leaves the match");

            // A's king on the line of B's rook: the warning names the rook.
            SmokeFixture(MatchMode.PvP,new[]{SP("a-king",Side.A,PieceKind.King,0,0,0,0),SP("b-rook",Side.B,PieceKind.Rook,0,0,0,3),SP("b-king",Side.B,PieceKind.King,4,0,4,0),SP("a-pawn",Side.A,PieceKind.Pawn,-3,0,-3,0)},"smoke-guide-king-warning/1");
            yield return null;yield return null;
            SmokeCheck(threatText.Contains("飛車"),"the king warning names the attacking rook");
            yield return SmokeCapture("guide-king-warning");
            SmokeAction("finder-B");yield return null;SmokeAction("finder-Rook");yield return SmokeWait(()=>tourStage==0,"focus on B's rook",5);RenderGeometry();
            var rook=session.State.Pieces.First(p=>p.Id=="b-rook");
            SmokeCheck(PieceInSlice(slice.Frame,PieceWorld(rook))&&selectedPiece!="b-rook","駒をさがす brings the other side's hidden rook into the section without selecting it");
            yield return SmokeCapture("guide-find-rook");
            SmokeAction("match-menu");yield return null;
        }
    }
}
