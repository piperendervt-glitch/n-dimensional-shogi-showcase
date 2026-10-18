using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // Help for a first-time player: the 遊び方 pages, the step hint above the board, the tour of the selected piece's candidates
    // that the board's section hides, the warning when the player's king can be taken, and the question before a match is abandoned.
    public sealed partial class Cfg2App
    {
        // The match clock stands still while the help or a question covers the board.
        sealed class Cfg2MatchClock : IMonotonicClock
        {
            readonly Stopwatch watch=Stopwatch.StartNew();double pausedAt=-1,pausedTotal;
            public double Seconds=>(pausedAt>=0?pausedAt:watch.Elapsed.TotalSeconds)-pausedTotal;
            public void Hold(bool held)
            {
                if(held&&pausedAt<0)pausedAt=watch.Elapsed.TotalSeconds;
                else if(!held&&pausedAt>=0){pausedTotal+=watch.Elapsed.TotalSeconds-pausedAt;pausedAt=-1;}
            }
        }
        Cfg2MatchClock matchClock;
        const string HintsKey="cfg2.step-hints",HelpSeenKey="cfg2.help-seen";
        bool HintsEnabled{get=>Pref(HintsKey);set=>SetPref(HintsKey,value);}
        bool OverlayOpen=>helpOverlay!=null&&helpOverlay.gameObject.activeSelf||leaveOverlay!=null&&leaveOverlay.gameObject.activeSelf;

        // ---- 遊び方 ----
        RectTransform helpOverlay;Text helpTitle,helpBody,helpPageLabel;int helpPage;readonly List<Button> helpTabs=new List<Button>();
        // Unity's Text breaks lines only at ASCII spaces, so the pages have none: a space before a long run of Japanese
        // pushes the whole run to the next line and leaves the word before it alone on its own line.
        static readonly (string tab,string title,string body)[] HelpPages=
        {
            ("ルール","この将棋について",
             "盤は各軸−4〜4の9マス。4次元なら9×9×9×9のマスがあり、メニューの「次元」で3〜11次元を選べます。\n"+
             "駒は普段の将棋の駒（王・飛車・角行・金・銀・桂馬・香車・歩兵）で、動きをN次元に広げたものです。\n\n"+
             "<color=#F2C97D>勝ち負け</color>\n"+
             "・相手の王を取れば勝ちです。\n"+
             "・持ち時間が0になると負け、どちらも動ける手がなければ引き分けです。\n"+
             "・王手を放っておいても反則になりません。自分の王が狙われると、盤の上に赤い文字で知らせます。\n\n"+
             "<color=#F2C97D>普段の将棋との違い</color>\n"+
             "・成り、持ち駒（取った駒を打つ）、千日手はありません。\n"+
             "・先手Aは+Z方向（奥）へ、後手Bは−Z方向へ進みます。\n"+
             "・デモ版の暫定ルールです。オプションで駒の動きを変えられます。"),
            ("動かし方","駒の動かし方",
             "<color=#F2C97D>1．駒を選ぶ</color>　自分の駒をクリックします。選んだ駒は金色になります。"+
             "盤に見えない駒は、左下の「駒をさがす」で種類をクリックすると、その駒が見える断面へ盤が動いて選ばれます（押すたびに同じ種類の次の駒へ）。\n\n"+
             "<color=#F2C97D>2．移動先をクリック</color>　青く光るマスが動ける場所です。相手の駒があるマスへ動けば、その駒を取れます。\n\n"+
             "<color=#F2C97D>3．見えない候補を見る</color>　盤に見えているのは、4次元空間を3次元で切った「断面」だけです。"+
             "見えていない方向（Wなど）にも動ける手があり、盤の上の案内に「この断面に26手／全80手」のように出ます。\n"+
             "Tabキー（または「見えない候補へ」ボタン）を押すと、残りの候補が見える断面へ盤が回ります。"+
             "押すたびに次の断面へ進み、最後は元の表示に戻ります。回った先でも、青いマスをクリックすれば移動できます。\n\n"+
             "<color=#F2C97D>4．迷ったら</color>　Rキーで最初の表示に戻ります。別の駒をクリックすれば選び直せます。"),
            ("4次元の見方","画面の見方",
             "<color=#F2C97D>盤</color>　4次元空間の3次元の断面です。最初はX（横）・Y（上）・Z（奥）の3軸が見えていて、W=0の場所を切っています。\n\n"+
             "<color=#F2C97D>右上の球（軸ジャイロ）</color>　今どの軸が見えているかを示します。軸の名前をクリックすると、その軸が入った断面へ盤が回ります。\n\n"+
             "<color=#F2C97D>右の小さな図</color>　ほかの軸の組み合わせで切った断面です。クリックすると盤がその断面になります。"+
             "マウスを乗せると左に拡大され、右クリックで拡大を固定できます。図の点は駒の位置で、拡大図の点をクリックするとその駒が見える位置まで盤が動きます。\n\n"+
             "<color=#F2C97D>左の欄</color>　手番と持ち時間、選んだ駒の名前と座標、お知らせが出ます。"+
             "下の「駒をさがす」には、駒の種類ごとに「今の断面に見えている数／全部の数」が出ます。見えていない駒がある種類は橙色です。"),
            ("操作一覧","操作一覧",
             "<color=#F2C97D>基本</color>\n"+
             "左クリック：駒を選ぶ・動かす　　Tab：見えない候補の断面へ　　R：最初の表示に戻す\n"+
             "左ドラッグ／右ドラッグ：視点を回す　　ホイール：拡大・縮小　　中ドラッグ／Shift＋左ドラッグ：平行移動\n"+
             "F1：この画面　　Esc：操作の取り消し／メニューへ（対局中は確認します）\n\n"+
             "<color=#F2C97D>4次元の回転（慣れてきたら）</color>\n"+
             "Ctrl＋左ドラッグ：盤を4次元で回す（X・YとWの回転）　　Ctrl＋Shift＋左ドラッグ：ZとWの回転\n"+
             "4〜9・0・−：Ctrlで回す相手の軸（4=W、5=V…）\n"+
             "右上の球の軸をドラッグ：断面を平行に動かす（ShiftかCtrlで1マスずつ）\n"+
             "右の図・球の上でHome：原点へ　　Shift＋Home：最初の位置へ\n\n"+
             "<color=#F2C97D>メニュー</color>\n"+
             "↑↓：項目を選ぶ　　Enter／Space：決定"),
            ("N次元・設定","N次元とオプション",
             "<color=#F2C97D>次元を変える</color>　メニューの「次元」の「−」「＋」で3〜11次元を選べます。初めてなら4次元がおすすめです。"+
             "5次元以上では右の図が増え、上に大きく出る3枚がCtrl＋ドラッグで回せる断面です。8次元以上は動作が重くなります。\n\n"+
             "<color=#F2C97D>オプション</color>\n"+
             "・盤・初期配置：次元と、駒の並べ方（平面、またはある軸の方向へ9層コピー）。\n"+
             "・駒の動き設定：駒ごとの動きを変え、試し盤で確かめられます。\n"+
             "・対局ルール：持ち時間など。　・CPU設定：CPUの手を見せる演出。\n"+
             "・表示・操作：表示の切り替え、操作ヒント、音量、画質。\n\n"+
             "<color=#F2C97D>保存と不具合の報告</color>\n"+
             "設定と棋譜は「%USERPROFILE%\\AppData\\LocalLow\\piperendervt-glitch\\四次元将棋」に保存されます。"+
             "うまく動かないときは、同じ場所のPlayer.logを添えてGitHubのIssuesへお知らせください。"),
        };
        void CreateHelp()
        {
            // Wider than the stage so a wide window is dimmed edge to edge.
            helpOverlay=CfgUi.Rect(root,"Help overlay",-1200,-600,1536+2400,1024+1200);var dim=helpOverlay.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.62f);dim.raycastTarget=true;
            var panel=Cfg2Ui.Panel(helpOverlay,"Help panel",1200+138,600+70,1260,884,new Color(Dark.r,Dark.g,Dark.b,.97f),true);
            helpTitle=CfgUi.Label(panel,"",36,22,900,48,32,Gold);
            CfgUi.Label(panel,"遊び方",1060,30,170,30,18,Muted).alignment=TextAnchor.UpperRight;
            for(int i=0;i<HelpPages.Length;i++){int page=i;var b=Button(panel,"help-tab-"+i,HelpPages[i].tab,36+i*238,86,228,46,()=>ShowHelpPage(page));b.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;helpTabs.Add(b);}
            helpBody=CfgUi.Label(panel,"",44,158,1172,610,21);helpBody.supportRichText=true;helpBody.lineSpacing=1.15f;
            helpPageLabel=CfgUi.Label(panel,"",36,808,200,40,18,Muted);helpPageLabel.alignment=TextAnchor.MiddleLeft;
            Button(panel,"help-prev","◀ 前へ",650,800,170,54,()=>ShowHelpPage(helpPage-1)).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            Button(panel,"help-next","次へ ▶",830,800,170,54,()=>ShowHelpPage(helpPage+1)).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            Button(panel,"help-close","閉じる",1010,800,214,54,CloseHelp,true).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            helpOverlay.gameObject.SetActive(false);
        }
        void OpenHelp(int page=-1)
        {
            if(helpOverlay==null)return;CancelActiveInput();
            helpOverlay.SetAsLastSibling();helpOverlay.gameObject.SetActive(true);ShowHelpPage(page>=0?page:helpPage);
            PlayerPrefs.SetInt(HelpSeenKey,1);PlayerPrefs.Save();Record("HelpOpened",helpPage.ToString());
        }
        void ShowHelpPage(int page)
        {
            helpPage=Mathf.Clamp(page,0,HelpPages.Length-1);
            helpTitle.text=HelpPages[helpPage].title;helpBody.text=HelpPages[helpPage].body;helpPageLabel.text=(helpPage+1)+" / "+HelpPages.Length;
            for(int i=0;i<helpTabs.Count;i++)MarkSelected(helpTabs[i],i==helpPage);
            actions["help-prev"].interactable=helpPage>0;actions["help-next"].interactable=helpPage<HelpPages.Length-1;
        }
        void CloseHelp(){if(helpOverlay!=null&&helpOverlay.gameObject.activeSelf){helpOverlay.gameObject.SetActive(false);CfgUi.ReleaseSelection();Record("HelpClosed");}}

        // ---- 対局の中断確認 ----
        RectTransform leaveOverlay;
        void CreateLeaveQuestion()
        {
            leaveOverlay=CfgUi.Rect(root,"Leave match question",-1200,-600,1536+2400,1024+1200);var dim=leaveOverlay.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.5f);dim.raycastTarget=true;
            var panel=Cfg2Ui.Panel(leaveOverlay,"Leave panel",1200+468,600+372,600,250,Dark,true);
            CfgUi.Label(panel,"対局を中断してメニューに戻りますか？",30,26,540,40,24,Gold);
            CfgUi.Label(panel,"中断した対局は再開できません（棋譜は保存されます）。",30,76,540,60,18,Muted);
            Button(panel,"leave-cancel","対局を続ける",30,166,260,56,CloseLeaveQuestion,true).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            Button(panel,"leave-confirm","中断して戻る",310,166,260,56,()=>{CloseLeaveQuestion();ReturnFromMatch();}).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            leaveOverlay.gameObject.SetActive(false);
        }
        // A finished match leaves at once; one still being played asks first.
        void RequestLeaveMatch()
        {
            if(session==null||session.IsFinished||leaveOverlay==null){ReturnFromMatch();return;}
            CancelActiveInput();leaveOverlay.SetAsLastSibling();leaveOverlay.gameObject.SetActive(true);Record("LeaveAsked");
        }
        void CloseLeaveQuestion(){if(leaveOverlay!=null&&leaveOverlay.gameObject.activeSelf){leaveOverlay.gameObject.SetActive(false);CfgUi.ReleaseSelection();}}
        // Keys while the help or the question is up; true when the key was theirs.
        bool OverlayKeys()
        {
            if(leaveOverlay!=null&&leaveOverlay.gameObject.activeSelf){if(Input.GetKeyDown(KeyCode.Escape))CloseLeaveQuestion();return true;}
            if(helpOverlay!=null&&helpOverlay.gameObject.activeSelf)
            {
                if(Input.GetKeyDown(KeyCode.Escape)||Input.GetKeyDown(KeyCode.F1))CloseHelp();
                else if(Input.GetKeyDown(KeyCode.RightArrow))ShowHelpPage(helpPage+1);
                else if(Input.GetKeyDown(KeyCode.LeftArrow))ShowHelpPage(helpPage-1);
                return true;
            }
            if(Input.GetKeyDown(KeyCode.F1)){OpenHelp();return true;}
            return false;
        }

        // ---- メニューの次元選択 ----
        Text menuDimensions,menuLayout;
        void CreateMenuDimensions()
        {
            var panel=Cfg2Ui.Panel(menu,"Menu dimensions",660,290,330,152,Dark,true);
            CfgUi.Label(panel,"次元",18,12,120,30,20,Gold);
            Button(panel,"menu-dimensions-down","−",18,48,62,56,()=>SetDimensions(DraftDimensions-1)).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            menuDimensions=CfgUi.Label(panel,"",88,46,150,60,34,Gold);menuDimensions.alignment=TextAnchor.MiddleCenter;
            Button(panel,"menu-dimensions-up","＋",246,48,62,56,()=>SetDimensions(DraftDimensions+1)).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            menuLayout=CfgUi.Label(panel,"",18,112,300,32,15,Muted);
            RefreshMenuDimensions();
        }
        void RefreshMenuDimensions()
        {
            if(menuDimensions==null)return;int n=DraftDimensions;menuDimensions.text=n+" 次元";
            var copy=DraftCopyAxis;
            menuLayout.text=(copy.HasValue?SliceAxes.AxisName(SliceAxes.AxisOfCore(copy.Value))+"方向に 9 層・360駒":"平面の初期配置・40駒")+(n>=8?"　重くなります":n==4?"　おすすめ":"");
            actions["menu-dimensions-down"].interactable=n>BoundsN.MinDimensions;actions["menu-dimensions-up"].interactable=n<BoundsN.MaxDimensions;
        }

        // ---- 対局中の手順ヒントと「見えない候補へ」 ----
        RectTransform hintPanel;Text hintText;
        void CreateMatchGuide()
        {
            hintPanel=Cfg2Ui.Panel(matchHud,"Step hint",420,14,770,66,new Color(.02f,.03f,.045f,.80f));
            hintText=CfgUi.Label(hintPanel,"",14,3,578,60,15);hintText.supportRichText=true;hintText.alignment=TextAnchor.MiddleLeft;
            Button(hintPanel,"candidate-tour","見えない候補へ（Tab）",596,10,164,46,StepCandidateTour).GetComponentInChildren<Text>().fontSize=14;
            Button(matchHud,"match-help","？ 遊び方",1202,16,150,40,()=>OpenHelp()).GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            CreatePieceFinder();
        }
        // The visible candidates are those whose cell the section cuts: every normal offset within half a cell.
        static bool InSlice(SliceFrame frame,VecN world)=>frame.Normals.All(normal=>Math.Abs(VecN.Dot(normal,world-frame.Center))<.5);
        int HiddenCandidateCount()
        {
            if(session==null||result==null||result.Status!=QueryStatus.Complete||session.State.Find(selectedPiece)==null)return 0;
            var embedding=slice.DisplayState.Embedding;var frame=slice.Frame;
            return result.P.Count(q=>!InSlice(frame,CfgPreviewRenderer.V(q)+embedding));
        }
        void RefreshHint()
        {
            if(hintPanel==null||session==null)return;
            RefreshPieceFinder();
            bool show=HintsEnabled&&!session.IsFinished;hintPanel.gameObject.SetActive(show);
            UpdateThreat();
            if(!show)return;
            var s=session.State;bool human=session.Snapshot.Controller(s.Turn)==MatchController.Human;
            string who=session.Snapshot.Mode==MatchMode.PvP?(s.Turn==Side.A?"先手 A":"後手 B")+" の番":"あなたの番";
            string line;int hidden=0;
            if(session.PresentationPaused||!human||PresentingMove)line=session.Snapshot.Mode==MatchMode.CvC?"CPU どうしの対局です。視点はドラッグで動かせます。":session.PresentationPaused?"手番を交代しています…":"CPU が考えています。視点はドラッグで動かせます。";
            else if(session.State.Find(selectedPiece) is var piece&&piece!=null&&piece.Side==s.Turn&&result!=null)
            {
                hidden=HiddenCandidateCount();int all=result.P.Count;
                if(all==0)line=who+"：この駒は動けません。ほかの駒をクリックしてください。";
                else if(TourActive)
                    line="見えない候補 "+(tourIndex+1)+"/"+tourStops.Count+"："+tourStops[tourIndex].Name+"に "+tourStops[tourIndex].Cells.Count+" 手\n青いマスをクリックで移動。Tab："+(tourIndex+1<tourStops.Count?"次の断面へ":"元の表示に戻る");
                else line=who+"：青いマスをクリックして移動。\nこの断面に "+(all-hidden)+" 手／全 "+all+" 手"+(hidden>0?"　Tab：見えない "+hidden+" 手を見る":"");
            }
            else line=who+"：動かす駒をクリックして選びます。\n見えない駒は左の「駒をさがす」から。迷ったら F1（遊び方）。";
            // The warning takes the first line; the step keeps the line that says what to press.
            if(threatText!=""){var lines=line.Split('\n');line="<color=#FF8A6A>"+threatText+"</color>\n"+lines[lines.Length-1];}
            hintText.text=line;
            bool tourable=human&&!session.PresentationPaused&&!PresentingMove&&(hidden>0||TourActive);
            actions["candidate-tour"].gameObject.SetActive(tourable);
            if(tourable)actions["candidate-tour"].GetComponentInChildren<Text>().text=TourActive&&tourIndex+1>=tourStops.Count?"元の表示へ（Tab）":"見えない候補へ（Tab）";
        }

        // Each stop is one orientation through the selected piece that shows some of the hidden candidates: an axis-aligned section
        // where it can, a section tilted along the step otherwise (as the CPU's moves are shown). Each Tab goes to the next stop;
        // after the last it goes back to the view the tour started from.
        sealed class TourStop {public SliceFrame Orientation;public ReferenceSection View;public bool Tilted;public List<VecN> Cells;public string Name;}
        List<TourStop> tourStops;int tourIndex=-1;string tourPiece="",tourMatch="";int tourPly=-1;long tourViewRevision=-1;PivotViewState tourHome;
        int tourStage;VecN tourFrom,tourTo;Vec3 tourAnchorFrom,tourAnchorTo;float tourStart;VecN tourPieceWorld;List<VecN> tourCells;bool tourReturning;
        const float TourGlideSeconds=.5f;
        void ResetCandidateTour(){tourStops=null;tourIndex=-1;tourStage=0;}
        // A tour belongs to the piece, position and view it was built for.
        bool TourActive=>tourStops!=null&&tourIndex>=0&&tourIndex<tourStops.Count&&session!=null&&tourPiece==selectedPiece&&tourPly==session.State.Ply&&tourMatch==session.Snapshot.MatchId;
        void StepCandidateTour()
        {
            if(session==null||opened||OverlayOpen)return;
            var piece=session.State.Find(selectedPiece);var s=session.State;
            if(piece==null||piece.Side!=s.Turn||session.Snapshot.Controller(s.Turn)!=MatchController.Human||!session.CanHumanAct||PresentingMove||result==null||result.Status!=QueryStatus.Complete)
            {Notice("先に、自分の駒をクリックして選んでください。");return;}
            if(ownership.Owner!=InputOwner.None||slice.IsAnimating||tourStage!=0)return;
            bool fresh=tourStops==null||tourPiece!=selectedPiece||tourPly!=s.Ply||tourMatch!=session.Snapshot.MatchId||tourViewRevision!=slice.ViewRevision;
            if(fresh)
            {
                tourStops=BuildTourStops(piece);tourIndex=-1;tourPiece=selectedPiece;tourPly=s.Ply;tourMatch=session.Snapshot.MatchId;tourHome=slice.DisplayState;
                if(tourStops.Count==0){Notice("この駒の候補は、すべて今の断面に見えています。");ResetCandidateTour();return;}
            }
            tourIndex++;
            if(tourIndex>=tourStops.Count){BeginTourLeg(null);tourIndex=-1;Record("CandidateTour","home");return;}
            BeginTourLeg(tourStops[tourIndex]);Record("CandidateTour",(tourIndex+1)+"/"+tourStops.Count+";"+tourStops[tourIndex].Name+";"+tourStops[tourIndex].Cells.Count);
        }
        List<TourStop> BuildTourStops(Piece piece)
        {
            var embedding=slice.DisplayState.Embedding;var frame=slice.Frame;var from=CfgPreviewRenderer.V(piece.Position)+embedding;
            var remaining=result.P.Select(q=>CfgPreviewRenderer.V(q)+embedding).Where(w=>!InSlice(frame,w)).ToList();
            var current=slice.StableSection??ReferenceSection.Initial(frame.Dimensions);
            // One candidate orientation per hidden cell, the same ones merged; then the orientation that shows the most cells is taken
            // first, and so on, so the tour has as few stops as it can.
            var options=new List<TourStop>();
            foreach(var cell in remaining)
            {
                var orientation=SliceAxes.OrientationContaining(from,cell,current,out var view);
                if(options.Any(o=>SliceAxes.SameOrientation(o.Orientation,orientation)))continue;
                bool tilted=!SliceAxes.SectionOf(orientation).HasValue;
                var cells=remaining.Where(r=>orientation.Normals.All(normal=>Math.Abs(VecN.Dot(normal,r-from))<1e-6)).ToList();
                options.Add(new TourStop{Orientation=orientation,View=view,Tilted=tilted,Cells=cells,Name=tilted?"斜めの断面":view.Name+" の断面"});
            }
            var stops=new List<TourStop>();var left=new HashSet<VecN>(remaining);
            while(left.Count>0&&options.Count>0)
            {
                var best=options.OrderBy(o=>o.Tilted).ThenByDescending(o=>o.Cells.Count(left.Contains)).First();
                int gained=best.Cells.Count(left.Contains);options.Remove(best);if(gained==0)continue;
                best.Cells=best.Cells.Where(left.Contains).ToList();foreach(var cell in best.Cells)left.Remove(cell);stops.Add(best);
            }
            // Axis-aligned sections first, the largest of each kind first.
            return stops.OrderBy(o=>o.Tilted).ThenByDescending(o=>o.Cells.Count).ToList();
        }
        // Turns the board to the stop's orientation, then slides C along the normals onto the piece, raising the display when a cell
        // would sink into the wooden board. A null stop goes back to the view the tour started from.
        void BeginTourLeg(TourStop stop)
        {
            tourReturning=stop==null;var piece=session.State.Find(selectedPiece);
            tourPieceWorld=CfgPreviewRenderer.V(piece.Position)+slice.DisplayState.Embedding;tourCells=stop?.Cells??new List<VecN>();
            if(tourReturning){var home=SliceAxes.SectionOf(tourHome.Frame);if(home.HasValue)slice.Request(home.Value);else slice.Request(tourHome.Frame);}
            else if(stop.Tilted)slice.Request(stop.Orientation);else slice.Request(stop.View);
            tourStage=1;geometryDirty=true;
        }
        void UpdateCandidateTour()
        {
            if(tourStage==0)return;
            if(session==null||opened||ownership.Owner!=InputOwner.None||PresentingMove){tourStage=0;stableDirty=true;return;}
            if(tourStage==1)
            {
                if(slice.IsAnimating||slice.QueuedSection.HasValue)return;
                tourFrom=slice.Center;tourAnchorFrom=slice.DisplayState.Anchor;
                if(tourReturning){tourTo=tourHome.Frame.Center;tourAnchorTo=tourHome.Anchor;}
                else
                {
                    tourTo=tourFrom;foreach(var normal in slice.Frame.Normals)tourTo=tourTo+normal*VecN.Dot(normal,tourPieceWorld-tourTo);
                    var state=slice.DisplayState;double board=SectionView.Anchor.y;var home=NewPivotSlice().DisplayState.Anchor;
                    double lowest=VecN.Dot(slice.Frame.AxisZ,tourPieceWorld-tourTo);
                    foreach(var cell in tourCells)lowest=Math.Min(lowest,VecN.Dot(slice.Frame.AxisZ,cell-tourTo));
                    double lift=Math.Max(0,-(lowest+(home.Y-board)/state.CellScale))*state.CellScale;
                    tourAnchorTo=home+new Vec3(0,lift,0);
                }
                tourStage=2;tourStart=Time.unscaledTime;
            }
            float t=Mathf.Clamp01((Time.unscaledTime-tourStart)/TourGlideSeconds),e=t*t*(3-2*t);
            slice.Place(tourFrom+(tourTo-tourFrom)*e,tourAnchorFrom+(tourAnchorTo-tourAnchorFrom)*e);geometryDirty=true;
            if(t<1)return;
            tourStage=0;stableDirty=true;
            if(tourReturning){slice.Restore(tourHome);ResetCandidateTour();}
            tourViewRevision=slice.ViewRevision;
        }

        // ---- 駒をさがす ----
        // One row per kind: how many of that side's pieces the board's section shows, of how many there are. A click on the row
        // brings the next piece of that kind into the section (C slides along the normals, the orientation stays) and, when it is
        // the player's piece on their turn, selects it so its moves show at once.
        RectTransform pieceFinder;Side finderSide=Side.A;readonly Dictionary<string,int> finderNext=new Dictionary<string,int>();
        readonly List<(PieceKind kind,Text name,Text count,Graphic icon)> finderRows=new List<(PieceKind,Text,Text,Graphic)>();
        static readonly PieceKind[] FinderKinds={PieceKind.King,PieceKind.Rook,PieceKind.Bishop,PieceKind.Gold,PieceKind.Silver,PieceKind.Knight,PieceKind.Lance,PieceKind.Pawn};
        string finderTurn="";
        void CreatePieceFinder()
        {
            pieceFinder=Cfg2Ui.Panel(matchHud,"Piece finder",28,600,337,266,Dark,true);
            CfgUi.Label(pieceFinder,"駒をさがす",12,7,140,28,18,Gold);
            Button(pieceFinder,"finder-A","先手 A",150,6,88,30,()=>{finderSide=Side.A;RefreshPieceFinder();}).GetComponentInChildren<Text>().fontSize=15;
            Button(pieceFinder,"finder-B","後手 B",242,6,88,30,()=>{finderSide=Side.B;RefreshPieceFinder();}).GetComponentInChildren<Text>().fontSize=15;
            for(int i=0;i<FinderKinds.Length;i++)
            {
                var kind=FinderKinds[i];var b=Button(pieceFinder,"finder-"+kind,"",8,42+i*27.5f,321,26,()=>FindPiece(kind));
                var name=b.GetComponentInChildren<Text>();name.alignment=TextAnchor.MiddleLeft;name.fontSize=15;name.rectTransform.anchoredPosition=new Vector2(32,0);name.rectTransform.sizeDelta=new Vector2(140,26);
                var count=CfgUi.Label(b.transform,"",140,0,172,26,14,Muted);count.alignment=TextAnchor.MiddleRight;
                var icon=Cfg2Ui.PieceIcon(b.transform,MatchConfig.DefinitionId(kind),Side.A,9,2,17,22);
                finderRows.Add((kind,name,count,icon));
            }
        }
        // A piece shows when C's offset from its cell lies inside the piece solid along every normal of the section.
        static bool PieceInSlice(SliceFrame frame,VecN world)
        {
            var box=Cfg2MiniView.PieceBounds(frame.Dimensions);
            foreach(var normal in frame.Normals)
            {
                double d=VecN.Dot(normal,frame.Center-world),low=0,high=0;
                for(int axis=0;axis<frame.Dimensions;axis++){double a=normal.Axis(axis)*box.Min.Axis(axis),b=normal.Axis(axis)*box.Max.Axis(axis);low+=Math.Min(a,b);high+=Math.Max(a,b);}
                if(d<low-1e-6||d>high+1e-6)return false;
            }
            return true;
        }
        VecN PieceWorld(Piece piece)=>CfgPreviewRenderer.V(piece.Position)+slice.DisplayState.Embedding;
        void RefreshPieceFinder()
        {
            if(pieceFinder==null||session==null)return;
            var s=session.State;
            // Each human turn starts on the mover's own pieces; the other side stays one click away.
            string turn=session.Snapshot.MatchId+"/"+s.Ply;
            if(finderTurn!=turn&&session.Snapshot.Controller(s.Turn)==MatchController.Human&&!session.PresentationPaused){finderTurn=turn;finderSide=s.Turn;}
            MarkSelected(actions["finder-A"],finderSide==Side.A);MarkSelected(actions["finder-B"],finderSide==Side.B);
            var frame=slice.Frame;
            foreach(var row in finderRows)
            {
                int all=0,shown=0;
                foreach(var piece in s.Pieces){if(piece.Side!=finderSide||piece.Kind!=row.kind)continue;all++;if(PieceInSlice(frame,PieceWorld(piece)))shown++;}
                string id=MatchConfig.DefinitionId(row.kind);row.name.text=ShortNames.TryGetValue(id,out var name)?name:id;
                row.count.text=all==0?"なし":shown==all?"すべて見えている "+all:"見える "+shown+" ／ 全 "+all;
                row.count.color=all==0?Muted:shown<all?new Color(1,.78f,.45f):Muted;
                actions["finder-"+row.kind].interactable=all>0;
            }
        }
        void FindPiece(PieceKind kind)
        {
            if(session==null||opened)return;
            var s=session.State;var pieces=s.Pieces.Where(p=>p.Side==finderSide&&p.Kind==kind).OrderBy(p=>p.Id,StringComparer.Ordinal).ToList();if(pieces.Count==0)return;
            // From after the last piece found of this kind, the next hidden one; when all are shown, simply the next one.
            var frame=slice.Frame;string key=finderSide+"/"+kind;finderNext.TryGetValue(key,out int start);int pick=-1;
            for(int k=0;k<pieces.Count;k++){int i=(start+k)%pieces.Count;if(!PieceInSlice(frame,PieceWorld(pieces[i]))){pick=i;break;}}
            if(pick<0)pick=start%pieces.Count;finderNext[key]=pick+1;var piece=pieces[pick];
            bool mine=piece.Side==s.Turn&&session.Snapshot.Controller(s.Turn)==MatchController.Human&&session.CanHumanAct;
            if(mine){selectedPiece=piece.Id;QueueQuery();}
            string id=MatchConfig.DefinitionId(piece.Kind);string name=ShortNames.TryGetValue(id,out var shortName)?shortName:id;
            var q=piece.Position.ToArray();int n=session.Config.Dimensions;
            string cell=string.Join(" ",SliceAxes.Axes(n).Select(axis=>{int core=SliceAxes.CoreAxis(axis);return SliceAxes.AxisName(axis)+"="+(core<q.Length?q[core]:0);}));
            if(!mine)Notice(name+"（"+(piece.Side==Side.A?"先手 A":"後手 B")+"）　"+cell);
            Record("FindPiece",piece.Id+";"+cell);
            FocusOn(PieceWorld(piece));
        }
        // Slides C along the section's normals until the cell's piece is cut by the section; the orientation is kept.
        void FocusOn(VecN world)
        {
            if(ownership.Owner!=InputOwner.None||slice.IsAnimating||PresentingMove)return;
            ResetCandidateTour();
            if(PieceInSlice(slice.Frame,world))return;
            var box=Cfg2MiniView.PieceBounds(slice.Frame.Dimensions);var middle=world;
            for(int axis=0;axis<slice.Frame.Dimensions;axis++)middle=middle.WithAxis(axis,world.Axis(axis)+(box.Min.Axis(axis)+box.Max.Axis(axis))*.5);
            tourPieceWorld=middle;tourCells=new List<VecN>();tourReturning=false;tourStage=1;geometryDirty=true;
        }

        // ---- 王が狙われているときの知らせ ----
        string threatText="",threatMatch="";int threatPly=-1;
        void UpdateThreat()
        {
            var s=session.State;if(threatPly==s.Ply&&threatMatch==session.Snapshot.MatchId)return;
            threatPly=s.Ply;threatMatch=session.Snapshot.MatchId;threatText="";
            if(session.IsFinished||session.Snapshot.Controller(s.Turn)!=MatchController.Human)return;
            var preview=session.ToPreview();var preset=session.Snapshot.Config.Preset;var names=new List<string>();
            foreach(var king in s.Pieces.Where(p=>p.Side==s.Turn&&p.Kind==PieceKind.King))
            {
                var attack=PreviewQuery.Attackers(preset,preview,king.Position,RuleCore.Other(s.Turn));
                foreach(var attacker in attack.Attackers)names.Add(ShortNames.TryGetValue(attacker.DefinitionId,out var name)?name:attacker.DefinitionId);
            }
            if(names.Count==0)return;
            threatText="⚠ 王が狙われています（"+string.Join("・",names.Distinct())+"）。王を選ぶと逃げ道が青く出ます。";
            Record("KingThreatened",string.Join(",",names));
        }
    }
}
