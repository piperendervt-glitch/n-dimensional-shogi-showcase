using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // 操作レクチャー: eight short lessons played on real match boards. Each step shows one subtitle, lights what it talks about
    // (the rest of the screen dims and takes no clicks) and moves on once the player has done what it asks.
    // Lessons use their own 4D rules (5D for the last board), whatever the options say; their games keep no records and no clock.
    public sealed partial class Cfg2App
    {
        sealed class LessonStep
        {
            public string Id;public int Lesson;public Func<string> Text;
            // Board and selection the step starts from; null keeps the board as the previous step left it.
            public Action Enter;
            // Null: a narration that the 次へ button (or Enter) moves on. Otherwise the step waits for it, says できました,
            // then waits for Then (B's answer, for instance) before the next step.
            public Func<bool> Done,Then;
            // The screen rectangle to light; null leaves the screen undimmed unless DimAll.
            public Func<Rect?> Light;public bool DimAll;public float Pad=10;
            // A move the step does not want is answered with this text instead of being played. Steps without it want no move.
            public Func<Piece,QN,string> Refuse;
            public string Next;
        }
        static readonly string[] LessonTitles={"駒を選んで動かす","視点を動かす","見えない手を見る","消えた駒をさがす","右の図の見方","王が狙われたら","王を取って勝つ","慣れてきたら"};
        LessonStep[] lessonSteps;int lesson=-1;float lessonShown,lessonDoneAt=-1,lessonSaidAt=-100;string lessonSaid="";
        readonly HashSet<string> lessonHeard=new HashSet<string>();readonly Dictionary<int,MatchConfig> lessonRules=new Dictionary<int,MatchConfig>();
        bool LessonActive=>lesson>=0;
        LessonStep Step=>lessonSteps!=null&&lesson>=0&&lesson<lessonSteps.Length?lessonSteps[lesson]:null;
        // What the step started from, for Done tests that compare.
        float lessonYaw,lessonPitch,lessonDistance;int lessonPly;ReferenceSection lessonSection;string lessonPiece="",lessonTarget="";
        RectTransform lessonOverlay,lessonCaption,lessonBoardArea;Cfg2Spotlight spotlight;CanvasGroup captionFade;Text lessonHeader,lessonBody,lessonPraise,lessonStatus;
        Rect lessonHole;bool lessonHoleShown;float captionHigh,captionHeight=CaptionHeight;string lessonText="";float lessonWrapScale;int lessonWrapFrame;
        // The caption's bottom edge when it sits low, and its top edge when it sits high, under the hint bar.
        const float LessonPraiseSeconds=.9f,LessonDim=.66f,CaptionX=394,CaptionWidth=880,CaptionHeight=226,CaptionFloor=1012,CaptionTop=90;
        // The board between the left column and the right panels, for steps that are about the board itself.
        const float BoardLeft=372,BoardTop=86,BoardWidth=922,BoardHeight=930;

        void CreateLesson()
        {
            lessonOverlay=CfgUi.Rect(root,"Lesson overlay",-1200,-600,1536+2400,1024+1200);
            var dim=CfgUi.Rect(lessonOverlay,"Lesson spotlight",0,0,1536+2400,1024+1200);CfgUi.Fill(dim);spotlight=dim.gameObject.AddComponent<Cfg2Spotlight>();spotlight.color=Color.white;
            lessonCaption=Cfg2Ui.Panel(lessonOverlay,"Lesson caption",1200+CaptionX,600+CaptionFloor-CaptionHeight,CaptionWidth,CaptionHeight,new Color(Dark.r,Dark.g,Dark.b,1),true);
            captionFade=lessonCaption.gameObject.AddComponent<CanvasGroup>();
            var frame=Cfg2Ui.Frame(lessonCaption);if(frame!=null){frame.color=new Color(Gold.r,Gold.g,Gold.b,.8f);frame.Glow=true;frame.SetVerticesDirty();}
            lessonHeader=CfgUi.Label(lessonCaption,"",20,12,560,30,19,Gold);
            lessonPraise=CfgUi.Label(lessonCaption,"",584,12,276,30,19,Gold);lessonPraise.alignment=TextAnchor.UpperRight;
            lessonBody=CfgUi.Label(lessonCaption,"",20,46,840,104,20);lessonBody.lineSpacing=1.1f;
            lessonStatus=CfgUi.Label(lessonCaption,"",20,180,404,38,15);lessonStatus.alignment=TextAnchor.MiddleLeft;
            Button(lessonCaption,"lesson-next","次へ ▶",436,180,180,38,NextLessonStep,true).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            Button(lessonCaption,"lesson-skip","次のレッスンへ",624,180,136,38,SkipLesson).GetComponentInChildren<Text>().fontSize=15;
            Button(lessonCaption,"lesson-quit","やめる",768,180,92,38,()=>ReturnFromMatch()).GetComponentInChildren<Text>().fontSize=15;
            lessonBoardArea=CfgUi.Rect(matchHud,"Lesson board area",BoardLeft,BoardTop,BoardWidth,BoardHeight);
            lessonOverlay.gameObject.SetActive(false);
        }
        void StartLesson()
        {
            lessonSteps=lessonSteps??LessonSteps();CloseHelp();
            lessonOverlay.SetAsLastSibling();lessonOverlay.gameObject.SetActive(true);spotlight.Dim=0;lessonHoleShown=false;captionHigh=-1;
            Record("LessonStarted");EnterLessonStep(0);
        }
        // From ReturnFromMatch: the lesson ends with the board it was played on.
        void EndLesson()
        {
            if(!LessonActive)return;Record("LessonEnded",Step?.Id??"");lesson=-1;
            lessonOverlay.gameObject.SetActive(false);guideText.transform.parent.gameObject.SetActive(true);
        }
        void EnterLessonStep(int index)
        {
            lesson=index;var step=Step;lessonDoneAt=-1;lessonSaid="";
            step.Enter?.Invoke();if(!LessonActive)return;
            if(session==null){Record("LessonBoardMissing",step.Id);ReturnFromMatch();return;}
            lessonHeard.Clear();lessonYaw=yaw;lessonPitch=pitch;lessonDistance=distance;lessonPly=session.State.Ply;lessonSection=slice.StableSection??ReferenceSection.Initial(slice.Frame.Dimensions);
            guideText.transform.parent.gameObject.SetActive(false);
            lessonHeader.text="レッスン "+step.Lesson+"／"+LessonTitles.Length+"　"+LessonTitles[step.Lesson-1];
            bool narration=step.Done==null;actions["lesson-next"].gameObject.SetActive(narration);
            lessonStatus.rectTransform.sizeDelta=new Vector2(narration?404:592,38);SetCaptionText(step.Text());lessonWrapFrame=Time.frameCount;lessonPraise.text="";lessonStatus.text="";if(hudNotice!=null)hudNotice.text="";
            actions["lesson-next"].GetComponentInChildren<Text>().text=step.Next??"次へ ▶";
            actions["lesson-skip"].gameObject.SetActive(!narration&&step.Lesson<LessonTitles.Length);
            captionFade.alpha=0;lessonShown=Time.unscaledTime;
            Record("LessonStep",(index+1)+"/"+lessonSteps.Length+";"+step.Id);
        }
        // The caption fits its text: the subtitle's lines, then one row with the latest notice on the left and the buttons on the
        // right. Wrapped again when the text or the canvas scale changes, and on the first frames, once the screen has settled.
        void SetCaptionText(string raw)
        {
            lessonText=raw;lessonWrapScale=canvas.scaleFactor;lessonBody.rectTransform.sizeDelta=new Vector2(840,400);lessonBody.text=KeepPunctuationOffLineStarts(lessonBody,raw);
            float body=Mathf.Ceil(Mathf.Max(28,lessonBody.preferredHeight)),row=46+body+8;lessonBody.rectTransform.sizeDelta=new Vector2(840,body);
            foreach(var id in new[]{"lesson-next","lesson-skip","lesson-quit"}){var r=(RectTransform)actions[id].transform;r.anchoredPosition=new Vector2(r.anchoredPosition.x,-row);}
            lessonStatus.rectTransform.anchoredPosition=new Vector2(20,-row);captionHeight=row+38+12;lessonCaption.sizeDelta=new Vector2(CaptionWidth,captionHeight);
        }
        void NextLessonStep()
        {
            if(!LessonActive)return;
            if(lesson+1>=lessonSteps.Length){ReturnFromMatch();return;}
            EnterLessonStep(lesson+1);
        }
        // 次のレッスンへ: the first step of the next lesson, which sets up the board it needs.
        void SkipLesson()
        {
            if(!LessonActive)return;int next=Array.FindIndex(lessonSteps,s=>s.Lesson==Step.Lesson+1);
            Record("LessonSkipped",Step.Id);if(next<0)NextLessonStep();else EnterLessonStep(next);
        }
        void LessonHeard(string name){if(LessonActive)lessonHeard.Add(name);}
        // During a lesson, notices (a refused click, a move the step does not want) appear only in the caption, which stays lit.
        void LessonSay(string text){if(!LessonActive)return;lessonSaid=text;lessonSaidAt=Time.unscaledTime;}
        string LessonRefusal(Piece piece,QN to)
        {
            var step=Step;if(step==null)return null;var why=step.Refuse!=null?step.Refuse(piece,to):"今は駒を動かさずに、字幕の案内に沿って進めましょう。";
            if(why!=null)Record("LessonRefused",step.Id+";"+piece.Id+";"+to);return why;
        }
        // In a lesson nobody plays B: once A's move has been shown, B answers with a quiet move, a pawn step forward when it has one.
        void LessonReply()
        {
            if(!session.CanHumanAct||session.State.Turn!=Side.B||PresentingMove)return;
            var legal=session.QueryLegal();if(legal.Status!=MatchQueryStatus.Complete||legal.Moves.Count==0)return;
            var quiet=legal.Moves.Where(m=>session.State.At(m.To)==null).ToList();if(quiet.Count==0)quiet=legal.Moves.ToList();
            var move=quiet.Where(m=>session.State.Find(m.PieceId)?.Kind==PieceKind.Pawn&&m.To==m.From.WithAxis(0,m.From.F-1)).DefaultIfEmpty(quiet[0]).First();
            if(!session.TryMove(move,out var reason)){Record("LessonReplyRefused",reason);return;}
            var from=CfgPreviewRenderer.V(move.From);var to=CfgPreviewRenderer.V(move.To);
            glide=new PieceGlide{Id=move.PieceId,From=from,To=to,Start=Time.unscaledTime,Seconds=Mathf.Clamp((float)(to-from).Length*GlideSecondsPerCell,GlideMinSeconds,GlideMaxSeconds)};
            selectedPiece="";QueueQuery();RefreshMatch();Record("LessonReply",move.PieceId+";"+move.From+">"+move.To);
        }
        bool BackToA=>session!=null&&session.State.Turn==Side.A&&session.CanHumanAct&&!PresentingMove;
        void UpdateLesson()
        {
            if(!LessonActive)return;
            if(session==null||opened){EndLesson();return;}
            LessonReply();var step=Step;
            if(lessonDoneAt<0&&step.Done!=null&&step.Done()){lessonDoneAt=Time.unscaledTime;PlayMenuSelect();Record("LessonDone",step.Id);}
            if(lessonDoneAt>=0&&Time.unscaledTime-lessonDoneAt>=LessonPraiseSeconds&&(step.Then==null||step.Then())){NextLessonStep();return;}
            if(step.Done==null&&CfgUi.SelectedInput==null&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter))){NextLessonStep();return;}
            LayoutLesson();
        }
        // The lit hole closes in from the whole screen onto the step's target, follows it while the view moves, and opens back
        // out to the whole screen while the dim fades when a step lights nothing. The caption takes the top or the bottom of the
        // screen, whichever covers less of the hole.
        void LayoutLesson()
        {
            var step=Step;float dt=Mathf.Min(.1f,Time.unscaledDeltaTime);var target=LessonTarget();
            var raw=step.Text();if(raw!=lessonText||canvas.scaleFactor!=lessonWrapScale||Time.frameCount<=lessonWrapFrame+2)SetCaptionText(raw);
            float pad=step.Pad*canvas.scaleFactor;var full=spotlight.rectTransform.rect;
            spotlight.Dim=Mathf.MoveTowards(spotlight.Dim,target.HasValue||step.DimAll?LessonDim:0,dt*2.4f);
            Rect? goal=target.HasValue?ScreenToSpotlight(Rect.MinMaxRect(target.Value.xMin-pad,target.Value.yMin-pad,target.Value.xMax+pad,target.Value.yMax+pad)):step.DimAll?(Rect?)null:full;
            if(goal.HasValue)
            {
                if(!lessonHoleShown){lessonHole=full;lessonHoleShown=true;}
                var g=goal.Value;float k=1-Mathf.Exp(-dt*11);
                lessonHole=Rect.MinMaxRect(Mathf.Lerp(lessonHole.xMin,g.xMin,k),Mathf.Lerp(lessonHole.yMin,g.yMin,k),Mathf.Lerp(lessonHole.xMax,g.xMax,k),Mathf.Lerp(lessonHole.yMax,g.yMax,k));
                spotlight.Hole=lessonHole;spotlight.HasHole=true;
            }
            else{spotlight.HasHole=false;lessonHoleShown=false;}
            spotlight.Pulse=Mathf.Repeat(Time.unscaledTime*.8f,1);spotlight.raycastTarget=spotlight.Dim>.05f;spotlight.SetVerticesDirty();
            // Low, the caption keeps its bottom edge whatever its height; high, its top edge.
            float low=CaptionFloor-captionHeight;
            float high=target.HasValue&&Overlap(target.Value,StageRect(CaptionX,low,CaptionWidth,captionHeight))>Overlap(target.Value,StageRect(CaptionX,CaptionTop,CaptionWidth,captionHeight))?1:0;
            captionHigh=captionHigh<0?high:Mathf.Lerp(captionHigh,high,1-Mathf.Exp(-dt*12));float captionY=Mathf.Lerp(low,CaptionTop,captionHigh);
            float shown=Mathf.Clamp01((Time.unscaledTime-lessonShown)/.3f);captionFade.alpha=shown;
            lessonCaption.anchoredPosition=new Vector2(1200+CaptionX,-(600+captionY+(1-shown)*(1-shown)*14));
            // できました pops up once the step is done; the latest notice of this step stays a while under the subtitle, then fades.
            float done=lessonDoneAt<0?0:Mathf.Clamp01((Time.unscaledTime-lessonDoneAt)/.25f);
            lessonPraise.text=lessonDoneAt<0?"":"✓ できました";lessonPraise.color=new Color(.62f,.95f,.62f,done);lessonPraise.rectTransform.localScale=Vector3.one*(1+.2f*(1-done));
            float age=Time.unscaledTime-lessonSaidAt;lessonStatus.text=age<6?lessonSaid:"";lessonStatus.color=new Color(1,.70f,.45f,Mathf.Clamp01(6-age));
        }
        // The step's target on screen, kept far enough inside the screen for the frame around it to show.
        Rect? LessonTarget()
        {
            var step=Step;var t=step?.Light?.Invoke();if(!t.HasValue)return null;float m=(step.Pad+4)*canvas.scaleFactor;
            var r=Rect.MinMaxRect(Mathf.Max(t.Value.xMin,m),Mathf.Max(t.Value.yMin,m),Mathf.Min(t.Value.xMax,Screen.width-m),Mathf.Min(t.Value.yMax,Screen.height-m));
            return r.width>0&&r.height>0?r:(Rect?)null;
        }
        Rect ScreenToSpotlight(Rect screen)
        {
            var r=spotlight.rectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(r,screen.min,null,out var a);RectTransformUtility.ScreenPointToLocalPointInRectangle(r,screen.max,null,out var b);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        }
        static float Overlap(Rect a,Rect b){float w=Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin),h=Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin);return w>0&&h>0?w*h:0;}
        // Stage coordinates (1536×1024, y down from the top) to screen pixels.
        Rect StageRect(float x,float y,float w,float h)
        {
            Vector2 At(float sx,float sy)=>root.TransformPoint(new Vector3(sx-root.rect.width*.5f,root.rect.height*.5f-sy,0));
            var a=At(x,y+h);var b=At(x+w,y);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        // Screen-space targets. An overlay canvas puts world corners in screen pixels.
        static Rect? ScreenRect(Transform t)
        {
            var r=t as RectTransform;if(r==null||!r.gameObject.activeInHierarchy)return null;
            var c=new Vector3[4];r.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);
        }
        static Rect? Union(IEnumerable<Rect?> rects)
        {
            Rect? all=null;foreach(var r in rects)if(r.HasValue)all=all.HasValue?Rect.MinMaxRect(Mathf.Min(all.Value.xMin,r.Value.xMin),Mathf.Min(all.Value.yMin,r.Value.yMin),Mathf.Max(all.Value.xMax,r.Value.xMax),Mathf.Max(all.Value.yMax,r.Value.yMax)):r;
            return all;
        }
        Rect? SectionRect(SectionResult section)
        {
            if(section==null||section.Vertices==null||section.Vertices.Length==0)return null;
            float scale=(float)slice.DisplayState.CellScale;float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;
            foreach(var v in section.Vertices)
            {
                var s=rig.Camera.WorldToScreenPoint(previewRenderer.Anchor+SectionView.MapVector(v.Position)*scale);if(s.z<=0)continue;
                x0=Mathf.Min(x0,s.x);y0=Mathf.Min(y0,s.y);x1=Mathf.Max(x1,s.x);y1=Mathf.Max(y1,s.y);
            }
            return x0<=x1?Rect.MinMaxRect(x0,y0,x1,y1):(Rect?)null;
        }
        Rect? PieceRect(string id)=>previewRenderer.PieceSections.TryGetValue(id??"",out var view)&&view.Renderer.enabled?OnBoard(SectionRect(view.Last)):null;
        Rect? BoardRect()=>ScreenRect(lessonBoardArea);
        // A piece or cell drawn outside the board area (behind the hint bar, off the screen) is not lit: a click there would not reach it.
        Rect? OnBoard(Rect? r)
        {
            var board=BoardRect();if(!r.HasValue||!board.HasValue)return r;if(!board.Value.Contains(r.Value.center))return null;var a=r.Value;var b=board.Value;
            return Rect.MinMaxRect(Mathf.Max(a.xMin,b.xMin),Mathf.Max(a.yMin,b.yMin),Mathf.Min(a.xMax,b.xMax),Mathf.Min(a.yMax,b.yMax));
        }
        // The blue cells of the selected piece that the step would play, as drawn now. Which of the visible cells the step allows
        // is worked out once per query and view (a king's safety test queries the board).
        List<QN> lessonCells;QueryResult lessonCellsFor;int lessonCellsStep=-1;long lessonCellsView=-1;
        Rect? CellsRect()
        {
            var piece=session?.State.Find(selectedPiece);if(piece==null||result==null)return null;
            if(!ReferenceEquals(lessonCellsFor,result)||lessonCellsStep!=lesson||lessonCellsView!=slice.ViewRevision)
            {
                var embedding=slice.DisplayState.Embedding;lessonCellsFor=result;lessonCellsStep=lesson;lessonCellsView=slice.ViewRevision;
                lessonCells=result.P.Where(q=>InSlice(slice.Frame,CfgPreviewRenderer.V(q)+embedding)&&Step?.Refuse?.Invoke(piece,q)==null).ToList();
            }
            return Union(lessonCells.Select(q=>previewRenderer.TryGetCellSection(q,out var section)?OnBoard(SectionRect(section)):null));
        }
        Rect? MinisRect(int first=0)=>Union(minis.Skip(first).Select(m=>ScreenRect(m.transform)));
        Rect? HintRect()=>ScreenRect(hintPanel);
        // A piece of the lesson: lights it until it is selected, then its blue cells.
        Rect? PieceThenCells()=>selectedPiece==lessonPiece?CellsRect()??BoardRect():PieceRect(lessonPiece)??BoardRect();

        MatchConfig LessonRules(int dimensions)
        {
            if(lessonRules.TryGetValue(dimensions,out var cached))return cached;
            var rules=SettingsDraft.CreateMatchDemo();if(dimensions!=4)rules.Edit(d=>ResizeDraft(d,4,dimensions));
            if(!MatchConfig.TryCreate(rules,new MatchOptionsRaw(),out var made,out var report)){Notice("レクチャーの盤を用意できません："+string.Join(" / ",report.Errors));return null;}
            lessonRules[dimensions]=made;return made;
        }
        // Lesson boards are fixtures (ids "lesson-…"), so their games are never saved as match records.
        void LessonBoard(string id,IReadOnlyList<Piece> pieces=null,int dimensions=4)
        {
            var rules=LessonRules(dimensions);if(rules==null){ReturnFromMatch();return;}
            StartMatch(MatchMode.PvP,pieces??MatchRules.InitialPieces(rules.Preset),"lesson-"+id,rules);
        }
        static bool LessonGame(MatchSession s)=>s!=null&&s.Snapshot.FixtureId.StartsWith("lesson-",StringComparison.Ordinal);
        static Piece Placed(string id,Side side,PieceKind kind,int f,int u,int v=0,int w=0)=>new Piece(id,side,kind,new QN(f,u,v,w));
        Piece LessonFind(Side side,PieceKind kind,Func<Piece,bool> where=null)=>session?.State.Pieces.FirstOrDefault(p=>p.Side==side&&p.Kind==kind&&(where==null||where(p)));
        string LessonName(PieceKind kind){string id=MatchConfig.DefinitionId(kind);return ShortNames.TryGetValue(id,out var name)?name:id;}
        // Lessons 3 and 4 continue the opening board; skipped into, they set up the position they talk about.
        void EnsureOpening(bool rookAway)
        {
            var rook=LessonFind(Side.A,PieceKind.Rook);
            bool fine=session!=null&&session.Config.Dimensions==4&&LessonGame(session)&&BackToA&&rook!=null&&(rook.Position.W!=0)==rookAway;
            if(!fine)
            {
                var pieces=MatchRules.InitialPieces(4).Select(p=>rookAway&&p.Side==Side.A&&p.Kind==PieceKind.Rook?new Piece(p.Id,p.Side,p.Kind,p.Position.WithAxis(3,2)):p).ToArray();
                LessonBoard(rookAway?"opening-rook-away/1":"opening/1",pieces);
            }
            else{ResetCandidateTour();slice.Restore(NewPivotSlice().DisplayState);geometryDirty=true;}
            lessonPiece=LessonFind(Side.A,PieceKind.Rook)?.Id??"";selectedPiece="";QueueQuery();
        }
        bool Orbited=>ownership.Owner==InputOwner.None&&(Mathf.Abs(Mathf.DeltaAngle(yaw,lessonYaw))>20||Mathf.Abs(pitch-lessonPitch)>10);
        bool Moved=>session!=null&&session.State.Ply>lessonPly;
        bool Heard(string name)=>lessonHeard.Contains(name);
        bool SeesPiece(string id){var piece=session?.State.Find(id);return piece!=null&&PieceInSlice(slice.Frame,PieceWorld(piece));}
        bool SettledView=>!slice.IsAnimating&&!slice.QueuedSection.HasValue&&tourStage==0&&sectionGlideStart<0;

        LessonStep[] LessonSteps()
        {
            var s=new List<LessonStep>();
            void Add(int lesson,string id,Func<string> text,Func<bool> done=null,Func<Rect?> light=null,Action enter=null,Func<bool> then=null,Func<Piece,QN,string> refuse=null,bool dimAll=false,string next=null,float pad=10)
                =>s.Add(new LessonStep{Lesson=lesson,Id=id,Text=text,Done=done,Light=light,Enter=enter,Then=then,Refuse=refuse,DimAll=dimAll,Next=next,Pad=pad});
            string OnlyLessonPiece(Piece p,QN q)=>p.Id==lessonPiece?null:"このレッスンでは、光っている"+LessonName(session.State.Find(lessonPiece)?.Kind??PieceKind.Pawn)+"を動かします。";

            // 1 駒を選んで動かす
            Add(1,"welcome",()=>"ようこそ。実際の盤を使って、N次元将棋の操作を8つのレッスンで覚えましょう。手前の駒が自分（先手A）、奥の駒が相手（後手B）です。",
                enter:()=>LessonBoard("opening/1"),next:"始める ▶");
            Add(1,"select-pawn",()=>"まず駒を選びます。光っている歩をクリックしてください。",
                enter:()=>{lessonPiece=LessonFind(Side.A,PieceKind.Pawn,p=>p.Position.U==0)?.Id??"";selectedPiece="";QueueQuery();},
                done:()=>selectedPiece==lessonPiece,light:()=>PieceRect(lessonPiece)??BoardRect());
            Add(1,"move-pawn",()=>"青く光るマスが、その駒の動ける場所です。青いマスをクリックして、歩を動かしましょう。",
                done:()=>Moved,then:()=>BackToA,light:PieceThenCells,refuse:OnlyLessonPiece);
            Add(1,"replied",()=>"相手（後手B）も1手指しました。このように交互に指して、相手の王を取ったほうが勝ちです。");

            // 2 視点を動かす
            Add(2,"orbit",()=>"盤の上をマウスでドラッグすると、視点が回ります。ドラッグして、盤をいろいろな向きから見てみましょう。",
                done:()=>Orbited,light:BoardRect);
            Add(2,"zoom",()=>"マウスのホイールを回すと、拡大・縮小します。回してみましょう。",
                done:()=>Mathf.Abs(distance-lessonDistance)>.08f,light:BoardRect);
            Add(2,"reset-camera",()=>"Rキーを押すと、視点も盤の向きも最初の表示に戻ります。押してみましょう。",
                done:()=>Heard("PivotReset"),light:BoardRect);

            // 3 見えない手を見る
            Add(3,"select-rook",()=>"次は飛車です。光っている飛車をクリックしてください。",
                enter:()=>EnsureOpening(false),done:()=>selectedPiece==lessonPiece,light:()=>PieceRect(lessonPiece)??BoardRect());
            Add(3,"count",()=>
                {
                    int all=result?.P.Count??0,hidden=HiddenCandidateCount();
                    return "上の案内を見てください。飛車が動ける"+all+"マスのうち、盤に見えているのは"+(all-hidden)+"マスだけです。残りの"+hidden+"マスは、4次元目のW方向にあります。";
                },light:HintRect);
            Add(3,"tour",()=>"「見えない候補へ」ボタンかTabキーを押すと、見えない手が見える向きへ盤が回ります。押してみましょう。",
                done:()=>TourActive&&SettledView,light:HintRect);
            Add(3,"move-w",()=>"盤が回り、W方向のマスが見えるようになりました。青いマスをクリックして、飛車をW方向へ動かしましょう。",
                done:()=>Moved,then:()=>BackToA,light:PieceThenCells,
                refuse:(p,q)=>OnlyLessonPiece(p,q)??(q.W==p.Position.W?"W方向のマス（Wの値が変わるマス）を選んでください。":null));
            Add(3,"back",()=>"相手も指しました。Rキーを押して、最初の表示に戻しましょう。",
                done:()=>Heard("PivotReset"),light:BoardRect);

            // 4 消えた駒をさがす
            Add(4,"vanished",()=>
                {
                    var rook=session.State.Find(lessonPiece);
                    return "飛車が盤から消えました。W＝"+(rook?.Position.W??0)+"へ動いたので、今の断面（W＝0）には映らないのです。駒が消えても、なくなったわけではありません。";
                },enter:()=>EnsureOpening(true));
            Add(4,"finder",()=>"左下の「駒をさがす」で、飛車の行が「見える0／全1」になっています。飛車の行をクリックしてください。",
                done:()=>selectedPiece==lessonPiece&&SeesPiece(lessonPiece)&&SettledView,light:()=>ScreenRect(actions["finder-Rook"].transform),pad:3);
            Add(4,"found",()=>"盤がW方向へ平行に動いて、飛車が見えるようになりました。左上には、選んだ駒の名前と座標が出ます。",
                light:()=>StageRect(30,244,330,92),pad:4);

            // 5 右の図の見方
            Add(5,"panels",()=>"右の4枚の図は、軸の組み合わせを変えて切った断面です。図の点が駒の位置で、盤に見えない駒もここに出ます。",
                light:()=>MinisRect()??BoardRect(),pad:4);
            Add(5,"panel-click",()=>"図をクリックすると、盤がその断面に切り替わります。下の3枚のどれかをクリックしてみましょう。",
                done:()=>SettledView&&slice.StableSection.HasValue&&slice.StableSection.Value!=lessonSection,light:()=>MinisRect(1)??BoardRect(),pad:4);
            Add(5,"gizmo",()=>"右上の球は、今見えている軸を示します。軸の名前をクリックしても、その軸が入った断面へ盤が回ります。",
                light:()=>ScreenRect(matchGizmo));

            // 6 王が狙われたら
            Add(6,"threat",()=>"新しい盤です。上の案内に赤い文字で「王が狙われています」と出ています。放っておくと、次の手で王を取られます。",
                enter:()=>{LessonBoard("escape/1",new[]{Placed("a-king",Side.A,PieceKind.King,-2,0),Placed("a-pawn-1",Side.A,PieceKind.Pawn,-1,-2),Placed("a-pawn-2",Side.A,PieceKind.Pawn,-1,2),
                    Placed("b-rook",Side.B,PieceKind.Rook,-2,0,0,3),Placed("b-king",Side.B,PieceKind.King,3,0),Placed("b-pawn",Side.B,PieceKind.Pawn,2,-2)});lessonPiece="a-king";},
                light:HintRect);
            Add(6,"where",()=>"狙っているのは後手Bの飛車です。W方向にいるので盤には見えませんが、右の図には点で出ています。",
                light:()=>MinisRect()??BoardRect(),pad:4);
            Add(6,"select-king",()=>"王をクリックして選びましょう。",
                done:()=>selectedPiece==lessonPiece,light:()=>PieceRect(lessonPiece)??BoardRect());
            Add(6,"escape",()=>"青いマスへ動かして逃げましょう。飛車の通り道（W方向の線）から外れれば、もう狙われません。",
                done:()=>Moved,then:()=>BackToA,light:PieceThenCells,
                refuse:(p,q)=>p.Id!=lessonPiece?"王を動かして逃げましょう。":KingSafeAt(p,q)?null:"そのマスも飛車に狙われています。ほかのマスへ逃げましょう。");
            Add(6,"escaped",()=>"逃げられました。王が狙われたら、王を逃がすか、狙っている駒を取りましょう。");

            // 7 王を取って勝つ
            Add(7,"win-select",()=>"最後の練習です。後手Bの王は見えないW方向にいますが、飛車で取れる位置です。光っている飛車をクリックしてください。",
                enter:()=>{LessonBoard("win/1",new[]{Placed("a-king",Side.A,PieceKind.King,-4,0),Placed("a-rook",Side.A,PieceKind.Rook,-1,0),Placed("a-pawn",Side.A,PieceKind.Pawn,-2,2),
                    Placed("b-king",Side.B,PieceKind.King,-1,0,0,1),Placed("b-pawn",Side.B,PieceKind.Pawn,3,-2),Placed("b-gold",Side.B,PieceKind.Gold,3,2)});lessonPiece="a-rook";lessonTarget="b-king";},
                done:()=>selectedPiece==lessonPiece,light:()=>PieceRect(lessonPiece)??BoardRect());
            Add(7,"win-tour",()=>"Tabキー（または「見えない候補へ」）で、見えない手が見える向きへ盤を回しましょう。",
                done:()=>TourActive&&SettledView&&SeesPiece(lessonTarget),light:HintRect);
            Add(7,"win-capture",()=>"後手Bの王が見えました。王のいるマスをクリックして、取りましょう。",
                done:()=>session.IsFinished&&session.State.Winner==Side.A,light:()=>selectedPiece==lessonPiece?PieceRect(lessonTarget)??BoardRect():PieceRect(lessonPiece)??BoardRect(),
                refuse:(p,q)=>session.State.Find(lessonTarget)?.Position==q?null:"後手Bの王がいるマスをクリックしてください。");
            Add(7,"won",()=>"勝ちました！相手の王を取ると、その場で勝ちが決まります。");

            // 8 慣れてきたら
            Add(8,"rotate",()=>"慣れてきたら、Ctrlキーを押しながら盤をドラッグしてみましょう。盤が4次元で回り、W方向が少しずつ入ってきます。",
                enter:()=>LessonBoard("rotate/1"),done:()=>Heard("Rotation4D")&&ownership.Owner==InputOwner.None,light:BoardRect);
            Add(8,"rotate-back",()=>"戻すときは、いつでもRキーです。",
                done:()=>Heard("PivotReset"),light:BoardRect);
            Add(8,"five",()=>"メニューの「次元」で、3〜11次元の盤を選べます。これは5次元の盤です。右の図が増え、上の大きな図の下の3枚がCtrl＋ドラッグで回せる断面です。",
                enter:()=>LessonBoard("five/1",null,5),light:()=>MinisRect()??BoardRect(),pad:4);
            Add(8,"end",()=>"これでレクチャーはおしまいです。迷ったら、F1でいつでも遊び方を開けます。メニューから対局を始めましょう。",
                dimAll:true,next:"メニューへ");
            return s.ToArray();
        }
        // The king is safe on a cell when no B piece could take it there after the move.
        bool KingSafeAt(Piece king,QN to)
        {
            var after=session.ToPreview();var rule=session.Snapshot.Config.Preset;
            if(!after.TryMove(rule,king.Id,to,out _))return true;
            var attack=PreviewQuery.Attackers(rule,after,to,RuleCore.Other(king.Side));
            return attack.Status!=QueryStatus.Complete||attack.Attackers.Count==0;
        }
    }

    // The lesson's dimmed screen with one lit hole and a pulsing gold frame around it. Clicks inside the hole reach what is
    // under it; the dimmed rest takes them, so only what the step talks about can be used.
    internal sealed class Cfg2Spotlight : MaskableGraphic,ICanvasRaycastFilter
    {
        public Rect Hole;public bool HasHole;public float Dim,Pulse;
        public bool IsRaycastLocationValid(Vector2 screen,Camera eventCamera)
        {
            if(Dim<.05f)return false;if(!HasHole)return true;
            return !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screen,eventCamera,out var local)||!Hole.Contains(local);
        }
        static void Quad(VertexHelper vh,float x0,float y0,float x1,float y1,Color c)
        {
            if(x1<=x0||y1<=y0)return;int i=vh.currentVertCount;
            vh.AddVert(new Vector3(x0,y0),c,Vector2.zero);vh.AddVert(new Vector3(x0,y1),c,Vector2.zero);vh.AddVert(new Vector3(x1,y1),c,Vector2.zero);vh.AddVert(new Vector3(x1,y0),c,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        static void Frame(VertexHelper vh,Rect r,float radius,Color c,float width)
        {
            radius=Mathf.Min(radius,r.width*.5f,r.height*.5f);var points=new List<Vector2>();
            var centers=new[]{new Vector2(r.xMax-radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMin+radius),new Vector2(r.xMax-radius,r.yMin+radius)};
            for(int corner=0;corner<4;corner++)for(int step=0;step<=6;step++){float a=(corner*90+step*15)*Mathf.Deg2Rad;points.Add(centers[corner]+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}
            for(int i=0;i<points.Count;i++)Cfg2MeshUi.Line(vh,points[i],points[(i+1)%points.Count],c,width);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(Dim<=.001f)return;var r=rectTransform.rect;var shade=new Color(0,0,0,Dim);
            if(!HasHole){Quad(vh,r.xMin,r.yMin,r.xMax,r.yMax,shade);return;}
            var h=Rect.MinMaxRect(Mathf.Clamp(Hole.xMin,r.xMin,r.xMax),Mathf.Clamp(Hole.yMin,r.yMin,r.yMax),Mathf.Clamp(Hole.xMax,r.xMin,r.xMax),Mathf.Clamp(Hole.yMax,r.yMin,r.yMax));
            Quad(vh,r.xMin,h.yMax,r.xMax,r.yMax,shade);Quad(vh,r.xMin,r.yMin,r.xMax,h.yMin,shade);
            Quad(vh,r.xMin,h.yMin,h.xMin,h.yMax,shade);Quad(vh,h.xMax,h.yMin,r.xMax,h.yMax,shade);
            // The frame glows and breathes; a second ring grows out of it and fades, drawing the eye to the hole.
            float strength=Dim/.66f,breath=.75f+.25f*Mathf.Sin(Pulse*Mathf.PI*2);var gold=new Color(.95f,.79f,.49f,1);
            Frame(vh,h,9,new Color(gold.r,gold.g,gold.b,.10f*strength*breath),14);
            Frame(vh,h,9,new Color(gold.r,gold.g,gold.b,.25f*strength*breath),7);
            Frame(vh,h,9,new Color(gold.r,gold.g,gold.b,.95f*strength),2.2f);
            float grow=4+14*Pulse;var ring=Rect.MinMaxRect(h.xMin-grow,h.yMin-grow,h.xMax+grow,h.yMax+grow);
            Frame(vh,ring,9+grow,new Color(gold.r,gold.g,gold.b,.55f*strength*(1-Pulse)*(1-Pulse)),2);
        }
    }
}
