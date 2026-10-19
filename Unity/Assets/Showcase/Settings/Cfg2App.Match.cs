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
        CpuResult cpuShowResult;int cpuShowStage;double cpuShowLift;VecN cpuShowFromCell,cpuShowToCell;Vec3 cpuShowAnchor;float cpuShowStart;VecN cpuShowFrom,cpuShowTo;ReferenceSection cpuShowView;bool cpuShowTilted;
        // The space and section the hyperplane panels were built for.
        int minisDimensions,minisActive;float minisRight;ReferenceSection minisSection;RectTransform matchGizmo,magnifiedSource;Cfg2MiniView magnifier;Text guideText;
        // The right column of hyperplane panels, shared by the panels and the magnifier left of them.
        const float MinisTop=254,MinisBottom=866,MinisLeftLimit=1290,MinisColumnWidth=300,MagnifierSize=300;
        static float MinisLeft(float right)=>Mathf.Min(MinisLeftLimit,right-MinisColumnWidth);
        readonly List<RectTransform> minisLabels=new List<RectTransform>();readonly List<Cfg2MiniView> magnifiable=new List<Cfg2MiniView>();
        // Panels narrower than this show the magnifier when hovered (the 4D panels, the Ctrl-axis group and the small grid).
        const float MagnifiableWidth=250;
        // A right click pins the magnifier: it stays when the pointer leaves, until another right click. Unpinned it stays while the
        // pointer is on its panel or on the magnifier itself, and a moment longer so the pointer can cross the gap between them.
        bool magnifierPinned;float magnifierHoverTime,hoverCandidateTime;Cfg2MiniView hoverCandidate;Text magnifierPinLabel;const float MagnifierGraceSeconds=.35f,MagnifierSwitchSeconds=.25f;
        // A clicked magnifier dot glides C to where that piece is on the board, the orientation unchanged.
        VecN sectionGlideFrom,sectionGlideTo;float sectionGlideStart=-1;const float SectionGlideSeconds=.4f;
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
        // The selected piece's name in the left column, and the pieces as dots on the magnified and the column's hyperplane panels.
        const string PieceNameKey="cfg2.selected-piece-name",MagnifierDotsKey="cfg2.panel-dots-magnified",PanelDotsKey="cfg2.panel-dots-small";
        bool PieceNameEnabled{get=>Pref(PieceNameKey);set=>SetPref(PieceNameKey,value);}
        bool MagnifierDotsEnabled{get=>Pref(MagnifierDotsKey);set=>SetPref(MagnifierDotsKey,value);}
        bool PanelDotsEnabled{get=>Pref(PanelDotsKey);set=>SetPref(PanelDotsKey,value);}
        Text selectionText;
        // Off (default): the right column keeps the panels of the start section in place and marks whichever one the board shows.
        // On: the column is rebuilt around the board's section, the panels gliding to their new places.
        const string PanelsFollowKey="cfg2.panels-follow-section";
        bool PanelsFollowSection{get=>PlayerPrefs.GetInt(PanelsFollowKey,0)!=0;set=>SetPref(PanelsFollowKey,value);}
        ReferenceSection LayoutSection=>PanelsFollowSection?slice.TargetSection:ReferenceSection.Initial(slice.Frame.Dimensions);
        readonly List<Color?> minisBorders=new List<Color?>();
        // The pieces handed to the panels with their last redraw; the magnifier redraws when they change.
        List<(VecN world,Side side,bool selected)> panelPieces;long panelPiecesRevision;
        void CreateMatchHud()
        {
            matchHud=CfgUi.Rect(root,"Match view",0,0,1536,1024);
            matchSurface=CfgUi.Rect(matchHud,"Actual match board input",0,0,1536,1024);var input=matchSurface.gameObject.AddComponent<Image>();input.color=Color.clear;AttachPointer(matchSurface);
            CfgUi.Label(matchHud,"N次元将棋",25,17,380,50,36,Gold);CfgUi.Label(matchHud,"N-DIMENSIONAL SHOGI",28,70,370,23,14,Gold);hudDimensions=CfgUi.Label(matchHud,"",215,24,160,40,26,Gold);
            var far=Cfg2Ui.Panel(matchHud,"Far clock",28,128,337,105,Dark);clockFar=CfgUi.Label(far,"",17,14,303,80,25);
            var near=Cfg2Ui.Panel(matchHud,"Near clock",28,872,352,117,Dark);clockNear=CfgUi.Label(near,"",17,15,318,86,27,Gold);
            Button(matchHud,"match-menu","☰ メニュー",1360,16,150,40,RequestLeaveMatch);
            // The hyperplane panels are built by StartMatch for the match's own space.
            CreateGizmo(matchHud,1310,70,"match-",200,175);matchGizmo=(RectTransform)gizmos[gizmos.Count-1].transform;
            matchStatus=CfgUi.Label(matchHud,"",30,248,330,100,17,Muted);
            selectionText=CfgUi.Label(matchHud,"",30,244,330,62,22,Gold);selectionText.supportRichText=true;selectionText.raycastTarget=false;
            // Notices during play sit under the status in the left column, clear of the board and the right column.
            hudNotice=CfgUi.Label(matchHud,"",30,352,330,200,15,Gold);
            var guide=Cfg2Ui.Panel(matchHud,"Mouse guide",394,957,796,49,new Color(.02f,.03f,.045f,.78f));
            guideText=CfgUi.Label(guide,GuideText(4),10,7,776,35,15,new Color(.91f,.91f,.88f));guideText.alignment=TextAnchor.MiddleCenter;
            Button(matchHud,"pause","一時停止",1202,877,142,43,TogglePause);
            Button(matchHud,"step","1手進める",1352,877,158,43,StepCpu);
            Button(matchHud,"retry","処理を再試行",1202,928,308,43,()=>{CancelCpu();session?.RetryProcessing();});
            resultPanel=Cfg2Ui.Panel(matchHud,"Game result",515,772,565,212,Dark,true);resultText=CfgUi.Label(resultPanel,"",20,16,525,82,26,Gold);
            Button(resultPanel,"next-match","次の対局",20,127,251,56,()=>StartMatch(lastMode));Button(resultPanel,"result-menu","メニュー",285,127,260,56,ReturnFromMatch);resultPanel.gameObject.SetActive(false);
            CreateMatchGuide();
        }
        static string GuideText(int dimensions)=>"ドラッグ：視点回転　ホイール：ズーム　"+(dimensions>3?"Ctrl＋ドラッグ：高次元回転（4〜9・0・−キーで回す軸）　":"")+"R：表示リセット";
        // A match that cannot be created leaves everything as it was; one whose screen cannot be set up is ended like a menu return,
        // so a failure never leaves a half-started match behind. A lesson passes its own rules; every other match plays the options'.
        void StartMatch(MatchMode mode,IReadOnlyList<Piece> fixture=null,string fixtureId=null,MatchConfig rules=null)
        {
            CancelActiveInput();if(rules==null)EndLesson();
            ValidateSettings();var use=rules??config;if(use==null){Notice("入力を確認してください："+string.Join(" / ",validation.Errors));if(session!=null)ReturnFromMatch();OpenSettings();return;}
            if(settingsDirty&&!AutoSave())return;
            // Only unsaved settings hold the next match back; a match display that cannot be saved has been reported and is left behind.
            if(!SaveStableView()&&(session==null||opened))return;
            MatchSession next;var clock=new Cfg2MatchClock();
            try{next=fixture==null?MatchSession.Create(use,mode,clock):MatchSession.CreateFixture(use,mode,clock,fixture,fixtureId);}
            catch(Exception e){Debug.LogException(e);Notice("対局を開始できません："+e.Message);return;}
            if(session==null){trialYaw=yaw;trialPitch=pitch;trialDistance=distance;trialPan=pan;trialSelectedPiece=selectedPiece;trialSelectedCell=selectedCell;trialPlaneReference=planeReferenceCell;}
            if(session!=null){session.Interrupt("次局開始");SaveMatchRecord();}CancelCpu();ownership.Cancel();
            session=next;matchClock=clock;lastMode=mode;rotationAxis=LogicalAxis.W;ResetCandidateTour();threatPly=-1;
            try
            {
                opened=false;settings.gameObject.SetActive(false);menu.gameObject.SetActive(false);matchHud.gameObject.SetActive(true);resultPanel.gameObject.SetActive(false);KeepScreenNotice();
                backgroundCamera.enabled=false;
                rig.Camera.targetTexture=null;slice=NewPivotSlice();previewRenderer.SetDisplayState(slice.DisplayState);RebuildMinis();hudDimensions.text=session.Config.Dimensions+"次元";guideText.text=GuideText(session.Config.Dimensions);UpdateWindowTitle();selectedPiece="";selectedCell=default;shownPly=-1;presentationStart=-1;cameraSide=Side.A;manualCamera=false;
                yaw=27;pitch=31;distance=1.04f;pan=Vector3.zero;SetMatchCamera(Side.A,false);QueueQuery();RefreshMatch();SaveMatchRecord();
            }
            catch(Exception e){Debug.LogException(e);Record("MatchStartAbandoned",e.Message);ReturnFromMatch();Notice("対局を開始できません："+e.Message);return;}
            // The launcher's saved view belongs to the first match of this process only.
            string restoredView=playViewApplied?"":Arg("--pivot-play-view","");if(restoredView!=""){playViewApplied=true;RestoreSavedMatchView(restoredView);}
            Record("MatchStarted",mode+";"+session.Snapshot.MatchId+";fixture="+session.Snapshot.FixtureId+";rules="+session.Config.RuleHash+";initial="+session.Snapshot.InitialHash);
            WritePlayerIdentity("Play");
        }
        // In 4D the four panels are the four reference views, as always. In higher N the column reaches the right edge of the
        // screen: on top the start section (or, when the column follows the board, the section the display is heading to), then the three sections that swap in the Ctrl-rotation axis
        // (the only ones that stay planes while Ctrl+drag turns the view), then every other swap section small, one row of
        // three per hidden axis. Hovering a small panel shows it magnified to the left of the column. A 3D board hides no axis:
        // its column is the current section alone. When only the sections change (same N, same places) the panels are kept
        // and shown the new sections; rebuild forces new panels.
        void RebuildMinis(bool rebuild=false)
        {
            int n=slice.Frame.Dimensions;var current=LayoutSection;bool follow=PanelsFollowSection;float right=MinisRight();
            foreach(var mini in minis)mini.EndGlide();
            var panels=new List<(float x,float y,float w,float h,ReferenceSection view,Color? border,bool small)>();var labels=new List<(float x,float y,float w,string text,Color color)>();
            var gizmo=new Vector2(1310,-70);
            if(n==4)for(int i=0;i<4;i++)panels.Add((1310,MinisTop+i*133,200,125,ReferenceSection.Of((ReferenceView)i),null,false));
            else
            {
                const float gap=6;float left=MinisLeft(right),width=right-left,y=MinisTop;
                if(matchGizmo!=null)gizmo=new Vector2(left+(width-matchGizmo.sizeDelta.x)*.5f,-70);
                panels.Add((left,y,width,150,current,null,false));y+=150+gap;
                var hidden=current.Hidden;int active=Array.IndexOf(hidden,RotationCoreAxis);if(active<0)active=0;
                if(hidden.Length>0)
                {
                    var neighbours=current.Neighbours;var activeColor=Cfg2Projection.AxisColor(hidden[active]);
                    string activeName=SliceAxes.AxisName(SliceAxes.AxisOfCore(hidden[active]));
                    labels.Add((left,y,width,follow?"Ctrl回転軸 "+activeName+" で行ける断面":activeName+" を入れた断面（Ctrl回転軸 "+activeName+"）",activeColor));y+=20;
                    float activeWidth=(width-2*gap)/3,activeHeight=Mathf.Min(140,activeWidth);
                    for(int j=0;j<3;j++)panels.Add((left+j*(activeWidth+gap),y,activeWidth,activeHeight,neighbours[active*3+j],activeColor,false));
                    y+=activeHeight+gap;
                    int groups=hidden.Length-1;
                    if(groups>0)
                    {
                        labels.Add((left,y,width,follow?"ほかの軸（4〜9・0・−キーで上に表示）":"ほかの軸を入れた断面（4〜9・0・−キーで上に表示）",new Color(.75f,.79f,.85f)));y+=19;
                        int perRow=groups<=4?1:2,rows=(groups+perRow-1)/perRow,columns=3*perRow;const float smallGap=5;
                        float cellWidth=(width-(columns-1)*smallGap)/columns,cellHeight=Mathf.Min(activeHeight,(MinisBottom-y-(rows-1)*smallGap)/rows);
                        for(int g=0,slot=0;g<hidden.Length;g++)
                        {
                            if(g==active)continue;int row=slot/perRow,firstColumn=slot%perRow*3;slot++;
                            for(int j=0;j<3;j++)panels.Add((left+(firstColumn+j)*(cellWidth+smallGap),y+row*(cellHeight+smallGap),cellWidth,cellHeight,neighbours[g*3+j],Cfg2Projection.AxisColor(hidden[g]),true));
                        }
                    }
                }
            }
            // A pinned magnifier outlives a rebuild of the column within the same space.
            HideMagnifier(n!=minisDimensions);minisDimensions=n;minisSection=current;minisActive=RotationCoreAxis;minisRight=right;
            minisBorders.Clear();minisBorders.AddRange(panels.Select(p=>p.border));
            if(matchGizmo!=null)matchGizmo.anchoredPosition=gizmo;
            bool Same(RectTransform r,float x,float y,float w,float h)=>r!=null&&r.anchoredPosition==new Vector2(x,-y)&&r.sizeDelta==new Vector2(w,h);
            if(!rebuild&&minis.Count==panels.Count&&minisLabels.Count==labels.Count&&minis.Select((m,i)=>Same((RectTransform)m.transform,panels[i].x,panels[i].y,panels[i].w,panels[i].h)).All(x=>x)&&minisLabels.Select((l,i)=>Same(l,labels[i].x,labels[i].y,labels[i].w,19)).All(x=>x))
            {
                // A section that stays in the column glides from its old place to its new one; a new section grows in where it lands.
                var was=new Dictionary<ReferenceSection,(Vector2 position,Vector2 size)>();
                foreach(var mini in minis){var r=(RectTransform)mini.transform;if(!was.ContainsKey(mini.Reference))was.Add(mini.Reference,(r.anchoredPosition,r.sizeDelta));}
                for(int i=0;i<panels.Count;i++)
                {
                    var from=minis[i].Reference;minis[i].Retarget(panels[i].view);PanelFrame(minis[i].transform,panels[i].border);minis[i].Highlighted=false;
                    if(from==panels[i].view)continue;
                    if(was.TryGetValue(panels[i].view,out var old))minis[i].GlideFrom(old.position,old.size);else minis[i].GlideFrom(null,null);
                }
                for(int i=0;i<labels.Count;i++){var text=minisLabels[i].GetComponentInChildren<Text>();text.text=labels[i].text;text.color=labels[i].color;}
                Record("MinisRetargeted",current.Name);return;
            }
            foreach(var mini in minis){CfgUi.ReleaseSelection(mini.transform);helperPanels.Remove((RectTransform)mini.transform);Destroy(mini.gameObject);}minis.Clear();
            foreach(var label in minisLabels){helperPanels.Remove(label);Destroy(label.gameObject);}minisLabels.Clear();magnifiable.Clear();
            foreach(var p in panels)
            {
                CreateMini(matchHud,p.x,p.y,p.w,p.h,p.view,p.border);var mini=minis[minis.Count-1];
                if(p.small)mini.DimWhenFlat=true;if(p.w<MagnifiableWidth)magnifiable.Add(mini);
            }
            foreach(var l in labels)MinisLabel(l.text,l.x,l.y,l.w,l.color);
        }
        // The right edge of the screen in stage units: the stage is centred, so a wide window leaves spare room beside it.
        float MinisRight()
        {
            float canvasWidth=canvas!=null?((RectTransform)canvas.transform).rect.width:1536;
            return Mathf.Max(1510,1536+Mathf.Max(0,(canvasWidth-1536)*.5f)-14);
        }
        // A dark strip behind the label keeps it readable over the bright piece tray.
        void MinisLabel(string text,float x,float y,float w,Color color)
        {
            var back=CfgUi.Rect(matchHud,"Hyperplane label",x,y,w,19);var image=back.gameObject.AddComponent<Image>();image.color=new Color(.02f,.03f,.045f,.72f);image.raycastTarget=false;
            var label=CfgUi.Label(back,text,2,0,w-4,19,13,color);label.alignment=TextAnchor.MiddleLeft;minisLabels.Add(back);helperPanels.Add(back);
        }
        void EnsureMinis()
        {
            if(matchHud==null||!matchHud.gameObject.activeInHierarchy)return;int n=slice.Frame.Dimensions;
            if(minis.Count>0&&minisDimensions==n&&(n==4||minisSection==LayoutSection&&minisActive==RotationCoreAxis&&Mathf.Abs(minisRight-MinisRight())<.5f))return;
            RebuildMinis();
        }
        // One magnified copy of the hovered (or pinned) panel, drawn left of the column. Its axes drag C like the panel's own,
        // its title turns the board to its section, and a click on a dot moves C to that piece.
        void UpdateMagnifier()
        {
            if(session==null||!showHelpers||minis.Count==0||!matchHud.gameObject.activeInHierarchy){HideMagnifier(true);return;}
            bool shown=magnifier!=null&&magnifier.gameObject.activeSelf;
            if(!magnifierPinned&&!(shown&&magnifier.Dragging))
            {
                if(ownership.Owner!=InputOwner.None){HideMagnifier();return;}
                Vector2 pointer=Input.mousePosition;var hovered=magnifiable.FirstOrDefault(m=>m.IsPointerOver(pointer));
                // With a magnifier up, another panel takes over only after the pointer rests on it a moment, so the pointer can
                // cross the panels between its own and the magnifier.
                if(hovered!=hoverCandidate){hoverCandidate=hovered;hoverCandidateTime=Time.unscaledTime;}
                bool other=shown&&hovered!=null&&(RectTransform)hovered.transform!=magnifiedSource;
                if(hovered!=null&&(!other||Time.unscaledTime-hoverCandidateTime>=MagnifierSwitchSeconds)){magnifierHoverTime=Time.unscaledTime;ShowMagnifier(hovered);}
                else if(other)magnifierHoverTime=Time.unscaledTime;
                else if(shown&&magnifier.IsPointerOver(pointer))magnifierHoverTime=Time.unscaledTime;
                else if(!shown||Time.unscaledTime-magnifierHoverTime>MagnifierGraceSeconds){HideMagnifier();return;}
            }
            if(magnifier==null||!magnifier.gameObject.activeSelf)return;
            if(magnifier.ViewRevision!=slice.ViewRevision||magnifier.CameraRevision!=cameraRevision||magnifier.PiecesRevision!=panelPiecesRevision)
                magnifier.Refresh(slice.Frame,rig.Camera.transform.rotation,cameraRevision,previewRenderer.HalfWindow,slice.DisplayState.Embedding,MagnifierDotsEnabled?panelPieces:null,panelPiecesRevision);
        }
        // Builds the magnifier for a panel, unless it already shows that panel; the next UpdateMagnifier draws it.
        void ShowMagnifier(Cfg2MiniView panel)
        {
            const float size=MagnifierSize;var source=(RectTransform)panel.transform;
            if(magnifier==null)
            {
                var r=Cfg2Ui.Panel(matchHud,"Hyperplane magnifier",0,0,size,size,new Color(.03f,.05f,.10f,.82f),true);
                magnifier=r.gameObject.AddComponent<Cfg2MiniView>();magnifier.DotClicked=BringPieceIntoSection;magnifier.PinRequested=()=>PinMagnifier(magnifier);
            }
            var rect=(RectTransform)magnifier.transform;
            if(magnifier.gameObject.activeSelf&&magnifier.Reference==panel.Reference&&magnifiedSource==source)return;
            magnifiedSource=source;magnifier.gameObject.SetActive(true);rect.SetAsLastSibling();
            float y=Mathf.Clamp(-source.anchoredPosition.y+source.sizeDelta.y*.5f-size*.5f,MinisTop,MinisBottom-size);
            rect.anchoredPosition=new Vector2(MinisLeft(minisRight)-14-size,-y);
            magnifier.Initialize(()=>slice,ownership,panel.Reference,()=>{geometryDirty=true;stableDirty=true;},v=>{slice.Request(v);geometryDirty=true;},()=>canvas.scaleFactor);
            // The frame takes the colour of the axis the panel brings in; a panel of the column's own section keeps the plain glass.
            var glass=Cfg2Ui.Frame(rect);var added=panel.Reference.Shown.Where(axis=>!minisSection.Shows(axis)).Select(axis=>(int?)axis).FirstOrDefault();
            if(glass!=null){var color=added.HasValue?Cfg2Projection.AxisColor(added.Value):new Color(.80f,.87f,1);glass.color=new Color(color.r,color.g,color.b,.85f);glass.Glow=true;glass.SetVerticesDirty();}
            magnifierPinLabel=CfgUi.Label(rect,"",10,28,size-20,18,12,Muted);magnifierPinLabel.alignment=TextAnchor.UpperRight;magnifierPinLabel.raycastTarget=false;ShowPinState();
            // Initialize leaves the old drawing to the next Refresh.
            magnifier.Refresh(slice.Frame,rig.Camera.transform.rotation,cameraRevision,previewRenderer.HalfWindow,slice.DisplayState.Embedding,MagnifierDotsEnabled?panelPieces:null,panelPiecesRevision);
        }
        // Right click on a panel pins its magnifier (or unpins it, if that one is pinned); right click on the magnifier toggles its pin.
        void PinMagnifier(Cfg2MiniView panel)
        {
            if(session==null||!matchHud.gameObject.activeInHierarchy)return;
            bool shown=magnifier!=null&&magnifier.gameObject.activeSelf;
            if(panel==magnifier)magnifierPinned=!magnifierPinned;
            else if(magnifierPinned&&shown&&magnifier.Reference==panel.Reference)magnifierPinned=false;
            else{ShowMagnifier(panel);magnifierPinned=true;}
            magnifierHoverTime=Time.unscaledTime;ShowPinState();Record("MagnifierPin",(magnifierPinned?"on;":"off;")+panel.Reference.Name);
        }
        void ShowPinState()
        {
            if(magnifierPinLabel==null)return;
            magnifierPinLabel.text=magnifierPinned?"固定中（右クリックで解除）":"右クリックで固定";magnifierPinLabel.color=magnifierPinned?Gold:new Color(.62f,.68f,.78f);
        }
        // C moves, the orientation stays, until the clicked piece is in the board's section: by the least move when the magnifier's
        // section meets the board's in a line or a plane, onto the piece itself when they coincide.
        void BringPieceIntoSection(VecN world)
        {
            if(session==null||magnifier==null)return;
            if(ownership.Owner!=InputOwner.None||slice.IsAnimating||slice.QueuedSection.HasValue||PresentingMove){Record("DotMoveRefused",magnifier.Reference.Name);return;}
            var frame=slice.Frame;int n=frame.Dimensions;var piece=Cfg2MiniView.PieceBounds(n);var target=world;
            // Aim at the middle of the piece solid (its cell is the bottom of the solid on the height axis).
            for(int axis=0;axis<n;axis++)target=target.WithAxis(axis,world.Axis(axis)+(piece.Min.Axis(axis)+piece.Max.Axis(axis))*.5);
            var to=SliceAxes.CenterReaching(frame,magnifier.Reference,target,magnifier.IsCoincident,previewRenderer.HalfWindow);
            Record("DotMove",magnifier.Reference.Name+(magnifier.IsCoincident?";centre":""));
            if((to-frame.Center).Length<1e-9)return;
            sectionGlideFrom=frame.Center;sectionGlideTo=to;sectionGlideStart=Time.unscaledTime;StepSectionGlide();
        }
        void StepSectionGlide()
        {
            if(sectionGlideStart<0)return;
            // Any other control of the view (a drag, a turn, a CPU show) ends the glide where it is.
            if(ownership.Owner!=InputOwner.None||slice.IsAnimating||PresentingMove||session==null){sectionGlideStart=-1;stableDirty=true;return;}
            float t=Mathf.Clamp01((Time.unscaledTime-sectionGlideStart)/SectionGlideSeconds),s=t*t*(3-2*t);
            slice.SetCenter(sectionGlideFrom+(sectionGlideTo-sectionGlideFrom)*s);geometryDirty=true;
            if(t>=1){sectionGlideStart=-1;stableDirty=true;}
        }
        // The match's pieces in display space (a gliding CPU piece where it is drawn now), for the panels' dots. Null outside a match.
        List<(VecN world,Side side,bool selected)> PanelPieces()
        {
            if(session==null||opened)return null;var embedding=slice.DisplayState.Embedding;float t=GlideProgress,smooth=t*t*(3-2*t);
            var list=new List<(VecN,Side,bool)>(session.State.Pieces.Count);
            foreach(var piece in session.State.Pieces)
            {
                var cell=glide!=null&&piece.Id==glide.Id?glide.From+(glide.To-glide.From)*smooth:CfgPreviewRenderer.V(piece.Position);
                list.Add((cell+embedding,piece.Side,piece.Id==selectedPiece));
            }
            return list;
        }
        // 「選択中：飛車（先手 A）」 in the piece's dot colour, its cell on the line below.
        string SelectionText()
        {
            if(session==null||!PieceNameEnabled)return "";var piece=session.State.Find(selectedPiece);if(piece==null)return "";
            string id=MatchConfig.DefinitionId(piece.Kind);string name=ShortNames.TryGetValue(id,out var shortName)?shortName:session.Snapshot.Config.Preset.Find(id)?.Name??piece.Kind.ToString();
            var q=piece.Position.ToArray();int n=session.Config.Dimensions;
            string cell=string.Join(" ",SliceAxes.Axes(n).Select(axis=>{int core=SliceAxes.CoreAxis(axis);return SliceAxes.AxisName(axis)+"="+(core<q.Length?q[core]:0);}));
            return "選択中：<color=#"+ColorUtility.ToHtmlStringRGB(Cfg2PanelDot.Of(piece.Side))+">"+name+"</color>（"+(piece.Side==Side.A?"先手 A":"後手 B")+"）\n<size=14>"+cell+"</size>";
        }
        // With the column held in place the board's section can be any panel: that one gets a gold frame (the top panel keeps its glass,
        // as it does whenever the column follows the board).
        void MarkBoardSection()
        {
            for(int i=0;i<minis.Count&&i<minisBorders.Count;i++)
            {
                bool mark=i>0&&minis[i].IsCoincident;if(mark==minis[i].Highlighted)continue;minis[i].Highlighted=mark;
                if(!mark){PanelFrame(minis[i].transform,minisBorders[i]);continue;}
                var glass=Cfg2Ui.Frame(minis[i].transform);if(glass!=null){glass.color=new Color(Gold.r,Gold.g,Gold.b,.95f);glass.Glow=true;glass.SetVerticesDirty();}
            }
        }
        // A pinned magnifier stays unless unpin is asked for (leaving the match, another space, the helpers turned off).
        void HideMagnifier(bool unpin=false)
        {
            if(magnifierPinned&&!unpin&&magnifier!=null&&magnifier.gameObject.activeSelf)return;
            magnifierPinned=false;magnifiedSource=null;if(magnifier!=null&&magnifier.gameObject.activeSelf){magnifier.CancelDrag();magnifier.gameObject.SetActive(false);}
        }
        void ReturnFromMatch()
        {
            CancelActiveInput();
            EndLesson();if(session==null){ShowMenu();return;}CancelCpu();session.Interrupt();SaveMatchRecord();Record("MatchLeft",session.State.Outcome+";"+session.State.Hash);session=null;
            // The match panels wait, hidden, for the next StartMatch to show them that match's space.
            selectedPiece=preview.Find(trialSelectedPiece)!=null?trialSelectedPiece:preview.Pieces.FirstOrDefault()?.Id??"";selectedCell=trialSelectedCell;planeReferenceCell=trialPlaneReference;slice=trialSlice;yaw=trialYaw;pitch=trialPitch;distance=trialDistance;pan=trialPan;rotationAxis=LogicalAxis.W;ShowMenu();
        }
        // Lesson games are practice: they leave no record.
        void SaveMatchRecord()
        {if(session!=null&&!LessonGame(session))try{MatchRecordStore.Save(Path.Combine(recordRoot,session.Snapshot.MatchId+".xml"),session);}catch(Exception e){Notice("棋譜の保存に失敗："+e.Message);}}
        void TogglePause()
        {
            if(session==null)return;CancelCpu();if(session.IsPaused)session.ResumeCvc();else session.PauseCvc();
            presentationStart=-1;RefreshMatch();SaveMatchRecord();Record("CvCPause",session.Phase.ToString());
        }
        void StepCpu(){if(session==null)return;CancelCpu();session.StepCvc();RefreshMatch();Record("CvCStep",session.Generation.ToString());}
        // keepGlide lets the move that ended the game finish its glide and its sound.
        void CancelCpu(bool keepGlide=false)
        {cpuCancel?.Cancel();cpuCancel?.Dispose();cpuCancel=null;pendingCpu=null;cpuShowResult=null;cpuShowStage=0;resetStage=0;if(!keepGlide)glide=null;if(slice!=null)slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;if(session!=null)session.CpuClockHeld=false;}
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
        // A gesture that moves the section itself holds the view: a 4D rotation, or an axis drag that moves C. Camera gestures
        // (orbit, pan, click) leave the CPU's shown move and the turn reset running.
        bool SectionHeld=>ownership.Owner==InputOwner.Rotation4D||ownership.Owner==InputOwner.Axis;
        float GlideProgress=>glide==null?1:Mathf.Clamp01((Time.unscaledTime-glide.Start)/glide.Seconds);
        CfgVisualPiece[] WithGlide(CfgVisualPiece[] pieces)
        {
            if(glide==null)return pieces;float t=GlideProgress,s=t*t*(3-2*t);
            foreach(var piece in pieces)if(piece.Id==glide.Id)piece.Glide=glide.From+(glide.To-glide.From)*s;
            return glide.Victim==null||t>=.98f?pieces:pieces.Concat(new[]{glide.Victim}).ToArray();
        }
        bool BeginCpuShow(CpuResult cpu)
        {
            if(!CpuShowEnabled||cpu.Status!=MatchQueryStatus.Complete||!cpu.Move.HasValue||SectionHeld)return false;
            var move=cpu.Move.Value;var embedding=slice.DisplayState.Embedding;
            var from=CfgPreviewRenderer.V(move.From)+embedding;var to=CfgPreviewRenderer.V(move.To)+embedding;
            // A step on four or more axes leaves every axis-aligned section; the view then tilts so the step still lies in it.
            var orientation=SliceAxes.OrientationContaining(from,to,slice.StableSection??ReferenceSection.Initial(slice.Frame.Dimensions),out cpuShowView);
            cpuShowTilted=!SliceAxes.SectionOf(orientation).HasValue;
            cpuShowFromCell=from;cpuShowToCell=to;
            cpuShowResult=cpu;cpuShowStage=1;slice.SecondsPerLeg=CpuTurnSeconds;if(cpuShowTilted?slice.Request(orientation):slice.Request(cpuShowView))geometryDirty=true;
            Record("CpuShow",cpuShowView.Name+(cpuShowTilted?"~tilted":"")+";"+move);return true;
        }
        void UpdateCpuShow()
        {
            if(cpuShowResult==null)return;
            // The player took the view mid-show: keep their view and commit at once.
            if(SectionHeld){FinishCpuShow();return;}
            if(cpuShowStage==1)
            {
                if(slice.IsAnimating||slice.QueuedSection.HasValue)return;
                // C slides along the hidden axes of the shown section until the moving piece's cell lies in it.
                slice.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;cpuShowFrom=slice.Center;cpuShowTo=slice.Center;
                if(cpuShowTilted)foreach(var normal in slice.Frame.Normals)cpuShowTo=cpuShowTo+normal*VecN.Dot(normal,cpuShowFromCell-cpuShowTo);
                else foreach(int hidden in cpuShowView.Hidden)cpuShowTo=SliceAxes.WithCoordinate(cpuShowTo,hidden,SliceAxes.Coordinate(cpuShowFromCell,hidden));
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
            if(t<1||slice.IsAnimating||slice.QueuedSection.HasValue)return false;
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
                CancelCpu(keepGlide:true);if(lastRecordedMatch!=session.Snapshot.MatchId){lastRecordedMatch=session.Snapshot.MatchId;SaveMatchRecord();Record("MatchEnded",session.State.Outcome+";winner="+session.State.Winner+";"+session.State.Hash);}
            }
            if(glide!=null){geometryDirty=true;if(GlideProgress>=1){glide=null;PlayPieceSound();}}
            // The hand-over waits until the moving piece has arrived.
            if(session.PresentationPaused&&glide==null)
            {
                if(SectionHeld)
                {CancelTurnReset();session.CompletePresentation();presentationStart=-1;Record("PresentationCompleted","input owner retained the view");}
                else
                {
                    // A lesson keeps the camera on A's side: B's answers are the lesson's own.
                    if(presentationStart<0){presentationStart=Time.unscaledTime;bool pvp=session.Snapshot.Mode==MatchMode.PvP&&!LessonActive;presentationSeconds=pvp?TurnChangeSeconds:.45f;if(!manualCamera)SetMatchCamera(pvp?session.State.Turn:Side.A,true);BeginTurnReset();}
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
            selectionText.text=SelectionText();bool named=selectionText.text!="";
            // The name takes the top of the left column; status and notices move down under it.
            matchStatus.rectTransform.anchoredPosition=new Vector2(30,named?-310:-248);hudNotice.rectTransform.anchoredPosition=new Vector2(30,named?-414:-352);
            matchStatus.text=session.Phase==MatchPhase.ProcessingPaused?"処理を停止しています\n"+session.FaultReason:session.IsPaused?"一時停止中":session.PresentationPaused?(resetStage!=0?"手番交代：初期表示へ戻しています":"手番交代中"):cpuShowResult!=null?"CPU 着手："+cpuShowView.Name+(cpuShowTilted?" 超平面（斜め）":" 超平面"):session.CanCpuAct?"CPU 思考中":selectedPiece!=""&&result!=null?"着手候補 "+result.P.Count+"手":"";
            if(shownPly!=s.Ply)
            {
                shownPly=s.Ply;geometryDirty=true;
                visuals.RefreshCaptured(s.Captured.Where(c=>c.Victim.Kind!=PieceKind.King).GroupBy(c=>new{c.Captor,c.Victim.Kind}).Select(g=>new Cfg2CapturedPiece{Owner=g.Key.Captor,DefinitionId=MatchConfig.DefinitionId(g.Key.Kind),Count=g.Count()}));
            }
            resultPanel.gameObject.SetActive(session.IsFinished&&s.Outcome!=MatchOutcome.Interrupted&&!LessonActive);
            if(session.IsFinished)
            {
                string why=s.Outcome==MatchOutcome.KingCapture?"王捕獲":s.Outcome==MatchOutcome.NoMovesDraw?"着手可能手なし":s.Outcome==MatchOutcome.TimeLoss?"時間切れ":"中断";
                resultText.text=(s.Winner.HasValue?(s.Winner==Side.A?"先手 A":"後手 B")+" の勝ち":"引分")+"\n"+why;
            }
            RefreshHint();
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
