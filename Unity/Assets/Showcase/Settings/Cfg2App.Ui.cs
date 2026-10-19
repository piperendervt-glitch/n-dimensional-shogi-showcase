using System;
using System.Linq;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        static readonly Color Gold=new Color(.95f,.79f,.49f),Dark=new Color(.025f,.045f,.065f,.83f),Muted=new Color(.62f,.69f,.76f);
        readonly System.Collections.Generic.List<Cfg2PlaneView> planeMinis=new System.Collections.Generic.List<Cfg2PlaneView>();
        // For a slider that moves k≥3 axes at once the coordinate planes hold no candidate: k-axis subspaces are shown instead.
        readonly System.Collections.Generic.List<Cfg2SubspaceView> subspaceMinis=new System.Collections.Generic.List<Cfg2SubspaceView>();
        Cfg2SubspaceView subspaceView;RectTransform subspaceRect;Text subspaceNote;const int MaxSubspaceMinis=48;
        readonly System.Collections.Generic.List<Cfg2AxisGizmo> gizmos=new System.Collections.Generic.List<Cfg2AxisGizmo>();
        void CreateCanvas()
        {
            var go=new GameObject("CFG2 Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1536,1024);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            root=CfgUi.Rect(go.transform,"Responsive stage",0,0,1536,1024);root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);root.pivot=new Vector2(.5f,.5f);root.anchoredPosition=Vector2.zero;inputGate=root.gameObject.AddComponent<CanvasGroup>();
            new GameObject("CFG2 EventSystem",typeof(EventSystem),typeof(StandaloneInputModule)).GetComponent<StandaloneInputModule>().forceModuleActive=true;
        }
        Button Button(Transform parent,string id,string title,float x,float y,float w,float h,Action action,bool accent=false)
        {
            var b=Cfg2Ui.Button(parent,title,x,y,w,h,()=>{if(ownership.Owner!=InputOwner.None){Record("UiRejectedDuringGesture",id);return;}Record("UI",id);PlayMenuConfirm();try{action();}catch(Exception e){Notice(e.Message);Debug.LogException(e);}},accent);
            b.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleCenter;
            b.GetComponent<Image>().color=accent?CfgUi.Blue:Dark;actions[id]=b;return b;
        }
        // The look Button gives an accent (selected) entry, switched on an existing button.
        static void MarkSelected(Button b,bool selected){b.GetComponent<Image>().color=selected?CfgUi.Blue:Dark;Cfg2Ui.SelectedFrame(b.transform,selected);}
        void CreateMenu()
        {
            menu=CfgUi.Rect(root,"Main menu",0,0,1536,1024);
            Cfg2Ui.Panel(menu,"Title contrast",66,78,657,180,new Color(.01f,.015f,.02f,.43f));
            var title=CfgUi.Label(menu,"N 次 元 将 棋",80,94,740,98,66,Gold);var shadow=title.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.8f);shadow.effectDistance=new Vector2(2,-2);
            CfgUi.Label(menu,"N - D I M E N S I O N A L  S H O G I",88,195,680,40,22,Gold);
            CfgUi.Line(menu,new Vector2(80,245),new Vector2(700,245),Gold,1);
            // The play modes keep ids menu-0..2, オプション menu-3 and 終了 menu-4; 操作レクチャー and 遊び方 sit between them.
            string[] labels={"人間 vs 人間","人間 vs CPU","CPU vs CPU","操作レクチャー","遊び方","オプション","終了"};
            string[] symbols={"PvP","PvC","CvC","入 門","？","設 定","終 了"};
            for(int i=0;i<MenuEntries;i++)
            {
                int entry=i;var b=Button(menu,MenuIds[i],labels[i],86,290+i*80,550,72,()=>MenuEntry(MenuIds[entry]),i==0);
                var t=b.GetComponentInChildren<Text>();t.fontSize=27;t.alignment=TextAnchor.MiddleLeft;t.rectTransform.anchoredPosition=new Vector2(130,-3);t.rectTransform.sizeDelta=new Vector2(405,66);
                CfgUi.Label(b.transform,symbols[i],16,15,105,42,18,Gold).alignment=TextAnchor.MiddleCenter;
                // Pointing at an entry moves the same cursor the arrow keys move.
                var hover=b.gameObject.AddComponent<EventTrigger>();var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};
                enter.callback.AddListener(_=>SetMenuIndex(entry,true));hover.triggers.Add(enter);
            }
            CreateMenuDimensions();
            menuNotice=CfgUi.Label(menu,"",86,860,640,140,17,Gold);
            CfgUi.Label(menu,"デモ版 v"+Application.version+"　F1：遊び方",86,996,640,24,14,Muted);
        }
        static readonly string[] MenuIds={"menu-0","menu-1","menu-2","menu-lesson","menu-help","menu-3","menu-4"};
        void MenuEntry(string id)
        {
            if(id=="menu-0")StartMatch(MatchMode.PvP);else if(id=="menu-1")StartMatch(MatchMode.PvC);else if(id=="menu-2")StartMatch(MatchMode.CvC);
            else if(id=="menu-lesson")StartLesson();else if(id=="menu-help")OpenHelp(0);else if(id=="menu-3")OpenSettings();else Quit();
        }
        // Main menu cursor: ↑↓ (or W/S) move it, Enter/Space presses the entry. The highlighted entry is the one Enter starts.
        int menuIndex;const int MenuEntries=7;
        void SetMenuIndex(int index,bool sound)
        {
            index=(index%MenuEntries+MenuEntries)%MenuEntries;bool moved=index!=menuIndex;menuIndex=index;
            for(int i=0;i<MenuEntries;i++)MarkSelected(actions[MenuIds[i]],i==menuIndex);
            if(moved&&sound)PlayMenuSelect();
        }
        void UpdateMenuKeys(bool typing)
        {
            if(typing||opened||session!=null||!menu.gameObject.activeInHierarchy||ownership.Owner!=InputOwner.None)return;
            if(Input.GetKeyDown(KeyCode.DownArrow)||Input.GetKeyDown(KeyCode.S))SetMenuIndex(menuIndex+1,true);
            else if(Input.GetKeyDown(KeyCode.UpArrow)||Input.GetKeyDown(KeyCode.W))SetMenuIndex(menuIndex-1,true);
            else if(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter)||Input.GetKeyDown(KeyCode.Space))actions[MenuIds[menuIndex]].onClick.Invoke();
        }
        void CreateSettings()
        {
            settings=CfgUi.Rect(root,"Six settings categories",0,0,1536,1024);
            settingsHeading=Cfg2Ui.Panel(settings,"Header",0,0,1536,88,new Color(.02f,.035f,.05f,.69f));
            CfgUi.Label(settingsHeading,"N次元将棋",22,9,370,45,34,Gold);CfgUi.Label(settingsHeading,"N-DIMENSIONAL SHOGI",24,57,360,23,14,Gold);headerDimensions=CfgUi.Label(settingsHeading,"",205,14,160,40,26,Gold);
            settingsTitle=CfgUi.Label(settingsHeading,"設定（オプション）",560,15,570,48,32);
            backButton=Button(settingsHeading,"back","戻る",1423,18,94,43,LeaveSettings);
            navPanel=Cfg2Ui.Panel(settings,"Categories",6,100,196,694,Dark);
            for(int i=0;i<6;i++){int c=i;Button(navPanel,"category-"+i,categories[i],5,5+i*113,186,108,()=>SelectCategory(c),i==category);}
            pieceHost=Cfg2Ui.Panel(settings,"Piece list",212,100,186,888,Dark);BuildPieces();
            var center=centerPanel=Cfg2Ui.Panel(settings,"Movement preview",408,100,730,888,Dark);
            CfgUi.Label(center,"駒の動きプレビュー",14,10,440,36,22);
            // Tab 0 is the live section, tab 1 the N→3D projection of the selected piece's directions, tabs 2.. the fixed planes.
            string[] names={"断面ビュー","投影ビュー","XY平面","XZ平面","XW平面","YZ平面"};
            for(int i=0;i<names.Length;i++){int tab=i;var b=Button(center,"plane-"+i,names[i],12+i*119,51,114,43,()=>SelectPlane(tab),i==0);b.GetComponentInChildren<Text>().fontSize=16;}
            previewRect=CfgUi.Rect(center,"Actual section surface",10,104,526,584);previewImage=previewRect.gameObject.AddComponent<RawImage>();previewImage.color=Color.white;
            AttachPointer(previewRect);
            planeRect=Cfg2Ui.Panel(center,"2D confirmation",10,104,526,584,new Color(.025f,.04f,.07f,.98f),true);planeView=planeRect.gameObject.AddComponent<Cfg2PlaneView>();
            planeView.Initialize(0,1,(q,revision)=>CommitCell(q,revision,preview.Revision,0),ownership,()=>preview.Revision,()=>draft.Revision);planeRect.gameObject.SetActive(false);
            subspaceRect=Cfg2Ui.Panel(center,"Subspace confirmation",10,104,526,584,new Color(.025f,.04f,.07f,.98f),true);subspaceView=subspaceRect.gameObject.AddComponent<Cfg2SubspaceView>();
            subspaceView.Initialize(new[]{0,1,2},ownership,()=>canvas.scaleFactor,true);subspaceRect.gameObject.SetActive(false);
            // The projection and its controller fill the whole height: the trial tools below belong to the section view.
            projectionRect=Cfg2Ui.Panel(center,"Direction projection",10,104,526,774,new Color(.025f,.04f,.07f,.98f),true);projectionView=projectionRect.gameObject.AddComponent<Cfg2ProjectionView>();
            projectionControls=Cfg2Ui.Panel(center,"Projection rotation controller",546,104,174,774,new Color(.02f,.035f,.06f,.98f),true);
            projectionView.Initialize(projectionControls,ownership,()=>canvas.scaleFactor,(name,data)=>Record(name,data));projectionRect.gameObject.SetActive(false);projectionControls.gameObject.SetActive(false);
            CreateGizmo(center,356,109,"settings-",174,130);settingsGizmo=helperPanels[helperPanels.Count-1];
            planeMiniHost=CfgUi.Rect(center,"Selected-cell planes",546,104,174,584);
            counts=CfgUi.Label(center,"",12,696,700,48,16,Muted);
            var tools=trialTools=Cfg2Ui.Panel(center,"Trial controls",10,754,438,124,new Color(.04f,.065f,.09f,.7f));
            CfgUi.Label(tools,"試し配置",10,5,200,27,19);
            Button(tools,"tool-select","選択",8,38,75,35,()=>SetTool("選択"));
            Button(tools,"tool-place","置く",88,38,75,35,()=>SetTool("配置"));
            Button(tools,"tool-erase","消す",168,38,75,35,()=>SetTool("消去"));
            Button(tools,"trial-clear","クリア",248,38,82,35,()=>{preview.Clear();AfterTrial();});
            Button(tools,"trial-reset","中央に置く",336,38,94,35,()=>{preview.Place(new TrialPiece("subject",selectedDefinition,Side.A,default));selectedPiece="subject";AfterTrial();});
            Button(tools,"side","陣営 A / B",8,81,119,34,()=>{placementSide=placementSide==Side.A?Side.B:Side.A;Notice("置く駒："+(placementSide==Side.A?"先手 A":"後手 B"));});
            Button(tools,"trial-undo","Undo",135,81,86,34,()=>{preview.Undo();AfterTrial();});Button(tools,"trial-redo","Redo",229,81,86,34,()=>{preview.Redo();AfterTrial();});
            var toggles=displayToggles=Cfg2Ui.Panel(center,"Display options",458,754,262,124,new Color(.04f,.065f,.09f,.7f));
            Toggle(toggles,"移動候補",showCandidates,8,4,244,v=>showCandidates=v);Toggle(toggles,"攻撃範囲",showAttacks,8,42,244,v=>showAttacks=v);Toggle(toggles,"座標グリッド",showGrid,8,80,244,v=>showGrid=v);
            detailHost=Cfg2Ui.Panel(settings,"Independent details",1148,100,380,888,Dark);BuildDetails();
            headerStatus=CfgUi.Label(settings,"",16,807,181,130,16,Muted);
            statusText=CfgUi.Label(settings,"",410,995,1100,25,15,Gold);UpdateHeader();
            EnsurePlaneMinis(CurrentDimensions);LayoutSettings();
        }
        // The stage is 1536 wide and centred; a wider window leaves spare room on both sides, which the settings screen
        // spends on the preview: the side columns slide outwards and the centre panel grows by twice the spare width.
        float settingsSpare=-1;
        float SettingsSpare(){float canvasWidth=canvas!=null?((RectTransform)canvas.transform).rect.width:1536;return Mathf.Clamp((canvasWidth-1536)*.5f-6,0,260);}
        static void Place(RectTransform r,float x,float y,float w,float h){r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
        void LayoutSettings()
        {
            if(settings==null)return;float spare=SettingsSpare();if(Mathf.Approximately(spare,settingsSpare))return;
            // A page of its own is rebuilt for the new width, which would drop a field being typed in: the layout waits for the typing to end.
            if(settingsSpare>=0&&category!=2&&CfgUi.EditingText)return;
            settingsSpare=spare;
            Place(settingsHeading,-spare,0,1536+2*spare,88);settingsTitle.rectTransform.anchoredPosition=new Vector2(560+spare,-15);((RectTransform)backButton.transform).anchoredPosition=new Vector2(1423+2*spare,-18);
            Place(navPanel,6-spare,100,196,694);Place(pieceHost,212-spare,100,186,888);LayoutCategory();
            headerStatus.rectTransform.anchoredPosition=new Vector2(16-spare,-807);Place(statusText.rectTransform,410-spare,995,1100+2*spare,25);
            float width=730+2*spare;Place(centerPanel,408-spare,100,width,888);
            // Projection tab: the view takes what the controller (230 px, up to 330 when there is room) leaves.
            float controls=Mathf.Clamp(174+spare*.5f,230,330);
            Place(projectionRect,10,104,width-30-controls,774);Place(projectionControls,width-10-controls,104,controls,774);projectionView.Layout();
            // Section tabs: the plane diagrams fill the room right of the section in as many 174 px columns as fit.
            Place(planeMiniHost,546,104,width-556,584);LayoutPlaneMinis();
            // A page of its own spreads its columns over the new width, at the scroll position it had.
            if(category!=2){RememberScroll();BuildDetails();}
        }
        // 駒の動き設定 works with the piece list and the movement preview beside it; every other category is a page of its own
        // over the whole width right of the categories.
        void LayoutCategory()
        {
            if(settings==null)return;float spare=Mathf.Max(0,settingsSpare);bool pieces=category==2;
            pieceHost.gameObject.SetActive(pieces);centerPanel.gameObject.SetActive(pieces);
            if(pieces)Place(detailHost,1148+spare,100,380,888);else Place(detailHost,212-spare,100,1316+2*spare,888);
        }
        // The columns of a page, side by side from the left; each is as wide as the 330 px controls of the piece details.
        // The content grows only if a column outruns the page, so the scroll bar stays a fallback.
        RectTransform[] Columns(RectTransform c,int count)
        {
            float stride=Mathf.Min(c.sizeDelta.x/count,460);var columns=new RectTransform[count];
            for(int i=0;i<count;i++)
            {
                columns[i]=CfgUi.Rect(c,"Column "+i,i*stride,0,346,c.sizeDelta.y);
                if(i>0){var line=CfgUi.Rect(c,"Column divider",i*stride-(stride-346)/2,8,1,c.sizeDelta.y-16);var img=line.gameObject.AddComponent<Image>();img.color=new Color(.67f,.59f,.42f,.25f);img.raycastTarget=false;}
            }
            return columns;
        }
        static void FitColumns(RectTransform c,RectTransform[] columns)
        {
            float bottom=c.sizeDelta.y;
            foreach(var column in columns)foreach(RectTransform child in column)bottom=Mathf.Max(bottom,-child.anchoredPosition.y+child.sizeDelta.y+8);
            c.sizeDelta=new Vector2(c.sizeDelta.x,bottom);
        }
        // One diagram per coordinate plane of the current N (6 in 4D, 10 in 5D, 55 in 11D), in a scrolling grid. When the selected
        // piece slides on k≥3 axes at once, one diagram per k-axis subspace instead (10 in 5D for k=3), the first 48 of them at most.
        int planeMiniDimensions,planeMiniAxes;ScrollRect planeMiniScroll;
        void EnsurePlaneMinis(int n,int k=0)
        {
            if(k<3)k=0;
            if(n==planeMiniDimensions&&k==planeMiniAxes&&planeMiniScroll!=null)return;planeMiniDimensions=n;planeMiniAxes=k;
            CfgUi.Clear(planeMiniHost);planeMinis.Clear();subspaceMinis.Clear();subspaceNote=null;
            planeMiniScroll=CfgUi.Scroll(planeMiniHost,"Plane diagrams",0,0,planeMiniHost.rect.width,planeMiniHost.rect.height,out var content,584);
            // 165 subspaces in 11D are a long column: one wheel notch moves about one row.
            planeMiniScroll.scrollSensitivity=k>=3?150:60;
            if(k>=3)
            {
                int total=Cfg2SubspaceView.SubspaceCount(n,k);
                foreach(var axes in Cfg2SubspaceView.Subspaces(n,k).Take(MaxSubspaceMinis))
                {
                    var r=Cfg2Ui.Panel(content,"Selected-cell subspace",0,0,174,174,new Color(.025f,.04f,.065f,.8f),true);
                    var diagram=r.gameObject.AddComponent<Cfg2SubspaceView>();diagram.Initialize(axes,ownership,()=>canvas.scaleFactor,false);diagram.Dimensions=n;subspaceMinis.Add(diagram);
                    var chosen=axes;
                    var open=r.gameObject.AddComponent<Button>();CfgUi.NoNavigation(open);open.targetGraphic=r.GetComponent<Image>();open.onClick.AddListener(()=>{if(ownership.Owner!=InputOwner.None)return;Record("UI","subspace-"+diagram.Name);PlayMenuConfirm();ShowSubspace(chosen);});
                }
                subspaceNote=CfgUi.Label(content,k+"軸を同時に動かす駒なので、2軸の平面ではなく "+k+"軸の部分空間に候補を描いています（全"+total+"個"+(total>MaxSubspaceMinis?"のうち先頭"+MaxSubspaceMinis+"個を表示":"")+"）。図を押すと中央に大きく表示し、「断面ビュー」で元の表示に戻ります。",0,0,100,40,11,Muted);
                LayoutPlaneMinis();return;
            }
            foreach(var (h,v) in Cfg2PlaneView.Planes(n))
            {
                var r=Cfg2Ui.Panel(content,"Selected-cell plane",0,0,174,140,new Color(.025f,.04f,.065f,.8f),true);
                var diagram=r.gameObject.AddComponent<Cfg2PlaneView>();diagram.Initialize(h,v,null,ownership,()=>preview.Revision,()=>draft.Revision);diagram.Dimensions=n;planeMinis.Add(diagram);
                // A click on a diagram opens that plane large in the centre.
                var open=r.gameObject.AddComponent<Button>();CfgUi.NoNavigation(open);open.targetGraphic=r.GetComponent<Image>();open.onClick.AddListener(()=>{if(ownership.Owner!=InputOwner.None)return;Record("UI","plane-"+diagram.Name);PlayMenuConfirm();ShowPlane(h,v);});
            }
            LayoutPlaneMinis();
        }
        void LayoutPlaneMinis()
        {
            if(planeMiniScroll==null)return;float w=planeMiniHost.rect.width,h=planeMiniHost.rect.height;
            Place((RectTransform)planeMiniScroll.transform,0,0,w,h);Place(planeMiniScroll.viewport,0,0,w,h);
            int columns=Mathf.Max(1,Mathf.FloorToInt((w-14+6)/180f));
            if(subspaceMinis.Count>0)
            {
                int rows=(subspaceMinis.Count+columns-1)/columns;
                // The note wraps at about 15 characters per 180 px of width; keep room for every line plus a margin.
                float noteWidth=w-22;float noteHeight=subspaceNote==null?0:Mathf.Ceil(subspaceNote.text.Length/Mathf.Max(1,Mathf.Floor(noteWidth/12f)))*16+24;
                Place(planeMiniScroll.content,0,0,w-14,Mathf.Max(h,rows*180-6+noteHeight));
                for(int i=0;i<subspaceMinis.Count;i++)Place((RectTransform)subspaceMinis[i].transform,(i%columns)*180,(i/columns)*180,174,174);
                if(subspaceNote!=null)Place(subspaceNote.rectTransform,4,rows*180,noteWidth,noteHeight);
                return;
            }
            int planeRows=(planeMinis.Count+columns-1)/columns;
            Place(planeMiniScroll.content,0,0,w-14,Mathf.Max(h,planeRows*146-6));
            for(int i=0;i<planeMinis.Count;i++)Place((RectTransform)planeMinis[i].transform,(i%columns)*180,(i/columns)*146,174,140);
        }
        // Shows one k-axis subspace large in the centre (tab 7: no fixed tab lights up); a drag turns its 3-D camera.
        void ShowSubspace(int[] axes)
        {
            planeTab=7;
            previewRect.gameObject.SetActive(false);projectionRect.gameObject.SetActive(false);planeRect.gameObject.SetActive(false);subspaceRect.gameObject.SetActive(true);
            subspaceView.Initialize(axes,ownership,()=>canvas.scaleFactor,true);subspaceView.Dimensions=CurrentDimensions;
            for(int i=0;i<6;i++)MarkSelected(actions["plane-"+i],false);
            ShowSectionAids(true);geometryDirty=true;Record("Subspace",subspaceView.Name);
        }
        // Shows one coordinate plane in the centre. The four fixed tabs are XY XZ XW YZ; any other plane lights no tab.
        static readonly (int h,int v)[] TabPlanes={(0,1),(0,2),(0,3),(1,2)};
        void ShowPlane(int h,int v)
        {
            int tab=Array.IndexOf(TabPlanes,(h,v));planeTab=tab>=0?tab+2:6;
            previewRect.gameObject.SetActive(false);projectionRect.gameObject.SetActive(false);planeRect.gameObject.SetActive(true);subspaceRect.gameObject.SetActive(false);
            planeView.Initialize(h,v,(q,revision)=>CommitCell(q,revision,preview.Revision,0),ownership,()=>preview.Revision,()=>draft.Revision);
            for(int i=0;i<6;i++)MarkSelected(actions["plane-"+i],i==planeTab);
            ShowSectionAids(true);geometryDirty=true;Record("Plane",planeTab.ToString()+";"+planeView.Name);
        }
        // The large plane (tabs 2..6) uses a UI axis that an N-board does not have.
        bool PlaneLacksAxis(int n)=>planeTab>=2&&planeTab<7&&Math.Max(planeView.HorizontalAxis,planeView.VerticalAxis)>=n;
        // Trial tools, counts, gizmo and the plane diagrams belong to the section tabs; the projection tab hides them.
        void ShowSectionAids(bool on)
        {
            trialTools.gameObject.SetActive(on);displayToggles.gameObject.SetActive(on);counts.gameObject.SetActive(on);planeMiniHost.gameObject.SetActive(on);
            // The gizmo would sit on the large subspace figure's own axes, so that view hides it too.
            settingsGizmo.gameObject.SetActive(on&&showHelpers&&planeTab!=7);projectionControls.gameObject.SetActive(!on);
        }
        void Toggle(Transform parent,string text,bool value,float x,float y,float w,Action<bool> action)
        {CfgUi.Toggle(parent,text,value,x,y,w,v=>{action(v);geometryDirty=true;});}
        void CreateGizmo(Transform parent,float x,float y,string prefix,float width=174,float height=150)
        {
            var r=Cfg2Ui.Panel(parent,"Camera-linked XYZW gizmo",x,y,width,height,new Color(.025f,.04f,.065f,.18f));helperPanels.Add(r);
            var gizmo=r.gameObject.AddComponent<Cfg2AxisGizmo>();gizmo.Initialize(axis=>{Record("UI",prefix+"axis-"+axis);RequestView(axis.ToString());},()=>slice,ownership,()=>{geometryDirty=true;stableDirty=true;Record("AxisTranslation",slice.Center.ToString());},()=>canvas.scaleFactor);
            foreach(var entry in gizmo.Buttons)actions[prefix+"axis-"+entry.Key]=entry.Value;gizmos.Add(gizmo);
        }
        void CreateMini(Transform parent,float x,float y,float w,float h,ReferenceSection view,Color? border)
        {
            var r=Cfg2Ui.Panel(parent,"Hyperplane "+view.Name,x,y,w,h,new Color(.03f,.05f,.10f,.38f),true);
            helperPanels.Add(r);
            var mini=r.gameObject.AddComponent<Cfg2MiniView>();mini.Initialize(()=>slice,ownership,view,()=>{geometryDirty=true;stableDirty=true;},v=>{slice.Request(v);geometryDirty=true;},()=>canvas.scaleFactor);mini.PinRequested=()=>PinMagnifier(mini);minis.Add(mini);
            PanelFrame(r,border);
        }
        // A null border is the plain glass of the 4D panels; a swap panel in higher N carries the colour of the axis it brings in.
        // The panel view keeps this frame when it fills itself (Cfg2MiniView.Initialize), so it is coloured here, after it.
        static void PanelFrame(Transform panel,Color? border)
        {
            var glass=Cfg2Ui.Frame(panel);if(glass==null)return;
            glass.color=border.HasValue?new Color(border.Value.r,border.Value.g,border.Value.b,.7f):new Color(.80f,.87f,1,.55f);glass.Glow=true;glass.SetVerticesDirty();
        }
        void RefreshAuxiliaryViews()
        {
            if(slice==null||rig?.Camera==null)return;var frame=slice.Frame;var rotation=rig.Camera.transform.rotation;int n=frame.Dimensions;
            foreach(var gizmo in gizmos)gizmo.Refresh(frame,rotation,slice.ViewRevision,cameraRevision);
            // Panels are drawn around the start pivot P, so a move of C along any axis shows up as a shifted plane.
            // They belong to the match screen: hidden, they are neither rebuilt nor redrawn.
            if(matchHud!=null&&matchHud.gameObject.activeInHierarchy)
            {
                EnsureMinis();panelPieces=PanelPieces();panelPiecesRevision++;var dots=PanelDotsEnabled?panelPieces:null;
                foreach(var mini in minis)mini.Refresh(frame,rotation,cameraRevision,previewRenderer.HalfWindow,slice.DisplayState.Embedding,dots,panelPiecesRevision);
                MarkBoardSection();
            }
            if(!opened)return;
            var ps=preview.Pieces.Select(p=>new CfgVisualPiece{Id=p.Id,DefinitionId=p.DefinitionId,Position=p.Position,Side=p.Side}).ToArray();
            var selected=preview.Find(selectedPiece)?.Position??planeReferenceCell;
            // The XW tab needs a fourth axis: in 3D it is gone. The diagrams are rebuilt for the planes of the current N.
            planeView.Dimensions=n;if(actions.TryGetValue("plane-4",out var xw))xw.gameObject.SetActive(n>3);
            // A large plane on an axis the board no longer has (XV after 5D→4D, YW after 4D→3D) goes back to the section view.
            if(PlaneLacksAxis(n))SelectPlane(0);
            int slideAxes=preset?.Find(selectedDefinition)?.SlideAxes??0;EnsurePlaneMinis(n,slideAxes);
            // The large subspace figure belongs to one k: another piece or axis count closes it back to the section view.
            if(planeTab==7&&(slideAxes<3||subspaceView.AxisCount!=slideAxes||subspaceView.Dimensions!=n))SelectPlane(0);
            if(planeMiniHost.gameObject.activeInHierarchy)
            {
                foreach(var diagram in planeMinis)diagram.Refresh(ps,result?.G,result?.P,attackCells,selected,slice.ViewRevision,showCandidates,showAttacks,showGrid);
                foreach(var diagram in subspaceMinis)diagram.Refresh(ps,result?.G,result?.P,attackCells,selected,slice.ViewRevision,showCandidates,showAttacks,showGrid);
            }
            if(planeTab>=2&&planeTab<7)planeView.Refresh(ps,result?.G,result?.P,attackCells,selected,slice.ViewRevision,showCandidates,showAttacks,showGrid);
            if(planeTab==7){subspaceView.Dimensions=n;subspaceView.Refresh(ps,result?.G,result?.P,attackCells,selected,slice.ViewRevision,showCandidates,showAttacks,showGrid);}
            if(planeTab==1)projectionView.Refresh(preset,selectedDefinition,ShortNames.TryGetValue(selectedDefinition,out var shortName)?shortName:selectedDefinition);
        }
        void BuildPieces()
        {
            CfgUi.Clear(pieceHost);CfgUi.Label(pieceHost,"駒の一覧",12,10,162,34,22);
            var ds=draft.Current.Definitions.Where(d=>!d.Id.StartsWith("promoted")).ToArray();
            for(int i=0;i<Math.Min(8,ds.Length);i++)
            {
                var d=ds[i];var b=Button(pieceHost,"piece-"+d.Id,ShortNames.TryGetValue(d.Id,out var shortName)?shortName:d.Name,9,52+i*65,168,59,()=>SelectDefinition(d.Id),d.Id==selectedDefinition);
                var label=b.GetComponentInChildren<Text>();label.fontSize=17;label.alignment=TextAnchor.MiddleLeft;label.rectTransform.anchoredPosition=new Vector2(53,-3);label.rectTransform.sizeDelta=new Vector2(108,53);
                Cfg2Ui.PieceIcon(b.transform,d.Id,Side.A,7,6,37,46);
            }
            CfgUi.Label(pieceHost,"成りなし",12,607,162,30,18,Muted);
            CfgUi.Label(pieceHost,"と金・成銀・成桂\n成香・龍王・龍馬\n\nデモ版では成りません",12,648,162,160,16,Muted);
        }
        // Looked up by definition id so a reordered definition list cannot mislabel a button.
        static readonly System.Collections.Generic.Dictionary<string,string> ShortNames=new System.Collections.Generic.Dictionary<string,string>
        {{"king-step","王（玉将）"},{"rook-h11","飛車"},{"bishop-h22","角行"},{"gold-legacy","金将"},{"silver-legacy","銀将"},{"knight-legacy","桂馬"},{"lance-legacy","香車"},{"pawn-step","歩兵"}};
        void SelectCategory(int index)
        {RememberScroll();category=index;for(int i=0;i<6;i++)MarkSelected(actions["category-"+i],i==index);LayoutCategory();BuildDetails();Record("Category",index.ToString());}
        void SelectDefinition(string id)
        {
            RememberScroll();selectedDefinition=id;
            if(preview.Find(selectedPiece)!=null)preview.ReplaceDefinition(selectedPiece,id);
            else{preview.Place(new TrialPiece("subject",id,Side.A,selectedCell));selectedPiece="subject";}
            BuildPieces();BuildDetails();AfterTrial();Record("Definition",id);
        }
        void SelectPlane(int tab)
        {
            if(tab==4&&CurrentDimensions<4)tab=0;
            if(tab>=2){var plane=TabPlanes[tab-2];ShowPlane(plane.h,plane.v);return;}
            planeTab=tab;previewRect.gameObject.SetActive(tab==0);projectionRect.gameObject.SetActive(tab==1);planeRect.gameObject.SetActive(false);subspaceRect.gameObject.SetActive(false);
            for(int i=0;i<6;i++)MarkSelected(actions["plane-"+i],i==tab);
            ShowSectionAids(tab!=1);geometryDirty=true;Record("Plane",tab.ToString());
        }
        void RememberScroll(){if(detailScroll!=null)detailPositions[detailContext]=detailScroll.verticalNormalizedPosition;}
        void BuildDetails()
        {
            if(detailHost==null)return;RememberScroll();CfgUi.Clear(detailHost);inputs.Clear();
            detailContext=category+"/"+selectedDefinition+"/"+detailTab;
            CfgUi.Label(detailHost,category==2?"駒の動きを設定":categories[category],14,12,350,35,22);
            if(category==2)
            {
                string[] tabs={"基本設定","詳細設定","表示設定"};
                for(int i=0;i<3;i++){int t=i;Button(detailHost,"detail-tab-"+i,tabs[i],12+i*119,59,115,42,()=>{RememberScroll();detailTab=t;BuildDetails();},i==detailTab);}
                detailScroll=CfgUi.Scroll(detailHost,"Details only scroll",10,114,360,760,out var pc,850);
                BuildPieceDetails(pc);
                detailScroll.verticalNormalizedPosition=detailPositions.TryGetValue(detailContext,out var position)?position:1;
                return;
            }
            // Every other category is one page in columns that fits without scrolling from 1536×1024 up.
            detailScroll=CfgUi.Scroll(detailHost,"Details only scroll",10,64,detailHost.sizeDelta.x-20,810,out var c,810);
            RectTransform[] cols;
            if(category==0)
            {
                int n=DraftDimensions;cols=Columns(c,2);
                CfgUi.Label(cols[0],n+"次元将棋（デモ版ルール）",8,7,330,44,24,Gold);
                CfgUi.Label(cols[0],"相手の王を取れば勝ち。\n手番の側に動かせる手が\nなければ引き分け。\n持ち時間が0になると負け。\n\n成り・持ち駒（打ち）・千日手は\nありません。\n王手を放置しても反則になりません。",8,73,330,375,20);
                CfgUi.Label(cols[0],"デモ版の暫定ルールです。\n今後変わることがあります。",8,473,330,89,17,Muted);
                Field(cols[1],"name","表示名",draft.Name,7,v=>{draft.Edit(d=>d.Name=v);AfterEdit();});
                if(startupLoadError!="")Button(cols[1],"recover-default","元保存を保持して新しい設定で復旧",8,142,330,58,RecoverToNewBundle);
            }
            else if(category==1)
            {
                int n=DraftDimensions;var axes=SliceAxes.Axes(n);cols=Columns(c,2);var left=cols[0];
                CfgUi.Label(left,"次元数 N（"+BoundsN.MinDimensions+"〜"+BoundsN.MaxDimensions+"）",8,7,330,32,19);
                int choices=BoundsN.MaxDimensions-BoundsN.MinDimensions+1;float choiceWidth=(330-3f*(choices-1))/choices;
                for(int i=0;i<choices;i++){int k=BoundsN.MinDimensions+i;var b=Button(left,"dimensions-"+k,k.ToString(CultureInfo.InvariantCulture),8+i*(choiceWidth+3),45,choiceWidth,40,()=>SetDimensions(k),k==n);b.GetComponentInChildren<Text>().fontSize=16;}
                // The one line the settings screen says about a large N; nothing else warns or asks.
                if(n>=8)CfgUi.Label(left,"N が大きいと表示が混み合い、動作も重くなります。",8,92,330,30,15,Muted);
                // Initial layout: the flat 40 pieces, or that plane copied to all 9 cells of one more axis (any axis but X and Z).
                int? copy=DraftCopyAxis;float y=n>=8?130:100;
                CfgUi.Label(left,"初期配置",8,y,330,32,19);y+=38;
                LayoutRow(left,DraftData.FlatLayout,"平面（従来）",40,y,!copy.HasValue);y+=46;
                for(int core=2;core<n;core++){LayoutRow(left,DraftData.CopyLayout(core),SliceAxes.AxisName(SliceAxes.AxisOfCore(core))+"方向にコピー",360,y,copy==core);y+=46;}
                // The right column says what the chosen N and layout make of the board.
                var right=cols[1];
                string copyName=copy.HasValue?SliceAxes.AxisName(SliceAxes.AxisOfCore(copy.Value)):"";
                CfgUi.Label(right,copy.HasValue?"初期配置：360駒（40駒 × 9層）":"初期配置：40駒",8,7,420,44,23,Gold);
                string hidden=string.Join("、",Enumerable.Range(3,n-3).Select(core=>SliceAxes.AxisName(SliceAxes.AxisOfCore(core))+"=0"));
                string layoutText=!copy.HasValue?"初期配置は両側20駒。\n表示の駒配置面はY=h"+(hidden==""?"":"、"+hidden)+"。\n開始中心Pは駒配置面の中央。"
                    :copyName+" = −4〜4 の 9 層すべてに\n従来の 40 駒をコピー。\n王は各陣 9 枚。\nどれか 1 枚を取られると負け。\n\n"+(copy==2?"表示では Y=−4 の層が盤に載り、\n9 層が上に積み重なります。":"表示の駒配置面はY=h"+(hidden==""?"":"、"+hidden)+"。\n"+copyName+" を動かすと別の層が現れます。")+"\n開始中心Pは中央層 "+copyName+"=0。";
                CfgUi.Label(right,"論理盤：各軸 −4〜4\n"+string.Join(" / ",axes.Select(SliceAxes.AxisName))+"\n\n先手 A：+Z方向\n後手 B：−Z方向\n\n"+layoutText+"\n先手Aから開始します。\n\n試し配置は対局に入りません。",8,72,330,copy.HasValue?560:450,20);
            }
            else if(category==3)
            {
                cols=Columns(c,2);
                CfgUi.Label(cols[0],"飛車・角などが斜めに走るときの通り道",8,8,330,35,19);
                Button(cols[0],"occlusion-lattice","通り道のマスだけで判定（標準）",8,58,330,44,()=>{draft.Edit(d=>d.Occlusion="lattice-line/1");AfterEdit();BuildDetails();},draft.Current.Occlusion=="lattice-line/1");
                Button(cols[0],"occlusion-supercover","かすめるマスも塞ぐ（厳しめ）",8,111,330,44,()=>{draft.Edit(d=>d.Occlusion="supercover-closed/1");AfterEdit();BuildDetails();},draft.Current.Occlusion=="supercover-closed/1");
                Field(cols[0],"time","双方の持ち時間（秒）",options.TimeSeconds,211,v=>{options.TimeSeconds=v;AfterEdit();});
                CfgUi.Label(cols[1],"秒読み・時間の加算はありません。\n\n終局：王を取る／手番の側に\n動ける手がない／時間切れ\n成り・打ちはありません。\n王手を放置しても指せます。",8,8,330,238,20);
            }
            else if(category==4)
            {
                int n=DraftDimensions;cols=Columns(c,2);var left=cols[0];var right=cols[1];
                CfgUi.Label(left,"CPU（"+n+"次元）",8,8,330,42,25,Gold);
                CfgUi.Label(left,"取れる駒があれば取り、なければ\n決まった規則で手を選ぶ簡単なCPUです。\n強さの段階はありません。",8,69,330,165,20);
                CfgUi.Label(left,"以下は通常、変更不要です。",8,226,330,30,16,Muted);
                Field(left,"seed","乱数の種（同じ値なら同じ手順）",options.CpuSeed,264,v=>{options.CpuSeed=v;AfterEdit();});
                Field(left,"budget","CPUが調べる手の上限",options.CpuBudget,389,v=>{options.CpuBudget=v;AfterEdit();});
                CfgUi.Label(right,"CPU戦の演出",8,8,330,34,21,Gold);
                CfgUi.Toggle(right,"CPUの手を、盤を回して見せる",CpuShowEnabled,8,48,330,v=>{CpuShowEnabled=v;Record("Pref",CpuShowKey+"="+v);});
                CfgUi.Toggle(right,"手番交代ごとに初期表示へ戻す",TurnResetEnabled,8,90,330,v=>{TurnResetEnabled=v;Record("Pref",TurnResetKey+"="+v);});
                int delay=int.TryParse(options.CpuDelayMilliseconds,out var parsed)?parsed:500;
                var delayLabel=CfgUi.Label(right,"",8,138,330,30,18);void ShowDelay(float ms)=>delayLabel.text="画面切り替えの待ち時間："+(ms/1000f).ToString("0.0")+" 秒";ShowDelay(delay);
                Cfg2Ui.Slider(right,8,176,330,0,5000,Mathf.Clamp(delay,0,5000),v=>{int ms=Mathf.RoundToInt(v/100f)*100;ShowDelay(ms);options.CpuDelayMilliseconds=ms.ToString(System.Globalization.CultureInfo.InvariantCulture);AfterEdit();});
                CfgUi.Label(right,"演出中と待ち時間の間は、CPUの\n持ち時間を消費しません。",8,222,330,52,16,Muted);
            }
            // 表示 and サウンド, 画質, then the controls help: three columns.
            else
            {
                cols=Columns(c,3);
                CfgUi.Label(cols[0],"表示",8,8,330,34,21,Gold);BuildDisplay(cols[0],50,cols[2]);BuildSound(cols[0],511);
                BuildGraphics(cols[1],8);
            }
            FitColumns(c,cols);
            detailScroll.verticalNormalizedPosition=detailPositions.TryGetValue(detailContext,out var value)?value:1;
        }
        void Field(RectTransform c,string id,string label,string value,float y,Action<string> edited)
        {
            CfgUi.Label(c,label,8,y,330,32,18);var error=CfgUi.Label(c,"",8,y+80,330,36,15,new Color(1,.61f,.47f));
            var input=CfgUi.Input(c,value,8,y+37,330,40,v=>{edited(v);error.text=config==null?"値を確認してください":"";},true);inputs[id]=input;
            if(config==null)error.text="値を確認してください";
        }
        void BuildPieceDetails(RectTransform c)
        {
            var d=draft.Current.Definitions.Single(p=>p.Id==selectedDefinition);bool editable=IsEditable(d);int n=DraftDimensions;
            CfgUi.Label(c,d.Name,8,0,330,43,25,Gold);
            if(detailTab==2){BuildDisplay(c,67);return;}
            if(!editable){CfgUi.Label(c,"この駒の動きは固定です。\n普段の将棋の動きを"+n+"次元に広げて、\n先手・後手の両方に使います。\n\n成り・打ちはありません。",8,71,330,290,20);return;}
            if(detailTab==0)
            {
                float sliderBottom=431;
                if(d.Kind=="step/1")
                {
                    CfgUi.Label(c,"1手で動ける最大距離：1",8,65,330,37,20);
                    Field(c,"max-axes","同時に動かす最大軸数",d.MaxAxes,130,v=>Edit(x=>x.MaxAxes=v,false));
                    CfgUi.Label(c,"移動パターン",8,259,330,32,19);
                    Button(c,"pattern-all","全方向（全近傍）",8,302,330,42,()=>Edit(x=>x.MaxAxes=n.ToString(CultureInfo.InvariantCulture)),d.MaxAxes==n.ToString(CultureInfo.InvariantCulture));
                    Button(c,"pattern-axis","軸方向のみ",8,353,330,42,()=>Edit(x=>x.MaxAxes="1"),d.MaxAxes=="1");
                }
                else BuildSliderDetails(c,d,n,out sliderBottom);
                float ay=sliderBottom;
                CfgUi.Label(c,"使用する軸",8,ay,330,32,19);int.TryParse(d.AxesMask,out var mask);
                // Four toggles per row: X Y Z W, then V U T S, then R Q P for the largest N.
                var axes=SliceAxes.Axes(n);int rows=(axes.Length+3)/4;
                for(int i=0;i<axes.Length;i++){var axis=axes[i];int bit=1<<SliceAxes.CoreAxis(axis);Toggle(c,SliceAxes.AxisName(axis),(mask&bit)!=0,8+i%4*83,ay+45+i/4*40,82,v=>Edit(x=>{int.TryParse(x.AxesMask,out int m);x.AxesMask=(v?m|bit:m&~bit).ToString();}));}
                float ly=ay+77+rows*40;
                // A slider moves exactly k of its axes at once: with fewer axes in use it has no move at all, so say so here.
                int used=0;for(int core=0;core<n;core++)if((mask&1<<core)!=0)used++;int k=SlideAxesOf(d);
                if(k>used){CfgUi.Label(c,"使う軸が"+used+"本しかないため、"+k+"軸を同時に動かすこの駒は動けません。",8,ly-26,330,44,15,new Color(1,.55f,.45f));ly+=26;}
                var legend=CfgUi.Label(c,"青：移動できるマス　赤：攻撃範囲\n金：選択中の駒\n\n候補は"+n+"次元全体で数え、\n今の断面にあるものだけを描きます。",8,ly,330,146,18,Muted);
                float bottom=legend.rectTransform.anchoredPosition.y*-1+146+20;if(c.sizeDelta.y<bottom)c.sizeDelta=new Vector2(c.sizeDelta.x,bottom);
            }
            else
            {
                Toggle(c,"前（陣営のZ方向）",d.Forward,8,77,330,v=>Edit(x=>x.Forward=v));
                Toggle(c,"横（Zの変位なし）",d.Sideways,8,127,330,v=>Edit(x=>x.Sideways=v));
                Toggle(c,"後",d.Backward,8,177,330,v=>Edit(x=>x.Backward=v));
                Toggle(c,"空セルへ移動",d.AllowEmpty,8,258,330,v=>Edit(x=>x.AllowEmpty=v));
                Toggle(c,"敵駒を捕獲",d.AllowCapture,8,308,330,v=>Edit(x=>x.AllowCapture=v));
                CfgUi.Label(c,"味方の駒があるマスには動けません。\n\n変更は試し盤と次の対局に反映されます。",8,389,330,217,18,Muted);
            }
        }
        // The coordinate field is written in UI order X,Y,Z,W,V...; QN is in core order.
        static QN FromUiAxes(int[] ui,int dimensions){var axes=SliceAxes.Axes(dimensions);var core=new int[dimensions];for(int i=0;i<dimensions;i++)core[SliceAxes.CoreAxis(axes[i])]=ui[i];return new QN(core);}
        static bool IsEditable(RawPieceDefinition d)=>d.Kind=="step/1"||IsSliderKind(d.Kind);
        static bool IsSliderKind(string kind)=>kind=="h11/1"||kind=="h22/1"||kind=="hkk/1";
        // Axes an editable slider moves at once. h11/h22 keep their own kind ids so existing 4D data and hashes are untouched; k>=3 is "hkk/1" with k in MaxAxes.
        static int SlideAxesOf(RawPieceDefinition d)=>d.Kind=="h11/1"?1:d.Kind=="h22/1"?2:d.Kind=="hkk/1"&&int.TryParse(d.MaxAxes,NumberStyles.Integer,CultureInfo.InvariantCulture,out int k)?k:0;
        // h11/h22 carry MaxAxes=N like every slider of an N-board (SetDimensions keeps it so), so going back to 1 or 2 axes restores
        // the same data and rule hash as before the axis count was raised.
        static void SetSlideAxes(RawPieceDefinition d,int k,int n)
        {
            if(k==1)d.Kind="h11/1";else if(k==2)d.Kind="h22/1";else d.Kind="hkk/1";
            d.MaxAxes=(k<=2?n:k).ToString(CultureInfo.InvariantCulture);
        }
        // The two axis counts whose direction counts C(N,k)·2^k tie at the maximum (N=2,5,8,11: (N+1)/3 pairs with (2N+2)/3), or null.
        static (int a,int b)? EqualSlidePair(int n)
        {
            int best=0;var ks=new System.Collections.Generic.List<int>();
            for(int k=1;k<=n;k++){int v=PieceDefinition.SlideDirections(n,k);if(v>best){best=v;ks.Clear();}if(v==best)ks.Add(k);}
            return ks.Count==2?(ks[0],ks[1]):((int,int)?)null;
        }
        static string Compact(int value)=>value>=10000?(value/10000.0).ToString("0.#",CultureInfo.InvariantCulture)+"万":value.ToString("N0",CultureInfo.InvariantCulture);
        // 基本設定 of 飛車/角行: the "同時に動かす軸の数" row, its direction count, the tie note and pair button, then 最大距離.
        void BuildSliderDetails(RectTransform c,RawPieceDefinition d,int n,out float bottom)
        {
            int k=SlideAxesOf(d);var pair=EqualSlidePair(n);
            CfgUi.Label(c,"同時に動かす軸の数",8,65,330,32,19);
            float gap=n<=5?6:3,bw=(330-gap*(n-1))/n;
            for(int i=1;i<=n;i++)
            {
                int kk=i;float bx=8+(i-1)*(bw+gap);
                var b=Button(c,"slide-axes-"+i,i.ToString(CultureInfo.InvariantCulture),bx,105,bw,58,()=>SetSlideAxesSelected(kk),kk==k);
                var t=b.GetComponentInChildren<Text>();t.fontSize=n<=5?22:16;t.rectTransform.anchoredPosition=new Vector2(0,7);
                var count=CfgUi.Label(b.transform,Compact(PieceDefinition.SlideDirections(n,kk)),0,32,bw,22,n<=5?13:n<=8?10:8,kk==k?new Color(.81f,.88f,1):Muted);count.alignment=TextAnchor.MiddleCenter;
            }
            float y=170;
            if(pair.HasValue)
            {
                // A gold bracket under the two buttons whose counts tie.
                float ax=8+(pair.Value.a-1)*(bw+gap)+bw/2,bx2=8+(pair.Value.b-1)*(bw+gap)+bw/2;
                var bracket=CfgUi.Rect(c,"Equal-direction bracket",ax,166,bx2-ax,8);var img=bracket.gameObject.AddComponent<Image>();img.color=Gold;img.raycastTarget=false;
                var label=CfgUi.Label(c,"同数",ax,176,bx2-ax,18,12,Gold);label.alignment=TextAnchor.MiddleCenter;y=200;
            }
            int directions=PieceDefinition.SlideDirections(n,k);
            CfgUi.Label(c,(k==1?"軸方向の走り":k+"軸の斜め走り")+"："+directions.ToString("N0",CultureInfo.InvariantCulture)+"方向",8,y,330,36,22,Gold);y+=38;
            long combos=k>0?directions>>k:0;
            CfgUi.Label(c,"（軸の選び方 "+combos.ToString("N0",CultureInfo.InvariantCulture)+"通り × 向き "+(1<<Math.Max(0,k)).ToString("N0",CultureInfo.InvariantCulture)+"通り）",8,y,330,26,14,Muted);y+=30;
            CfgUi.Label(c,k==1?"1本の軸に沿ってまっすぐ進みます。":k+"本の座標が同じマス数ずつ進みます。\nほかの軸は動きません。",8,y,330,50,16);y+=56;
            if(pair.HasValue)
            {
                int a=pair.Value.a,pb=pair.Value.b;int tie=PieceDefinition.SlideDirections(n,a);
                CfgUi.Label(c,n+"次元では "+a+"軸 と "+pb+"軸 が同じ"+tie.ToString("N0",CultureInfo.InvariantCulture)+"方向。\n飛車と角のような「対の駒」になります。",8,y,330,50,15,Gold);y+=56;
                Button(c,"slide-pair","方向数がそろう組にする（"+a+"軸・"+pb+"軸）",8,y,330,42,()=>SetSlidePair(a,pb));y+=52;
            }
            else{CfgUi.Label(c,n+"次元には方向数がそろう組はありません\n（5・8・11次元にあります）。",8,y,330,50,15,Muted);y+=56;}
            // The other slider's current setting, so the pair reads as one decision.
            string otherId=d.Id=="rook-h11"?"bishop-h22":"rook-h11";var other=draft.Current.Definitions.FirstOrDefault(x=>x.Id==otherId);
            if(other!=null&&IsSliderKind(other.Kind)){int ok=SlideAxesOf(other);CfgUi.Label(c,(ShortNames.TryGetValue(otherId,out var on)?on:other.Name)+"："+ok+"軸（"+PieceDefinition.SlideDirections(n,ok).ToString("N0",CultureInfo.InvariantCulture)+"方向）",8,y,330,26,14,Muted);y+=34;}
            Field(c,"distance","最大距離",d.MaxDistance,y,v=>Edit(x=>x.MaxDistance=v,false));y+=120;
            bottom=y;
        }
        void SetSlideAxesSelected(int k)
        {
            int n=DraftDimensions;if(k<1||k>n)return;
            Edit(x=>{if(IsSliderKind(x.Kind))SetSlideAxes(x,k,n);});Record("SlideAxes",selectedDefinition+"="+k.ToString(CultureInfo.InvariantCulture));
        }
        // 飛車 takes the smaller axis count of the tie, 角行 the larger: one edit, so one Undo restores both.
        void SetSlidePair(int a,int b)
        {
            int n=DraftDimensions;
            draft.Edit(d=>{foreach(var x in d.Definitions){if(!IsSliderKind(x.Kind))continue;if(x.Id=="rook-h11")SetSlideAxes(x,a,n);else if(x.Id=="bishop-h22")SetSlideAxes(x,b,n);}});
            AfterEdit();BuildDetails();Record("SlidePair",a+"/"+b);
        }
        // Candidates at the initial position grow about threefold per axis: 46,591 in 8D fit the default budget of 65,536,
        // 138,601 in 9D do not, and 11D needs 1,241,209. A budget is a ceiling, not a cost, so one generous value serves 9D and up.
        // A copied layout has nine times the pieces: 7D already exceeds the default, and 11D needs about 11.2 million.
        static int RequiredCpuBudget(int dimensions,bool copied)=>copied?(dimensions>=11?20000000:dimensions>=7?5000000:0):(dimensions>=9?5000000:0);
        // Only raises a valid budget that is too small: text that is not a number stays for the player to correct, and nothing below 1 is written.
        void EnsureCpuBudget(int dimensions,bool copied)
        {
            int required=RequiredCpuBudget(dimensions,copied);
            if(required>0&&int.TryParse(options.CpuBudget,NumberStyles.Integer,CultureInfo.InvariantCulture,out int budget)&&budget<required)options.CpuBudget=required.ToString(CultureInfo.InvariantCulture);
        }
        // One row of the initial-layout list: the name on the left, the piece count on the right.
        void LayoutRow(RectTransform c,string layout,string name,int pieces,float y,bool selected)
        {
            var b=Button(c,"layout-"+layout.Replace('/','-'),name,8,y,330,40,()=>SetLayout(layout),selected);
            var text=b.GetComponentInChildren<Text>();text.alignment=TextAnchor.MiddleLeft;text.rectTransform.anchoredPosition+=new Vector2(8,0);text.rectTransform.sizeDelta-=new Vector2(16,0);
            var count=CfgUi.Label(b.transform,pieces.ToString(CultureInfo.InvariantCulture)+"駒",8,0,314,40,14,selected?new Color(.81f,.88f,1):Muted);count.alignment=TextAnchor.MiddleRight;
        }
        // The next match's initial layout. The display anchor depends on it, so the trial view starts again (SyncViewToDraft).
        void SetLayout(string layout)
        {
            if(layout==draft.Current.InitialLayout)return;
            var axis=DraftData.CopyAxisOf(layout);if(layout!=DraftData.FlatLayout&&!(axis.HasValue&&axis.Value<DraftDimensions))return;
            draft.Edit(d=>d.InitialLayout=layout);SyncViewToDraft();
            AfterEdit();BuildDetails();Record("InitialLayout",layout);
        }
        // The next match's N. Masks that meant "every axis" keep meaning that; fixed definitions always use every axis;
        // a per-axis mask or a step count that no longer fits is clipped. Trial pieces outside the new board are dropped.
        // A copy axis the smaller board no longer has falls back to the flat layout in the same edit, so one Undo restores both.
        void SetDimensions(int n)
        {
            int old=DraftDimensions;if(n==old||n<BoundsN.MinDimensions||n>BoundsN.MaxDimensions)return;
            draft.Edit(d=>ResizeDraft(d,old,n));
            SyncViewToDraft();AfterEdit();BuildPieces();BuildDetails();Record("Dimensions",n.ToString(CultureInfo.InvariantCulture));
        }
        // The edit behind SetDimensions, also used for the lessons' own 5D rules.
        static void ResizeDraft(DraftData d,int old,int n)
        {
            string Inv(int value)=>value.ToString(CultureInfo.InvariantCulture);
            d.Dimensions=Inv(n);
            foreach(var x in d.Definitions)
            {
                int.TryParse(x.AxesMask,NumberStyles.Integer,CultureInfo.InvariantCulture,out int mask);
                bool every=!IsEditable(x)||mask==BoundsN.FullAxesMask(old)||mask<=0;
                x.AxesMask=Inv(every?BoundsN.FullAxesMask(n):mask&BoundsN.FullAxesMask(n));
                int.TryParse(x.MaxAxes,NumberStyles.Integer,CultureInfo.InvariantCulture,out int axes);
                // A slider's axis count is a rule choice, not "every axis": it only shrinks when the new board has fewer axes.
                if(x.Kind=="hkk/1"){if(axes>n||axes<=0)SetSlideAxes(x,Math.Max(1,Math.Min(axes,n)),n);}
                else if(!IsEditable(x)||axes==old||axes>n||axes<=0)x.MaxAxes=Inv(n);
            }
            var copy=DraftData.CopyAxisOf(d.InitialLayout);if(copy.HasValue&&copy.Value>=n)d.InitialLayout=DraftData.FlatLayout;
        }
        // N and initial layout the trial board and view were built for.
        int viewDimensions;string viewLayout;
        // Brings everything on the options screen that depends on the draft's N or initial layout in line with the draft, however it got
        // there (N or layout button, Undo, Redo, startup): the trial board's domain and selection, the view and its anchor, the camera,
        // the rotation axis, the CPU budget and the titles. The next save then always pairs the draft with a board and view of the same N.
        void SyncViewToDraft()
        {
            int n=DraftDimensions;var copy=DraftCopyAxis;string layout=draft.Current.InitialLayout;
            if(preview.Bounds.Dimensions!=n)
            {
                // Trial pieces outside the new board are dropped.
                var bounds=new BoundsN(preview.Bounds.Min,preview.Bounds.Max,n);
                preview=new PreviewSession(bounds,preview.Pieces.Where(p=>bounds.Contains(p.Position)).Select(p=>p.At(bounds.Normalize(p.Position))));
                if(preview.Find(selectedPiece)==null)selectedPiece=preview.Pieces.FirstOrDefault()?.Id??"";
            }
            var b=preview.Bounds;
            selectedCell=b.Contains(selectedCell)?b.Normalize(selectedCell):b.Origin;planeReferenceCell=b.Contains(planeReferenceCell)?b.Normalize(planeReferenceCell):b.Origin;
            if(n!=viewDimensions||layout!=viewLayout||trialSlice.Frame.Dimensions!=n)
            {
                if(n!=viewDimensions)rotationAxis=LogicalAxis.W;
                viewDimensions=n;viewLayout=layout;EnsureCpuBudget(n,copy.HasValue);trialSlice=NewPivotSlice(n,copy);
                // During a match only the trial view waiting in the background changes.
                if(session==null||opened){slice=trialSlice;previewRenderer.SetDisplayState(slice.DisplayState);if(PlaneLacksAxis(n))SelectPlane(0);if(opened)UpdateCamera();}
                UpdateHelpText();Record("TrialViewReset","N="+n+";layout="+layout);
            }
            UpdateHeader();UpdateWindowTitle();
        }
        // Leaving Options never waits on the disk: a failed save keeps the edit in memory, says so and is retried in the background.
        void LeaveSettings()
        {
            if(ownership.Owner!=InputOwner.None)return;
            if(startupLoadError==""&&!AutoSave())Record("OptionsLeftUnsaved",notice);
            ShowMenu();
        }
        // The controls help follows the toggles, or heads a column of its own (helpColumn) on the 表示・操作 page.
        void BuildDisplay(RectTransform c,float y=8,RectTransform helpColumn=null)
        {
            Toggle(c,"移動候補を表示",showCandidates,8,y,330,v=>showCandidates=v);Toggle(c,"攻撃範囲を表示",showAttacks,8,y+49,330,v=>showAttacks=v);Toggle(c,"座標グリッドを表示",showGrid,8,y+98,330,v=>showGrid=v);
            Toggle(c,"断面補助を表示",showHelpers,8,y+147,330,v=>showHelpers=v);
            Toggle(c,"選択した駒の名前を表示",PieceNameEnabled,8,y+196,330,v=>{PieceNameEnabled=v;Record("Pref",PieceNameKey+"="+v);});
            Toggle(c,"拡大した超平面図に駒の点を表示",MagnifierDotsEnabled,8,y+245,330,v=>{MagnifierDotsEnabled=v;Record("Pref",MagnifierDotsKey+"="+v);});
            Toggle(c,"超平面図に駒の点を小さく表示",PanelDotsEnabled,8,y+294,330,v=>{PanelDotsEnabled=v;Record("Pref",PanelDotsKey+"="+v);});
            Toggle(c,"超平面図を盤の断面に合わせて並べ替える",PanelsFollowSection,8,y+343,330,v=>{PanelsFollowSection=v;Record("Pref",PanelsFollowKey+"="+v);});
            Toggle(c,"対局中に操作ヒントを表示",HintsEnabled,8,y+392,330,v=>{HintsEnabled=v;Record("Pref",HintsKey+"="+v);});
            if(helpColumn==null){helpText=CfgUi.Label(c,HelpText(),8,y+465,330,440,17);return;}
            CfgUi.Label(helpColumn,"操作",8,8,330,34,21,Gold);helpText=CfgUi.Label(helpColumn,HelpText(),8,50,330,440,17);
        }
        Text helpText;
        // Ctrl+drag turns X and Y (Z with Shift) against the current rotation axis; a 3D board has nothing to turn against.
        string HelpText()
        {
            string h=SliceAxes.AxisName(SliceAxes.AxisOfCore(RotationCoreAxis));
            string rotation=CurrentDimensions<=3?"":"Ctrl＋左：X"+h+"／Y"+h+"回転（4次元）\nCtrl＋Shift＋左：Z"+h+"／Y"+h+"回転\n4〜9・0・-：Ctrl で回す相手の軸\n";
            return "左クリック：駒を選ぶ・動かす\nTab：見えない候補の断面へ（対局中）\n左／右ドラッグ：視点回転\n中／Shift＋左：平行移動\nホイール：拡大・縮小\n"+rotation+"\n軸名クリック：その軸が入った断面へ\n軸ドラッグ：断面を平行移動\nShift／Ctrl＋軸ドラッグ：1マスずつ\nR：最初の表示に戻す\n図・軸の上で Home：原点へ\nShift＋Home：最初の位置へ\nF1：遊び方　Esc：戻る／操作取消";
        }
        void UpdateHelpText(){if(helpText!=null)helpText.text=HelpText();}
        void ShowMenu()
        {
            CancelActiveInput();
            opened=false;settings.gameObject.SetActive(false);matchHud.gameObject.SetActive(false);menu.gameObject.SetActive(true);KeepScreenNotice();
            // A button selected on another screen must not also receive the Enter that the menu cursor handles.
            CfgUi.ReleaseSelection();
            backgroundCamera.enabled=false;
            rig.Camera.targetTexture=null;rig.Camera.transform.position=new Vector3(.58f,1.38f,-.8f);rig.Camera.transform.LookAt(new Vector3(-.14f,1.02f,0));cameraRevision++;
            geometryDirty=true;result=null;attackCells=Array.Empty<QN>();visuals.RefreshCaptured(Array.Empty<Cfg2CapturedPiece>());UpdateWindowTitle();Record("Menu");
            WritePlayerIdentity("Menu");
        }
        void OpenSettings()
        {
            CancelActiveInput();
            opened=true;menu.gameObject.SetActive(false);matchHud.gameObject.SetActive(false);settings.gameObject.SetActive(true);
            backgroundCamera.transform.position=new Vector3(.58f,1.38f,-.8f);backgroundCamera.transform.LookAt(new Vector3(-.14f,1.02f,0));backgroundCamera.enabled=true;
            if(previewTexture==null){previewTexture=new RenderTexture(1052,1168,24){name="CFG2 live section"};previewTexture.Create();previewImage.texture=previewTexture;}
            rig.Camera.targetTexture=previewTexture;UpdateCamera();QueueQuery();BuildPieces();BuildDetails();UpdateHeader();UpdateWindowTitle();Record("OptionsOpened");
            WritePlayerIdentity("Settings");
        }
    }
}
