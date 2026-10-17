using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // 投影ビュー: every direction a piece can slide in, drawn around the piece at the origin after a perspective projection of the
    // N-dimensional board down to three dimensions (one hidden axis at a time) and then to the screen. The XYZ grids through
    // the origin are projected the same way, so they bend with the hidden-axis rotation. All rays are faint until a cell of the
    // rotation-plane table or a ±axis button is chosen. The table and the sliders live in a controller panel beside the view:
    // a cell names a plane (a pair of axes); the plane last clicked is the one the angle slider turns.
    internal sealed class Cfg2ProjectionView : MonoBehaviour
    {
        Cfg2ProjectionGraphic graphic;Cfg2ProjectionTableGraphic tableGraphic;RectTransform controls,tableRect,listHost;InputOwnership ownership;Func<float> uiScale;Action<string,string> record;
        readonly List<Text> axisLabels=new List<Text>();readonly List<Text> rowLabels=new List<Text>(),columnLabels=new List<Text>();
        Text countLabel,rotationLabel,axisHint,tableTitle,tableHint,turnTitle,activeLabel,listTitle,listMore,zoomTitle,zoomLabel;Slider angleSlider,zoomSlider;
        readonly List<Button> axisButtons=new List<Button>(),stepButtons=new List<Button>(),listButtons=new List<Button>();readonly List<Slider> listSliders=new List<Slider>();readonly List<Text> listValues=new List<Text>();
        readonly List<(int a,int b)> listedPlanes=new List<(int a,int b)>();Button resetButton,clearButton;
        // Board and piece shown.
        int dimensions=4;string pieceName="",refreshKey;
        // Directions in UI axis order (X Y Z W V ...), one int[dimensions] each, and the ray length in cells.
        readonly List<int[]> directions=new List<int[]>();int rayLength=1;
        // The rotation is the product of one turn per plane, composed in table order from the angles here (degrees, −180..180).
        double[,] rotation;readonly SortedDictionary<(int a,int b),double> angles=new SortedDictionary<(int a,int b),double>();
        (int a,int b) active=(0,1);
        // 3-D camera around the projected picture.
        float yaw=-35,pitch=22,zoom=1;
        // Chosen cells (a,b with a<=b; a==b means "uses axis a"), chosen ±axes, the hovered cell, the plane being dragged.
        (int a,int b)? hovered,dragging;readonly HashSet<(int a,int b)> selected=new HashSet<(int a,int b)>();readonly HashSet<(int axis,int sign)> axisSelected=new HashSet<(int axis,int sign)>();
        // Pointer gesture.
        int pointer=-1;Vector2 downPosition,lastPosition;bool pressed,moved,orbiting,syncing;
        // UI meshes hold at most 65,000 vertices: faint rays are one quad each, lit rays get steps and glow only while few.
        internal const int MaxFaintRays=6000,MaxLitRays=3000,MaxDetailedRays=700;
        internal const float MinZoom=.4f,MaxZoom=3f;
        internal int DirectionCount=>directions.Count;
        internal int HighlightedCount{get;private set;}
        internal IReadOnlyCollection<(int a,int b)> Selected=>selected;
        internal IReadOnlyCollection<(int axis,int sign)> AxisSelected=>axisSelected;
        internal (int a,int b)? Hovered=>hovered;
        internal (int a,int b) Active=>active;
        internal double[,] Rotation=>rotation;
        internal float Zoom=>zoom;
        internal double Angle(int a,int b)=>angles.TryGetValue(a<b?(a,b):(b,a),out double v)?(a<b?v:-v):0;
        internal void Initialize(RectTransform controller,InputOwnership input,Func<float> scale,Action<string,string> recorder)
        {
            controls=controller;ownership=input;uiScale=scale;record=recorder;
            Cfg2Ui.ClearOwned(transform,true);Cfg2Ui.ClearOwned(controls,true);
            axisLabels.Clear();rowLabels.Clear();columnLabels.Clear();axisButtons.Clear();stepButtons.Clear();listButtons.Clear();listSliders.Clear();listValues.Clear();listedPlanes.Clear();
            // Rays near a hidden-axis camera project far outside the panel: everything in the view is clipped to it.
            if(!TryGetComponent<RectMask2D>(out _))gameObject.AddComponent<RectMask2D>();
            var rect=CfgUi.Rect(transform,"Projected directions",0,0,1,1);CfgUi.Fill(rect);
            graphic=rect.gameObject.AddComponent<Cfg2ProjectionGraphic>();graphic.View=this;graphic.color=Color.white;graphic.raycastTarget=true;
            var surface=rect.gameObject.AddComponent<CfgPointerSurface>();surface.Down=OnViewDown;surface.Drag=OnDrag;surface.Up=OnUp;surface.Scroll=OnScroll;
            countLabel=CfgUi.Label(transform,"",10,6,400,22,14,Cfg2Ui.Gold);
            rotationLabel=CfgUi.Label(transform,"",10,28,400,20,12,new Color(.75f,.80f,.86f));
            axisHint=CfgUi.Label(transform,"方向で選ぶ（＋側・−側）　背景ドラッグ：視点　ホイール：ズーム",10,0,400,18,11,new Color(.62f,.68f,.76f));
            // Controller: table, angle slider of the active plane, list of turned planes, zoom, reset.
            CfgUi.Label(controls,"回転コントローラー",12,8,300,26,17,Cfg2Ui.Gold);
            tableTitle=CfgUi.Label(controls,"① 面を選ぶ",12,36,300,18,13);
            // One Graphic per GameObject: the table graphic itself is the raycast target (its whole rect, not just the drawn cells).
            tableRect=CfgUi.Rect(controls,"Rotation planes",34,58,100,100);
            tableGraphic=tableRect.gameObject.AddComponent<Cfg2ProjectionTableGraphic>();tableGraphic.View=this;tableGraphic.color=Color.white;tableGraphic.raycastTarget=true;
            var tableSurface=tableRect.gameObject.AddComponent<CfgPointerSurface>();tableSurface.Down=OnTableDown;tableSurface.Drag=OnDrag;tableSurface.Up=OnUp;
            tableHint=CfgUi.Label(controls,"クリック＝強調＋回す面に。もう一度で解除。\nドラッグ＝その面で回転。右クリック＝全解除。",12,0,320,32,11,new Color(.62f,.68f,.76f));
            turnTitle=CfgUi.Label(controls,"② 選んだ面を回す",12,0,300,18,13);
            activeLabel=CfgUi.Label(controls,"",12,0,300,22,15,Cfg2Ui.Gold);
            angleSlider=Cfg2Ui.Slider(controls,12,0,100,-180,180,0,v=>{if(syncing)return;SetAngle(active,v);UpdateTexts();graphic.SetVerticesDirty();},()=>record?.Invoke("ProjectionTurn","slider "+CellName(active)));
            foreach(var step in new[]{-15,0,15}){int s=step;stepButtons.Add(Cfg2Ui.Button(controls,s==0?"0°":(s>0?"+":"−")+Math.Abs(s)+"°",0,0,50,26,()=>{if(s==0)SetAngle(active,0);else AddAngle(active.a,active.b,s);record?.Invoke("ProjectionTurn","step "+CellName(active)+" "+s);UpdateTexts();graphic.SetVerticesDirty();}));}
            listTitle=CfgUi.Label(controls,"③ 回した面（名前を押すと選び直し）",12,0,320,18,13);
            listHost=CfgUi.Rect(controls,"Turned planes",12,0,100,10);listMore=CfgUi.Label(controls,"",12,0,300,18,11,new Color(.62f,.68f,.76f));
            zoomTitle=CfgUi.Label(controls,"④ ズーム",12,0,120,18,13);zoomLabel=CfgUi.Label(controls,"",0,0,80,18,13,Cfg2Ui.Gold);zoomLabel.alignment=TextAnchor.MiddleRight;
            zoomSlider=Cfg2Ui.Slider(controls,12,0,100,MinZoom,MaxZoom,1,v=>{if(syncing)return;zoom=v;UpdateTexts();graphic.SetVerticesDirty();},()=>record?.Invoke("ProjectionZoom",zoom.ToString("0.00",CultureInfo.InvariantCulture)));
            resetButton=Cfg2Ui.Button(controls,"初期位置に戻す",0,0,100,30,()=>ResetRotation());
            clearButton=Cfg2Ui.Button(controls,"選択をクリア",0,0,100,30,ClearSelection);
            foreach(var b in stepButtons.Concat(new[]{resetButton,clearButton})){var t=b.GetComponentInChildren<Text>();t.alignment=TextAnchor.MiddleCenter;t.fontSize=13;}
            ResetRotation(false);EnsureLabels();EnsureAxisButtons();Layout();
        }
        // Rebuilds the direction list for the piece. Allowed unit directions of {-1,0,1}^N are enumerated like the preview query does.
        internal void Refresh(PreviewPreset preset,string definitionId,string displayName)
        {
            int n=preset?.Bounds.Dimensions??4;var definition=preset?.Find(definitionId);
            // The list only changes with the rules or the piece; a view change (the usual reason for a refresh) keeps it.
            string key=(preset?.RuleHash??"")+"/"+definitionId+"/"+n;
            if(key==refreshKey&&pieceName==displayName)return;refreshKey=key;
            if(n!=dimensions){dimensions=n;selected.RemoveWhere(c=>c.a>=n||c.b>=n);axisSelected.RemoveWhere(c=>c.axis>=n);hovered=null;ResetRotation(false);EnsureLabels();EnsureAxisButtons();Layout();}
            pieceName=displayName;directions.Clear();rayLength=1;
            if(definition!=null&&definition.IsSupported)
            {
                var axes=SliceAxes.Axes(n);var core=new int[n];for(int i=0;i<n;i++)core[i]=SliceAxes.CoreAxis(axes[i]);
                rayLength=definition.IsSlider?Math.Max(1,definition.MaxDistance):1;
                if(definition.IsEditable)
                {
                    var u=new int[n];for(int i=0;i<n;i++)u[i]=-1;
                    for(int axis=0;axis>=0;)
                    {
                        if(definition.Allows(new QN(u),Side.A,preset.ForwardAxis)){var d=new int[n];for(int i=0;i<n;i++)d[i]=u[core[i]];directions.Add(d);}
                        for(axis=n-1;axis>=0&&u[axis]==1;axis--)u[axis]=-1;
                        if(axis>=0)u[axis]++;
                    }
                }
                else
                {
                    // Fixed pieces: their own deltas, each drawn as one ray of its own length (the lance's 8 cells, the knight's jump).
                    var seen=new HashSet<string>();
                    foreach(var delta in Movement.Deltas(definition.FixedKind,Side.A,n))
                    {
                        int g=0;foreach(int x in delta)g=Gcd(g,Math.Abs(x));if(g==0)continue;
                        var d=new int[n];for(int i=0;i<n;i++)d[i]=delta[core[i]]/g;string text=string.Join(",",d);
                        if(seen.Add(text))directions.Add(d);rayLength=Math.Max(rayLength,g);
                    }
                    if(definition.Kind==PreviewMovementKind.Knight)rayLength=1;
                }
            }
            UpdateTexts();graphic.SetVerticesDirty();
        }
        static int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}
        // ---- rotation ----
        internal void ResetRotation(bool notify=true)
        {
            angles.Clear();
            // The hidden axes start a little way out of the origin so that W, V, ... can be told apart at once.
            for(int a=3;a<dimensions;a++){SetAngle((a%3,a),a==3?32:a==4?26:14+6*(a-5),false);if(a>=4)SetAngle(((a+1)%3,a),10,false);}
            Rebuild();active=dimensions>3?(0,3):(0,1);yaw=-35;pitch=22;zoom=1;listReset=true;
            if(notify){record?.Invoke("ProjectionReset","");UpdateTexts();graphic?.SetVerticesDirty();}
        }
        static double Wrap(double degrees){double d=(degrees+180)%360;if(d<0)d+=360;return d-180;}
        // Sets the angle of one plane (a<b, in degrees); a and b swapped means the opposite turn.
        internal void SetAngle((int a,int b) plane,double degrees,bool rebuild=true)
        {
            if(plane.a==plane.b||plane.a<0||plane.b<0||plane.a>=dimensions||plane.b>=dimensions)return;
            var key=plane.a<plane.b?plane:(plane.b,plane.a);double v=Wrap(plane.a<plane.b?degrees:-degrees);
            if(Math.Abs(v)<.05)angles.Remove(key);else angles[key]=v;
            if(rebuild)Rebuild();
        }
        internal void AddAngle(int a,int b,double degrees)=>SetAngle((a,b),Angle(a,b)+degrees);
        // Kept for callers that think in turns: adds to the plane's angle.
        internal void Turn(int a,int b,double degrees)=>AddAngle(a,b,degrees);
        void Rebuild()
        {
            rotation=new double[dimensions,dimensions];for(int i=0;i<dimensions;i++)rotation[i,i]=1;
            foreach(var kv in angles)
            {
                int a=kv.Key.a,b=kv.Key.b;double t=kv.Value*Math.PI/180,c=Math.Cos(t),s=Math.Sin(t);
                for(int j=0;j<dimensions;j++){double ra=rotation[a,j],rb=rotation[b,j];rotation[a,j]=c*ra-s*rb;rotation[b,j]=s*ra+c*rb;}
            }
        }
        // Projection of a point in UI axis order: N → 3 by one perspective division per hidden axis (camera at +Distance on it),
        // then the fixed 3-D camera. depth sums the hidden coordinates so nearer rays can be drawn brighter and thicker.
        internal const double HiddenCamera=16,Camera3=16;
        internal Vector3 Project(double[] point,out float depth)
        {
            var p=new double[dimensions];for(int i=0;i<dimensions;i++){double v=0;for(int j=0;j<dimensions;j++)v+=rotation[i,j]*point[j];p[i]=v;}
            double d=0;
            // The divisor is kept at 2 or more so a point almost at a hidden camera grows at most eightfold per axis.
            for(int a=dimensions-1;a>=3;a--){double s=HiddenCamera/Math.Max(2,HiddenCamera-p[a]);d+=p[a]/HiddenCamera;for(int i=0;i<a;i++)p[i]*=s;}
            double x=p[0],y=dimensions>1?p[1]:0,z=dimensions>2?p[2]:0;
            double cy=Math.Cos(yaw*Math.PI/180),sy=Math.Sin(yaw*Math.PI/180),cp=Math.Cos(pitch*Math.PI/180),sp=Math.Sin(pitch*Math.PI/180);
            double x1=x*cy-z*sy,z1=x*sy+z*cy,y2=y*cp-z1*sp,z2=y*sp+z1*cp;
            double s3=Camera3/Math.Max(2,Camera3-z2);depth=(float)d;
            return new Vector3((float)(x1*s3),(float)(y2*s3),(float)z2);
        }
        internal float Scale=>Mathf.Min(graphic.rectTransform.rect.width,graphic.rectTransform.rect.height)/17f*zoom;
        internal Vector2 Center=>new Vector2(graphic.rectTransform.rect.width*.5f,-graphic.rectTransform.rect.height*.5f);
        internal Vector2 ToScreen(Vector3 projected)=>Center+new Vector2(projected.x,projected.y)*Scale;
        internal int RayLength=>rayLength;
        internal IReadOnlyList<int[]> Directions=>directions;
        internal int Dimensions=>dimensions;
        internal const double AxisLength=5;
        // Highlight level of a direction: 2 chosen (by a cell or a ±axis button), 1 hovered, 0 faint; 1 for every ray while nothing is chosen.
        internal int Level(int[] d)
        {
            if(!AnyChoice)return 1;
            foreach(var c in selected)if(d[c.a]!=0&&d[c.b]!=0)return 2;
            foreach(var s in axisSelected)if(Math.Sign(d[s.axis])==s.sign)return 2;
            if(hovered.HasValue&&d[hovered.Value.a]!=0&&d[hovered.Value.b]!=0)return 1;
            return 0;
        }
        internal bool AnyChoice=>selected.Count>0||axisSelected.Count>0||hovered.HasValue;
        internal (int a,int b)? Dragging=>dragging;
        // ---- table geometry (inside tableRect; cell (row,column) with row<=column) ----
        internal float CellSize{get{float room=controls==null?100:controls.rect.width-52;return Mathf.Clamp(Mathf.Floor(room/Math.Max(1,dimensions)),10,40);}}
        internal Rect CellRect(int row,int column){float cs=CellSize;return new Rect(column*cs,-row*cs-cs,cs,cs);}
        bool TryCell(Vector2 local,out (int a,int b) cell)
        {
            cell=default;float cs=CellSize;float x=local.x,y=-local.y;
            if(x<0||y<0||x>=cs*dimensions||y>=cs*dimensions)return false;
            int column=(int)(x/cs),row=(int)(y/cs);if(column<row)return false;cell=(row,column);return true;
        }
        bool TableLocal(Vector2 screen,Camera camera,out Vector2 local)=>RectTransformUtility.ScreenPointToLocalPointInRectangle(tableRect,screen,camera,out local);
        // ---- pointer ----
        bool Begin(PointerEventData e)
        {
            if(pressed||ownership.Owner!=InputOwner.None||CfgUi.EditingText)return false;
            if(e.button==PointerEventData.InputButton.Right){ClearSelection();return false;}
            if(e.button!=PointerEventData.InputButton.Left)return false;
            if(!ownership.TryBegin(InputOwner.Rotation4D,e.pointerId,()=>{pressed=false;dragging=null;orbiting=false;}))return false;
            pressed=true;moved=false;pointer=e.pointerId;downPosition=lastPosition=e.position;orbiting=false;dragging=null;return true;
        }
        void OnViewDown(PointerEventData e){if(Begin(e))orbiting=true;}
        void OnTableDown(PointerEventData e)
        {
            if(!TableLocal(e.position,e.pressEventCamera,out var local)||!TryCell(local,out var cell)){if(e.button==PointerEventData.InputButton.Right)ClearSelection();return;}
            if(Begin(e))dragging=cell;
        }
        void OnDrag(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer||!ownership.IsOwner(InputOwner.Rotation4D,pointer))return;
            float scale=Mathf.Max(.01f,uiScale?.Invoke()??1);var delta=(e.position-lastPosition)/scale;lastPosition=e.position;
            if(!moved&&(e.position-downPosition).magnitude/scale<5)return;moved=true;
            if(dragging.HasValue&&dragging.Value.a!=dragging.Value.b){active=dragging.Value;AddAngle(dragging.Value.a,dragging.Value.b,delta.x*.6f);}
            else if(dragging.HasValue){int other=dragging.Value.a==0?1:0;active=(other,dragging.Value.a);AddAngle(other,dragging.Value.a,delta.x*.6f);}
            else if(orbiting){yaw+=delta.x*.4f;pitch=Mathf.Clamp(pitch-delta.y*.4f,-80,80);}
            UpdateTexts();graphic.SetVerticesDirty();
        }
        void OnUp(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer)return;pressed=false;
            bool owned=ownership.IsOwner(InputOwner.Rotation4D,pointer);if(owned)ownership.Release(InputOwner.Rotation4D,pointer);
            if(!moved&&dragging.HasValue)
            {
                var cell=dragging.Value;bool on=!selected.Remove(cell);if(on)selected.Add(cell);
                if(on&&cell.a!=cell.b)active=cell;
                record?.Invoke("ProjectionSelect",CellName(cell)+"="+on);
            }
            else if(moved)record?.Invoke("ProjectionTurn",dragging.HasValue?CellName(dragging.Value):"orbit");
            dragging=null;orbiting=false;UpdateTexts();graphic.SetVerticesDirty();
        }
        void OnScroll(PointerEventData e)
        {
            if(ownership.Owner!=InputOwner.None||CfgUi.EditingText)return;float wheel=e.scrollDelta.y;if(Mathf.Approximately(wheel,0))return;
            zoom=Mathf.Clamp(zoom*Mathf.Exp(wheel*.08f),MinZoom,MaxZoom);record?.Invoke("ProjectionZoom",zoom.ToString("0.00",CultureInfo.InvariantCulture));UpdateTexts();graphic.SetVerticesDirty();
        }
        void Update()
        {
            if(pressed&&!ownership.IsOwner(InputOwner.Rotation4D,pointer)){pressed=false;dragging=null;orbiting=false;}
            // Hover: the cell under the mouse while no gesture is running.
            (int a,int b)? over=null;
            if(!pressed&&tableRect!=null&&tableRect.gameObject.activeInHierarchy&&TableLocal(Input.mousePosition,null,out var local)&&TryCell(local,out var cell))over=cell;
            if(!Nullable.Equals(over,hovered)){hovered=over;UpdateTexts();graphic.SetVerticesDirty();}
        }
        void OnDisable(){if(pressed&&ownership!=null&&ownership.IsOwner(InputOwner.Rotation4D,pointer))ownership.Cancel();pressed=false;dragging=null;orbiting=false;hovered=null;}
        void ClearSelection(){if(selected.Count==0&&axisSelected.Count==0)return;selected.Clear();axisSelected.Clear();record?.Invoke("ProjectionSelect","clear");UpdateTexts();graphic.SetVerticesDirty();}
        internal void SetSelected(IEnumerable<(int a,int b)> cells){selected.Clear();foreach(var c in cells)if(c.a<=c.b&&c.b<dimensions)selected.Add(c);UpdateTexts();graphic?.SetVerticesDirty();}
        internal void SetAxisSelected(int axis,int sign,bool on){var key=(axis,Math.Sign(sign));if(on)axisSelected.Add(key);else axisSelected.Remove(key);UpdateTexts();graphic?.SetVerticesDirty();}
        internal void SetActive((int a,int b) plane){if(plane.a!=plane.b&&plane.b<dimensions)active=plane.a<plane.b?plane:(plane.b,plane.a);UpdateTexts();}
        internal string AxisName(int uiAxis)=>SliceAxes.AxisName(SliceAxes.Axes(Math.Max(dimensions,uiAxis+1))[uiAxis]);
        internal Color AxisColor(int uiAxis)=>Cfg2Projection.AxisColor(SliceAxes.Axes(Math.Max(dimensions,uiAxis+1))[uiAxis]);
        string CellName((int a,int b) c)=>c.a==c.b?AxisName(c.a):AxisName(c.a)+"-"+AxisName(c.b);
        // ---- widgets that depend on N ----
        void EnsureLabels()
        {
            while(axisLabels.Count<dimensions){var t=CfgUi.Label(transform,"",0,0,30,24,16);Cfg2Glow.GlowText(t,Color.white);axisLabels.Add(t);}
            while(rowLabels.Count<dimensions){rowLabels.Add(CfgUi.Label(controls,"",0,0,20,20,11));columnLabels.Add(CfgUi.Label(controls,"",0,0,20,20,11));}
            for(int i=0;i<axisLabels.Count;i++){bool on=i<dimensions;axisLabels[i].gameObject.SetActive(on);rowLabels[i].gameObject.SetActive(on);columnLabels[i].gameObject.SetActive(on);
                if(!on)continue;var color=AxisColor(i);string name=AxisName(i);
                axisLabels[i].text=name;axisLabels[i].color=color;axisLabels[i].GetComponent<Outline>().effectColor=Cfg2Glow.Alpha(color,.45f);
                rowLabels[i].text=name;rowLabels[i].color=color;columnLabels[i].text=name;columnLabels[i].color=color;
                rowLabels[i].fontSize=columnLabels[i].fontSize=dimensions<=5?12:dimensions<=8?10:9;rowLabels[i].alignment=TextAnchor.MiddleRight;columnLabels[i].alignment=TextAnchor.MiddleCenter;}
        }
        // One +/− button pair per axis at the bottom left of the view: "+Z" lights every direction that moves towards +Z.
        void EnsureAxisButtons()
        {
            if(axisButtons.Count==2*dimensions)return;
            foreach(var b in axisButtons)Destroy(b.gameObject);axisButtons.Clear();
            for(int axis=0;axis<dimensions;axis++)for(int sign=1;sign>=-1;sign-=2)
            {
                int a=axis,s=sign;var b=Cfg2Ui.Button(transform,(s>0?"+":"−")+AxisName(a),0,0,40,26,()=>{if(ownership.Owner!=InputOwner.None)return;bool on=!axisSelected.Contains((a,s));SetAxisSelected(a,s,on);record?.Invoke("ProjectionAxis",(s>0?"+":"-")+AxisName(a)+"="+on);});
                var t=b.GetComponentInChildren<Text>();t.alignment=TextAnchor.MiddleCenter;t.fontSize=dimensions<=6?13:11;t.color=AxisColor(a);axisButtons.Add(b);
            }
        }
        // Positions every widget from the current sizes of the view and the controller; called after a resize or a change of N.
        internal void Layout()
        {
            if(graphic==null||controls==null)return;float w=((RectTransform)transform).rect.width,h=((RectTransform)transform).rect.height,cw=controls.rect.width,ch=controls.rect.height;
            countLabel.rectTransform.sizeDelta=new Vector2(w-20,22);rotationLabel.rectTransform.sizeDelta=new Vector2(w-20,20);
            float bw=Mathf.Clamp((w-20)/Math.Max(1,dimensions)-4,24,46);
            axisHint.rectTransform.anchoredPosition=new Vector2(10,-(h-88));axisHint.rectTransform.sizeDelta=new Vector2(w-20,18);
            for(int i=0;i<axisButtons.Count;i++){int axis=i/2,row=i%2;var r=(RectTransform)axisButtons[i].transform;r.anchoredPosition=new Vector2(10+axis*(bw+4),-(h-66+row*30));r.sizeDelta=new Vector2(bw,26);}
            float cs=CellSize;int n=dimensions;float y=58;
            tableRect.anchoredPosition=new Vector2(34,-y);tableRect.sizeDelta=new Vector2(n*cs,n*cs);
            for(int i=0;i<n&&i<rowLabels.Count;i++)
            {
                rowLabels[i].rectTransform.anchoredPosition=new Vector2(10,-(y+i*cs));rowLabels[i].rectTransform.sizeDelta=new Vector2(22,cs);
                columnLabels[i].rectTransform.anchoredPosition=new Vector2(34+i*cs,-(y+n*cs));columnLabels[i].rectTransform.sizeDelta=new Vector2(cs,16);
            }
            y+=n*cs+20;float hintHeight=cw<260?48:32;tableHint.rectTransform.anchoredPosition=new Vector2(12,-y);tableHint.rectTransform.sizeDelta=new Vector2(cw-24,hintHeight);y+=hintHeight+8;
            turnTitle.rectTransform.anchoredPosition=new Vector2(12,-y);y+=20;activeLabel.rectTransform.anchoredPosition=new Vector2(12,-y);activeLabel.rectTransform.sizeDelta=new Vector2(cw-24,22);y+=26;
            var slider=(RectTransform)angleSlider.transform;slider.anchoredPosition=new Vector2(12,-y);slider.sizeDelta=new Vector2(cw-24,24);ResizeSlider(angleSlider,cw-24);y+=32;
            float sw=(cw-24-8)/3;for(int i=0;i<stepButtons.Count;i++){var r=(RectTransform)stepButtons[i].transform;r.anchoredPosition=new Vector2(12+i*(sw+4),-y);r.sizeDelta=new Vector2(sw,26);}y+=40;
            listTitle.rectTransform.anchoredPosition=new Vector2(12,-y);listTitle.rectTransform.sizeDelta=new Vector2(cw-24,18);y+=22;
            // Rows only as far as the zoom section (58) and the buttons (50) leave room; the rest is named in listMore.
            // Narrow panels stack the two buttons; rows only as far as the zoom section (58) and the buttons leave room.
            bool stacked=cw<260;float buttons=stacked?2*30+6:30;const float RowPitch=32;float nameWidth=stacked?46:54;
            listCapacity=Mathf.Clamp(Mathf.FloorToInt((ch-y-20-58-buttons-20)/RowPitch),0,8);int rows=Math.Min(listedPlanes.Count,listCapacity);
            listHost.anchoredPosition=new Vector2(12,-y);listHost.sizeDelta=new Vector2(cw-24,Math.Max(10,rows*RowPitch));
            for(int i=0;i<listedPlanes.Count;i++)
            {
                bool shown=i<rows;listButtons[i].gameObject.SetActive(shown);listSliders[i].gameObject.SetActive(shown);listValues[i].gameObject.SetActive(shown);
                var r=(RectTransform)listButtons[i].transform;r.anchoredPosition=new Vector2(0,-i*RowPitch);r.sizeDelta=new Vector2(nameWidth,26);
                float sliderWidth=cw-24-nameWidth-6-40;
                var s=(RectTransform)listSliders[i].transform;s.anchoredPosition=new Vector2(nameWidth+6,-(i*RowPitch+1));s.sizeDelta=new Vector2(sliderWidth,24);ResizeSlider(listSliders[i],sliderWidth);
                listValues[i].rectTransform.anchoredPosition=new Vector2(cw-24-38,-(i*RowPitch+3));listValues[i].rectTransform.sizeDelta=new Vector2(38,20);
            }
            y+=rows*RowPitch;listMore.rectTransform.anchoredPosition=new Vector2(12,-y);if(listMore.text!="")y+=20;y+=12;
            zoomTitle.rectTransform.anchoredPosition=new Vector2(12,-y);zoomLabel.rectTransform.anchoredPosition=new Vector2(cw-12-80,-y);y+=22;
            var zs=(RectTransform)zoomSlider.transform;zs.anchoredPosition=new Vector2(12,-y);zs.sizeDelta=new Vector2(cw-24,24);ResizeSlider(zoomSlider,cw-24);
            float by=Math.Max(y+40,ch-12-buttons);
            if(stacked)
            {
                ((RectTransform)resetButton.transform).anchoredPosition=new Vector2(12,-by);((RectTransform)resetButton.transform).sizeDelta=new Vector2(cw-24,30);
                ((RectTransform)clearButton.transform).anchoredPosition=new Vector2(12,-(by+36));((RectTransform)clearButton.transform).sizeDelta=new Vector2(cw-24,30);
            }
            else
            {
                float hw=(cw-24-6)/2;
                ((RectTransform)resetButton.transform).anchoredPosition=new Vector2(12,-by);((RectTransform)resetButton.transform).sizeDelta=new Vector2(hw,30);
                ((RectTransform)clearButton.transform).anchoredPosition=new Vector2(12+hw+6,-by);((RectTransform)clearButton.transform).sizeDelta=new Vector2(hw,30);
            }
            LayoutLabels();graphic.SetVerticesDirty();tableGraphic.SetVerticesDirty();
        }
        // Cfg2Ui.Slider lays its parts out for one width; a resize moves the track, fill area and handle area with it.
        static void ResizeSlider(Slider slider,float width)
        {
            var root=(RectTransform)slider.transform;
            foreach(RectTransform child in root)
            {
                if(child.name=="Track"||child.name=="Fill area")child.sizeDelta=new Vector2(width,6);
                else if(child.name=="Handle area")child.sizeDelta=new Vector2(width-18,24);
            }
        }
        // Axis labels sit beside the projected axis ends.
        internal void LayoutLabels()
        {
            if(graphic==null)return;
            for(int i=0;i<dimensions&&i<axisLabels.Count;i++){var p=new double[dimensions];p[i]=AxisLength;var k=ToScreen(Project(p,out _));axisLabels[i].rectTransform.anchoredPosition=new Vector2(k.x+8,k.y+12);}
        }
        // The list shows every plane that has been turned, the active one framed. A row stays once added (a slider being
        // dragged through 0° must not vanish under the pointer); the list empties only on a reset or a change of N.
        bool listReset;int listCapacity=8;
        void RefreshList()
        {
            var planes=listReset?new List<(int a,int b)>():listedPlanes.Where(p=>p.b<dimensions).ToList();listReset=false;
            foreach(var kv in angles)if(Math.Abs(kv.Value)>=.5&&!planes.Contains(kv.Key))planes.Add(kv.Key);
            if(!planes.Contains(active)&&angles.ContainsKey(active))planes.Add(active);
            int shown=Math.Min(planes.Count,Math.Max(listCapacity,1));
            if(!planes.Take(shown).SequenceEqual(listedPlanes))
            {
                foreach(var b in listButtons)Destroy(b.gameObject);foreach(var s in listSliders)Destroy(s.gameObject);foreach(var t in listValues)Destroy(t.gameObject);
                listButtons.Clear();listSliders.Clear();listValues.Clear();listedPlanes.Clear();listedPlanes.AddRange(planes.Take(shown));
                foreach(var plane in listedPlanes)
                {
                    var p=plane;
                    var b=Cfg2Ui.Button(listHost,CellName(p),0,0,54,26,()=>{SetActive(p);record?.Invoke("ProjectionActive",CellName(p));});var t=b.GetComponentInChildren<Text>();t.alignment=TextAnchor.MiddleCenter;t.fontSize=12;listButtons.Add(b);
                    listSliders.Add(Cfg2Ui.Slider(listHost,60,1,100,-180,180,(float)Angle(p.a,p.b),v=>{if(syncing)return;SetAngle(p,v);if(active!=p)active=p;UpdateTexts();graphic.SetVerticesDirty();},()=>record?.Invoke("ProjectionTurn","list "+CellName(p))));
                    var value=CfgUi.Label(listHost,"",0,0,40,20,12,Cfg2Ui.Gold);value.alignment=TextAnchor.MiddleRight;listValues.Add(value);
                }
                listMore.text=planes.Count>shown?"ほか "+(planes.Count-shown)+" 面（表から選べます）":"";
                Layout();
            }
            syncing=true;
            for(int i=0;i<listedPlanes.Count;i++)
            {
                double v=Angle(listedPlanes[i].a,listedPlanes[i].b);listSliders[i].value=(float)v;listValues[i].text=v.ToString("0",CultureInfo.InvariantCulture)+"°";
                MarkSelected(listButtons[i],listedPlanes[i]==active);
            }
            syncing=false;
        }
        void UpdateTexts()
        {
            if(countLabel==null)return;
            int total=directions.Count,lit=0;
            if(AnyChoice)foreach(var d in directions)if(Level(d)>0)lit++;
            HighlightedCount=AnyChoice?lit:0;
            string piece=string.IsNullOrEmpty(pieceName)?"":pieceName+"　";
            countLabel.text=piece+dimensions+"次元 → 3次元 透視投影　"+total.ToString("N0",CultureInfo.InvariantCulture)+"方向"+(AnyChoice?"　強調 "+lit.ToString("N0",CultureInfo.InvariantCulture)+" 本"+(lit>MaxLitRays?"（表示は"+MaxLitRays.ToString("N0",CultureInfo.InvariantCulture)+"本まで）":""):"")+(total>MaxFaintRays?"　（多いので選んだ方向だけ描画）":"");
            var parts=angles.Where(kv=>Math.Abs(kv.Value)>=.5).Select(kv=>CellName(kv.Key)+" "+kv.Value.ToString("0",CultureInfo.InvariantCulture)+"°").ToList();
            var chosen=selected.OrderBy(c=>c).Select(CellName).Concat(axisSelected.OrderBy(c=>c).Select(c=>(c.sign>0?"+":"−")+AxisName(c.axis))).ToList();
            rotationLabel.text="回転："+(parts.Count==0?"なし":string.Join("  ",parts))+"　ズーム "+Mathf.RoundToInt(zoom*100)+"%"+(chosen.Count>0?"　選択："+string.Join(" ",chosen):"");
            activeLabel.text=CellName(active)+" の回転　"+Angle(active.a,active.b).ToString("0",CultureInfo.InvariantCulture)+"°";
            zoomLabel.text=Mathf.RoundToInt(zoom*100)+"%";
            syncing=true;angleSlider.value=(float)Angle(active.a,active.b);zoomSlider.value=zoom;syncing=false;
            for(int i=0;i<axisButtons.Count;i++)MarkSelected(axisButtons[i],axisSelected.Contains((i/2,i%2==0?1:-1)));
            RefreshList();LayoutLabels();tableGraphic?.SetVerticesDirty();
        }
        static void MarkSelected(Button b,bool selected){b.GetComponent<Image>().color=selected?new Color(.47f,.36f,.12f,.95f):new Color(.05f,.07f,.1f,.95f);Cfg2Ui.SelectedFrame(b.transform,selected);}
    }
    internal sealed class Cfg2ProjectionGraphic : MaskableGraphic
    {
        public Cfg2ProjectionView View;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        // Ray colour by how many hidden axes (W, V, ...) the direction uses: none (visible in the plain section), one, two, more.
        static readonly Color[] Tier={new Color(.47f,.75f,1),new Color(1,.67f,.35f),new Color(.75f,.47f,1),new Color(.47f,.90f,.63f),new Color(1,.47f,.63f)};
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(View==null||View.Rotation==null)return;var r=rectTransform.rect;int n=View.Dimensions;
            Cfg2Glow.Gradient(vh,0,0,r.width,r.height,new Color(.55f,.65f,1,.06f),new Color(.08f,.11f,.25f,.02f));
            var origin=View.ToScreen(View.Project(new double[n],out _));
            // The three XYZ grids through the origin, projected like the rays so a hidden-axis turn bends them. The X–Z floor is the firmest.
            var a=new double[n];var b=new double[n];
            foreach(var (p,q,alpha) in new[]{(0,2,.11f),(0,1,.06f),(1,2,.06f)})
            {
                if(q>=n)continue;
                for(int i=-4;i<=4;i++)
                {
                    var color=new Color(.5f,.58f,.75f,i==0?alpha*2:alpha);
                    Array.Clear(a,0,n);Array.Clear(b,0,n);a[p]=i;a[q]=-4;b[p]=i;b[q]=4;
                    Cfg2Glow.Line(vh,View.ToScreen(View.Project(a,out _)),View.ToScreen(View.Project(b,out _)),color,1.2f);
                    Array.Clear(a,0,n);Array.Clear(b,0,n);a[q]=i;a[p]=-4;b[q]=i;b[p]=4;
                    Cfg2Glow.Line(vh,View.ToScreen(View.Project(a,out _)),View.ToScreen(View.Project(b,out _)),color,1.2f);
                }
            }
            // Rays. Faint ones first and as single segments; lit ones on top with a dot per reached cell.
            var dirs=View.Directions;int length=View.RayLength;bool many=dirs.Count>Cfg2ProjectionView.MaxFaintRays;bool choice=View.AnyChoice;
            var lit=new List<(int[] d,int level,float depth)>();int faint=0;
            var point=new double[n];
            foreach(var d in dirs)
            {
                int level=View.Level(d);
                if(level==0||(level==1&&choice==false&&many)){if(many)continue;if(faint++>Cfg2ProjectionView.MaxFaintRays)continue;}
                int hidden=0;for(int i=3;i<n;i++)if(d[i]!=0)hidden++;var tier=Tier[Mathf.Min(hidden,Tier.Length-1)];
                for(int i=0;i<n;i++)point[i]=d[i]*length;var end=View.Project(point,out float depth);
                if(level>0&&choice){lit.Add((d,level,depth));continue;}
                // Faint when a choice exists, plain when nothing is chosen.
                float fade=Mathf.Clamp(.9f+depth*1.6f,.3f,1);
                var color=choice?Cfg2Glow.Alpha(tier,.16f):Cfg2Glow.Alpha(tier,.55f*fade);
                Cfg2Glow.Line(vh,origin,View.ToScreen(end),color,choice?1.4f:1.6f+depth*3f);
            }
            lit.Sort((x,y)=>x.depth.CompareTo(y.depth));
            bool detailed=lit.Count<=Cfg2ProjectionView.MaxDetailedRays;int drawn=0;
            foreach(var entry in lit)
            {
                if(drawn++>=Cfg2ProjectionView.MaxLitRays)break;
                var d=entry.d;int hidden=0;for(int i=3;i<n;i++)if(d[i]!=0)hidden++;var tier=Tier[Mathf.Min(hidden,Tier.Length-1)];
                float fade=Mathf.Clamp(.9f+entry.depth*1.6f,.4f,1);var color=Cfg2Glow.Alpha(tier,fade);
                if(!detailed){for(int i=0;i<n;i++)point[i]=d[i]*length;var e=View.ToScreen(View.Project(point,out float dz));Cfg2Glow.Line(vh,origin,e,color,(entry.level==2?2.4f:1.6f)+dz*2f);continue;}
                Vector2 previous=origin;
                for(int step=1;step<=length;step++)
                {
                    for(int i=0;i<n;i++)point[i]=d[i]*step;var p=View.ToScreen(View.Project(point,out float depth));
                    float width=(entry.level==2?2.6f:1.8f)+depth*3f;
                    if(entry.level==2)Cfg2Glow.Neon(vh,previous,p,color,width*.6f,width*2.5f);else Cfg2Glow.Line(vh,previous,p,color,width);
                    Cfg2Glow.Blob(vh,p,(entry.level==2?4.5f:3.2f)+depth*4f,entry.level==2?new Color(1,1,1,.9f):color);
                    previous=p;
                }
            }
            // Axes as thin lines with a dot at the positive end, back to front; the negative half is dashed.
            var axes=new List<(int axis,Vector3 p)>();for(int axis=0;axis<n;axis++){var v=new double[n];v[axis]=Cfg2ProjectionView.AxisLength;axes.Add((axis,View.Project(v,out _)));}
            axes.Sort((x,y)=>y.p.z.CompareTo(x.p.z));
            foreach(var entry in axes)
            {
                var color=View.AxisColor(entry.axis);var end=View.ToScreen(entry.p);
                var back=new double[n];back[entry.axis]=-Cfg2ProjectionView.AxisLength*.6;
                Cfg2Glow.Dashed(vh,origin,View.ToScreen(View.Project(back,out _)),Cfg2Glow.Alpha(color,.35f),1.4f,3,3);
                Cfg2Glow.Neon(vh,origin,end,color,1.0f,5);
                Cfg2Glow.Blob(vh,end,4.5f,color);
            }
            Cfg2Glow.Blob(vh,origin,14,new Color(1,.86f,.55f,.75f));Cfg2Glow.Blob(vh,origin,5,new Color(1,.97f,.88f,1));
        }
    }
    // Rotation-plane table in the controller: upper triangle incl. the diagonal; the diagonal cell means "directions using this axis".
    internal sealed class Cfg2ProjectionTableGraphic : MaskableGraphic
    {
        public Cfg2ProjectionView View;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        static readonly Color SelectedCell=new Color(.47f,.36f,.12f,.95f),SelectedEdge=new Color(.91f,.79f,.52f),HoverCell=new Color(.11f,.16f,.22f,.95f),HoverEdge=new Color(.67f,.78f,.94f),PlainCell=new Color(.03f,.045f,.07f,.9f),PlainEdge=new Color(.24f,.28f,.35f),ActiveEdge=new Color(.40f,.60f,.93f);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(View==null)return;int n=View.Dimensions;float cs=View.CellSize;
            Cfg2Glow.Halo(vh,-4,4,n*cs+8,n*cs+8,6,new Color(.02f,.03f,.05f,.78f));
            for(int row=0;row<n;row++)for(int column=row;column<n;column++)
            {
                var cell=(row,column);var rect=View.CellRect(row,column);
                bool isSelected=View.Selected.Contains(cell),isHovered=View.Hovered.HasValue&&View.Hovered.Value==cell,isActive=row!=column&&View.Active==cell,rotating=View.Dragging.HasValue&&View.Dragging.Value==cell;
                var fill=isSelected?SelectedCell:isHovered?HoverCell:PlainCell;var edge=isHovered?HoverEdge:isSelected?SelectedEdge:PlainEdge;
                float x=rect.x+1.5f,y=rect.y+rect.height-1.5f,w=rect.width-3,h=rect.height-3;
                Cfg2Glow.Rect(vh,x,y,w,h,fill);
                Cfg2Glow.Line(vh,new Vector2(x,y),new Vector2(x+w,y),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x+w,y),new Vector2(x+w,y-h),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x+w,y-h),new Vector2(x,y-h),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x,y-h),new Vector2(x,y),edge,1.2f);
                if(isActive||rotating){float t=rotating?2.5f:2;Cfg2Glow.Line(vh,new Vector2(x-2,y+2),new Vector2(x+w+2,y+2),ActiveEdge,t);Cfg2Glow.Line(vh,new Vector2(x+w+2,y+2),new Vector2(x+w+2,y-h-2),ActiveEdge,t);Cfg2Glow.Line(vh,new Vector2(x+w+2,y-h-2),new Vector2(x-2,y-h-2),ActiveEdge,t);Cfg2Glow.Line(vh,new Vector2(x-2,y-h-2),new Vector2(x-2,y+2),ActiveEdge,t);}
                var ca=View.AxisColor(row);var cb=View.AxisColor(column);float inset=cs*.26f;var mid=new Vector2(x+w/2,y-h/2);float bar=cs>=16?2.2f:1.5f;
                if(row==column)Cfg2Glow.Line(vh,new Vector2(mid.x,y-inset),new Vector2(mid.x,y-h+inset),ca,bar);
                else{Cfg2Glow.Line(vh,new Vector2(x+inset,mid.y),new Vector2(x+w-inset,mid.y),ca,bar);Cfg2Glow.Line(vh,new Vector2(mid.x,y-inset),new Vector2(mid.x,y-h+inset),cb,bar);}
                if(isSelected)Cfg2Glow.Blob(vh,new Vector2(x+w-4,y-4),cs>=16?3.5f:2.5f,SelectedEdge);
            }
        }
    }
}
