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
    // N-dimensional board down to three dimensions (one hidden axis at a time) and then to the screen. All rays are faint until a
    // cell of the rotation-plane table (one cell per pair of axes) is hovered or clicked; a drag in a cell or on an axis knob turns
    // the whole picture in that plane, so the hidden axes come out of the origin and the depth of W, V, ... can be seen.
    internal sealed class Cfg2ProjectionView : MonoBehaviour
    {
        Cfg2ProjectionGraphic graphic;InputOwnership ownership;Func<float> uiScale;Action<string,string> record;
        readonly List<Text> axisLabels=new List<Text>();readonly List<Text> rowLabels=new List<Text>(),columnLabels=new List<Text>();
        Text countLabel,rotationLabel,hint;Button playButton,stopButton,resetButton,clearButton;
        // Board and piece shown.
        int dimensions=4;string pieceName="",refreshKey;
        // Directions in UI axis order (X Y Z W V ...), one int[dimensions] each, and the ray length in cells.
        readonly List<int[]> directions=new List<int[]>();int rayLength=1;
        // N×N rotation, UI axis order. Angles per plane are only remembered for the status line.
        double[,] rotation;readonly Dictionary<(int,int),double> angles=new Dictionary<(int,int),double>();
        // 3-D camera around the projected picture.
        float yaw=-35,pitch=22;
        // Table state: hovered cell, selected cells (a,b with a<=b; a==b means "uses axis a"), the plane being dragged.
        (int a,int b)? hovered,dragging;readonly HashSet<(int a,int b)> selected=new HashSet<(int a,int b)>();
        bool spinning;float spinPhase;
        // Pointer gesture.
        int pointer=-1;Vector2 downPosition,lastPosition;bool pressed,moved;int knobAxis=-1;bool orbiting;
        // UI meshes hold at most 65,000 vertices: faint rays are one quad each, lit rays get steps and glow only while few.
        internal const int MaxFaintRays=6000,MaxLitRays=3000,MaxDetailedRays=700;
        internal int DirectionCount=>directions.Count;
        internal int HighlightedCount{get;private set;}
        internal IReadOnlyCollection<(int a,int b)> Selected=>selected;
        internal (int a,int b)? Hovered=>hovered;
        internal bool Spinning=>spinning;
        internal double[,] Rotation=>rotation;
        internal void Initialize(InputOwnership input,Func<float> scale,Action<string,string> recorder)
        {
            ownership=input;uiScale=scale;record=recorder;
            Cfg2Ui.ClearOwned(transform,true);axisLabels.Clear();rowLabels.Clear();columnLabels.Clear();
            var rect=CfgUi.Rect(transform,"Projected directions",0,0,1,1);CfgUi.Fill(rect);
            graphic=rect.gameObject.AddComponent<Cfg2ProjectionGraphic>();graphic.View=this;graphic.color=Color.white;graphic.raycastTarget=true;
            var surface=rect.gameObject.AddComponent<CfgPointerSurface>();surface.Down=OnDown;surface.Drag=OnDrag;surface.Up=OnUp;
            float w=((RectTransform)transform).rect.width,h=((RectTransform)transform).rect.height;
            countLabel=CfgUi.Label(transform,"",10,6,w-20,22,14,Cfg2Ui.Gold);
            rotationLabel=CfgUi.Label(transform,"",10,28,w-20,20,12,new Color(.75f,.80f,.86f));
            hint=CfgUi.Label(transform,"表のマス：オーバーで強調、クリックで確定（複数可）、ドラッグで回転。軸の丸をドラッグでも回転。右クリックで解除。",10,h-22,w-20,20,11,new Color(.62f,.68f,.76f));
            playButton=Cfg2Ui.Button(transform,"▶ 回す",10,h-56,78,28,()=>SetSpinning(true));
            stopButton=Cfg2Ui.Button(transform,"■ 止める",94,h-56,86,28,()=>SetSpinning(false));
            resetButton=Cfg2Ui.Button(transform,"初期位置",186,h-56,80,28,()=>ResetRotation());
            clearButton=Cfg2Ui.Button(transform,"選択をクリア",272,h-56,110,28,ClearSelection);
            foreach(var b in new[]{playButton,stopButton,resetButton,clearButton}){var t=b.GetComponentInChildren<Text>();t.alignment=TextAnchor.MiddleCenter;t.fontSize=13;}
            ResetRotation(false);
        }
        // Rebuilds the direction list for the piece. Allowed unit directions of {-1,0,1}^N are enumerated like the preview query does.
        internal void Refresh(PreviewPreset preset,string definitionId,string displayName)
        {
            int n=preset?.Bounds.Dimensions??4;var definition=preset?.Find(definitionId);
            // The list only changes with the rules or the piece; a view change (the usual reason for a refresh) keeps it.
            string key=(preset?.RuleHash??"")+"/"+definitionId+"/"+n;
            if(key==refreshKey&&pieceName==displayName)return;refreshKey=key;
            if(n!=dimensions){dimensions=n;ResetRotation(false);selected.RemoveWhere(c=>c.a>=n||c.b>=n);hovered=null;}
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
            EnsureLabels();LayoutLabels();UpdateTexts();graphic.SetVerticesDirty();
        }
        static int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}
        internal void ResetRotation(bool notify=true)
        {
            rotation=new double[dimensions,dimensions];for(int i=0;i<dimensions;i++)rotation[i,i]=1;angles.Clear();
            // The hidden axes start a little way out of the origin so that W, V, ... can be grabbed at once.
            for(int a=3;a<dimensions;a++){Turn(a%3,a,a==3?32:a==4?26:14+6*(a-5));if(a>=4)Turn((a+1)%3,a,10);}
            yaw=-35;pitch=22;spinPhase=0;
            if(notify){record?.Invoke("ProjectionReset","");UpdateTexts();graphic?.SetVerticesDirty();}
        }
        // Rotation in the plane of UI axes a and b by degrees; composed on the left so it acts on the current picture.
        internal void Turn(int a,int b,double degrees)
        {
            if(a==b||a<0||b<0||a>=dimensions||b>=dimensions||degrees==0)return;
            double t=degrees*Math.PI/180,c=Math.Cos(t),s=Math.Sin(t);
            for(int j=0;j<dimensions;j++){double ra=rotation[a,j],rb=rotation[b,j];rotation[a,j]=c*ra-s*rb;rotation[b,j]=s*ra+c*rb;}
            var key=a<b?(a,b):(b,a);angles.TryGetValue(key,out double sum);angles[key]=(sum+(a<b?degrees:-degrees)+180)%360-180;
        }
        // Projection of a point in UI axis order: N → 3 by one perspective division per hidden axis (camera at +Distance on it),
        // then the fixed 3-D camera. depth sums the hidden coordinates so nearer rays can be drawn brighter and thicker.
        internal const double HiddenCamera=16,Camera3=16;
        internal Vector3 Project(double[] point,out float depth)
        {
            var p=new double[dimensions];for(int i=0;i<dimensions;i++){double v=0;for(int j=0;j<dimensions;j++)v+=rotation[i,j]*point[j];p[i]=v;}
            double d=0;
            for(int a=dimensions-1;a>=3;a--){double s=HiddenCamera/Math.Max(.5,HiddenCamera-p[a]);d+=p[a]/HiddenCamera;for(int i=0;i<a;i++)p[i]*=s;}
            double x=p[0],y=dimensions>1?p[1]:0,z=dimensions>2?p[2]:0;
            double cy=Math.Cos(yaw*Math.PI/180),sy=Math.Sin(yaw*Math.PI/180),cp=Math.Cos(pitch*Math.PI/180),sp=Math.Sin(pitch*Math.PI/180);
            double x1=x*cy-z*sy,z1=x*sy+z*cy,y2=y*cp-z1*sp,z2=y*sp+z1*cp;
            double s3=Camera3/Math.Max(.5,Camera3-z2);depth=(float)d;
            return new Vector3((float)(x1*s3),(float)(y2*s3),(float)z2);
        }
        internal float Scale=>Mathf.Min(graphic.rectTransform.rect.width,graphic.rectTransform.rect.height)/17f;
        internal Vector2 Center=>new Vector2(graphic.rectTransform.rect.width*.5f,-graphic.rectTransform.rect.height*.5f-10);
        internal Vector2 ToScreen(Vector3 projected)=>Center+new Vector2(projected.x,projected.y)*Scale;
        internal int RayLength=>rayLength;
        internal IReadOnlyList<int[]> Directions=>directions;
        internal int Dimensions=>dimensions;
        // Highlight level of a direction: 2 selected, 1 hovered, 0 faint; 1 for every ray while nothing is chosen.
        internal int Level(int[] d)
        {
            if(selected.Count==0&&!hovered.HasValue)return 1;
            foreach(var c in selected)if(d[c.a]!=0&&d[c.b]!=0)return 2;
            if(hovered.HasValue&&d[hovered.Value.a]!=0&&d[hovered.Value.b]!=0)return 1;
            return 0;
        }
        internal bool AnyChoice=>selected.Count>0||hovered.HasValue;
        internal (int a,int b)? Dragging=>dragging;
        // ---- table geometry (bottom-right) ----
        internal float CellSize=>dimensions<=5?30:dimensions<=8?22:16;
        internal Vector2 TableOrigin{get{var r=graphic.rectTransform.rect;float cs=CellSize;return new Vector2(r.width-12-dimensions*cs,-(r.height-70-dimensions*cs));}}
        internal Rect CellRect(int row,int column){var o=TableOrigin;float cs=CellSize;return new Rect(o.x+column*cs,o.y-row*cs-cs,cs,cs);}
        bool TryCell(Vector2 local,out (int a,int b) cell)
        {
            cell=default;var o=TableOrigin;float cs=CellSize;float x=local.x-o.x,y=o.y-local.y;
            if(x<0||y<0||x>=cs*dimensions||y>=cs*dimensions)return false;
            int column=(int)(x/cs),row=(int)(y/cs);if(column<row)return false;cell=(row,column);return true;
        }
        // ---- axis knobs ----
        internal Vector2 KnobPosition(int axis)
        {var p=new double[dimensions];p[axis]=AxisLength;return ToScreen(Project(p,out _));}
        internal const double AxisLength=5;
        int KnobAt(Vector2 local){for(int a=0;a<dimensions;a++)if((KnobPosition(a)-local).sqrMagnitude<14*14)return a;return -1;}
        // The shown axis whose screen direction best matches a mouse delta; never the dragged axis itself.
        int PartnerAxis(int axis,Vector2 delta)
        {
            int best=-1;float score=-1;var o=ToScreen(Project(new double[dimensions],out _));
            for(int a=0;a<Math.Min(3,dimensions);a++){if(a==axis)continue;var dir=(KnobPosition(a)-o);if(dir.sqrMagnitude<1)continue;float s=Mathf.Abs(Vector2.Dot(dir.normalized,delta.normalized));if(s>score){score=s;best=a;}}
            if(best<0)best=axis==0?1:0;return best;
        }
        bool Local(PointerEventData e,out Vector2 local)=>RectTransformUtility.ScreenPointToLocalPointInRectangle(graphic.rectTransform,e.position,e.pressEventCamera,out local);
        void OnDown(PointerEventData e)
        {
            if(pressed||ownership.Owner!=InputOwner.None||CfgUi.EditingText)return;if(!Local(e,out var local))return;
            if(e.button==PointerEventData.InputButton.Right){ClearSelection();return;}
            if(e.button!=PointerEventData.InputButton.Left)return;
            if(!ownership.TryBegin(InputOwner.Rotation4D,e.pointerId,()=>{pressed=false;dragging=null;knobAxis=-1;orbiting=false;}))return;
            pressed=true;moved=false;pointer=e.pointerId;downPosition=lastPosition=e.position;knobAxis=KnobAt(local);orbiting=false;dragging=null;
            if(knobAxis<0&&TryCell(local,out var cell))dragging=cell;
            else if(knobAxis<0)orbiting=true;
        }
        void OnDrag(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer||!ownership.IsOwner(InputOwner.Rotation4D,pointer))return;
            float scale=Mathf.Max(.01f,uiScale?.Invoke()??1);var delta=(e.position-lastPosition)/scale;lastPosition=e.position;
            if(!moved&&(e.position-downPosition).magnitude/scale<5)return;moved=true;
            if(knobAxis>=0){int partner=PartnerAxis(knobAxis,delta);var o=ToScreen(Project(new double[dimensions],out _));var dir=(KnobPosition(partner)-o).normalized;Turn(partner,knobAxis,Vector2.Dot(delta,dir)*.6f);}
            else if(dragging.HasValue&&dragging.Value.a!=dragging.Value.b)Turn(dragging.Value.a,dragging.Value.b,delta.x*.6f);
            else if(dragging.HasValue){int other=dragging.Value.a==0?1:0;Turn(other,dragging.Value.a,delta.x*.6f);}
            else if(orbiting){yaw+=delta.x*.4f;pitch=Mathf.Clamp(pitch-delta.y*.4f,-80,80);}
            UpdateTexts();graphic.SetVerticesDirty();
        }
        void OnUp(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer)return;pressed=false;
            bool owned=ownership.IsOwner(InputOwner.Rotation4D,pointer);if(owned)ownership.Release(InputOwner.Rotation4D,pointer);
            if(!moved&&dragging.HasValue){var cell=dragging.Value;if(!selected.Remove(cell))selected.Add(cell);record?.Invoke("ProjectionSelect",CellName(cell)+"="+selected.Contains(cell));}
            else if(moved)record?.Invoke("ProjectionTurn",knobAxis>=0?"axis "+AxisName(knobAxis):dragging.HasValue?CellName(dragging.Value):"orbit");
            dragging=null;knobAxis=-1;orbiting=false;UpdateTexts();graphic.SetVerticesDirty();
        }
        void Update()
        {
            if(pressed&&!ownership.IsOwner(InputOwner.Rotation4D,pointer)){pressed=false;dragging=null;knobAxis=-1;orbiting=false;}
            // Hover: the cell under the mouse while no gesture is running.
            (int a,int b)? over=null;
            if(!pressed&&RectTransformUtility.ScreenPointToLocalPointInRectangle(graphic.rectTransform,Input.mousePosition,null,out var local)&&TryCell(local,out var cell))over=cell;
            bool changed=!Nullable.Equals(over,hovered);hovered=over;
            if(spinning)
            {
                // Each hidden axis turns slowly against a shown axis, at different rates so the picture keeps changing.
                float dt=Time.unscaledDeltaTime;spinPhase+=dt;
                for(int a=3;a<dimensions;a++){Turn(a%3,a,dt*(10+3*(a-3)));if(dimensions>4)Turn((a+1)%3,a,dt*4*Mathf.Sin(spinPhase*.3f+a));}
                changed=true;
            }
            if(changed){UpdateTexts();graphic.SetVerticesDirty();}
        }
        void OnDisable(){if(pressed&&ownership!=null&&ownership.IsOwner(InputOwner.Rotation4D,pointer))ownership.Cancel();pressed=false;dragging=null;knobAxis=-1;orbiting=false;hovered=null;}
        void SetSpinning(bool on){spinning=on;record?.Invoke("ProjectionSpin",on.ToString());UpdateTexts();}
        void ClearSelection(){if(selected.Count==0)return;selected.Clear();record?.Invoke("ProjectionSelect","clear");UpdateTexts();graphic.SetVerticesDirty();}
        internal void SetSelected(IEnumerable<(int a,int b)> cells){selected.Clear();foreach(var c in cells)if(c.a<=c.b&&c.b<dimensions)selected.Add(c);UpdateTexts();graphic?.SetVerticesDirty();}
        internal string AxisName(int uiAxis)=>SliceAxes.AxisName(SliceAxes.Axes(Math.Max(dimensions,uiAxis+1))[uiAxis]);
        string CellName((int a,int b) c)=>c.a==c.b?AxisName(c.a):AxisName(c.a)+"-"+AxisName(c.b);
        void EnsureLabels()
        {
            while(axisLabels.Count<dimensions){var t=CfgUi.Label(transform,"",0,0,30,24,16);Cfg2Glow.GlowText(t,Color.white);axisLabels.Add(t);}
            while(rowLabels.Count<dimensions){rowLabels.Add(CfgUi.Label(transform,"",0,0,20,20,11));columnLabels.Add(CfgUi.Label(transform,"",0,0,20,20,11));}
            for(int i=0;i<axisLabels.Count;i++){bool on=i<dimensions;axisLabels[i].gameObject.SetActive(on);rowLabels[i].gameObject.SetActive(on);columnLabels[i].gameObject.SetActive(on);
                if(!on)continue;var color=Cfg2Projection.AxisColor(SliceAxes.Axes(dimensions)[i]);string name=AxisName(i);
                axisLabels[i].text=name;axisLabels[i].color=color;axisLabels[i].GetComponent<Outline>().effectColor=Cfg2Glow.Alpha(color,.45f);
                rowLabels[i].text=name;rowLabels[i].color=color;columnLabels[i].text=name;columnLabels[i].color=color;
                rowLabels[i].fontSize=columnLabels[i].fontSize=dimensions<=5?12:dimensions<=8?10:9;rowLabels[i].alignment=TextAnchor.MiddleRight;columnLabels[i].alignment=TextAnchor.MiddleCenter;}
        }
        // Axis labels sit beside their knobs; row and column labels frame the table.
        internal void LayoutLabels()
        {
            if(graphic==null)return;float cs=CellSize;var o=TableOrigin;
            for(int i=0;i<dimensions;i++)
            {
                var k=KnobPosition(i);var r=axisLabels[i].rectTransform;r.anchoredPosition=new Vector2(k.x+12,k.y+12);
                rowLabels[i].rectTransform.anchoredPosition=new Vector2(o.x-22,o.y-i*cs);rowLabels[i].rectTransform.sizeDelta=new Vector2(20,cs);
                columnLabels[i].rectTransform.anchoredPosition=new Vector2(o.x+i*cs,o.y-dimensions*cs);columnLabels[i].rectTransform.sizeDelta=new Vector2(cs,18);
            }
        }
        void UpdateTexts()
        {
            if(countLabel==null)return;
            int total=directions.Count,lit=0;
            if(AnyChoice)foreach(var d in directions)if(Level(d)>0)lit++;
            HighlightedCount=AnyChoice?lit:0;
            string piece=string.IsNullOrEmpty(pieceName)?"":pieceName+"　";
            countLabel.text=piece+dimensions+"次元 → 3次元 透視投影　"+total.ToString("N0",CultureInfo.InvariantCulture)+"方向"+(AnyChoice?"　強調 "+lit.ToString("N0",CultureInfo.InvariantCulture)+" 本"+(lit>MaxLitRays?"（表示は"+MaxLitRays.ToString("N0",CultureInfo.InvariantCulture)+"本まで）":""):"")+(total>MaxFaintRays?"　（多いので選んだ方向だけ描画）":"");
            var parts=angles.Where(kv=>Math.Abs(kv.Value)>=.5).OrderBy(kv=>kv.Key).Select(kv=>CellName(kv.Key)+" "+kv.Value.ToString("0",CultureInfo.InvariantCulture)+"°").ToList();
            rotationLabel.text="回転："+(parts.Count==0?"なし":string.Join("  ",parts))+(spinning?"　（自動回転中）":"")+(selected.Count>0?"　選択："+string.Join(" ",selected.OrderBy(c=>c).Select(CellName)):"");
            if(playButton!=null){MarkSelected(playButton,spinning);MarkSelected(stopButton,!spinning);}
            LayoutLabels();
        }
        static void MarkSelected(Button b,bool selected){b.GetComponent<Image>().color=selected?Cfg2Ui.Blue:new Color(.05f,.07f,.1f,.95f);Cfg2Ui.SelectedFrame(b.transform,selected);}
    }
    internal sealed class Cfg2ProjectionGraphic : Graphic
    {
        public Cfg2ProjectionView View;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        // Ray colour by how many hidden axes (W, V, ...) the direction uses: none (visible in the plain section), one, two, more.
        static readonly Color[] Tier={new Color(.47f,.75f,1),new Color(1,.67f,.35f),new Color(.75f,.47f,1),new Color(.47f,.90f,.63f),new Color(1,.47f,.63f)};
        static readonly Color SelectedCell=new Color(.47f,.36f,.12f,.95f),SelectedEdge=new Color(.91f,.79f,.52f),HoverCell=new Color(.11f,.16f,.22f,.95f),HoverEdge=new Color(.67f,.78f,.94f),PlainCell=new Color(.03f,.045f,.07f,.9f),PlainEdge=new Color(.24f,.28f,.35f),RotatingEdge=new Color(.40f,.60f,.93f);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(View==null||View.Rotation==null)return;var r=rectTransform.rect;int n=View.Dimensions;
            Cfg2Glow.Gradient(vh,0,0,r.width,r.height,new Color(.55f,.65f,1,.06f),new Color(.08f,.11f,.25f,.02f));
            var origin=View.ToScreen(View.Project(new double[n],out _));
            // Floor grid of the shown X–Z plane for footing.
            for(int i=-4;i<=4;i++)
            {
                var a=new double[n];var b=new double[n];a[0]=i;a[2]=-4;b[0]=i;b[2]=4;
                Cfg2Glow.Line(vh,View.ToScreen(View.Project(a,out _)),View.ToScreen(View.Project(b,out _)),new Color(.5f,.58f,.75f,i==0?.22f:.10f),1.2f);
                a[0]=-4;a[2]=i;b[0]=4;b[2]=i;
                Cfg2Glow.Line(vh,View.ToScreen(View.Project(a,out _)),View.ToScreen(View.Project(b,out _)),new Color(.5f,.58f,.75f,i==0?.22f:.10f),1.2f);
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
            // Axes with knobs, back to front.
            var axes=new List<(int axis,Vector3 p)>();for(int a=0;a<n;a++){var v=new double[n];v[a]=Cfg2ProjectionView.AxisLength;axes.Add((a,View.Project(v,out _)));}
            axes.Sort((x,y)=>y.p.z.CompareTo(x.p.z));
            foreach(var entry in axes)
            {
                var color=Cfg2Projection.AxisColor(SliceAxes.Axes(n)[entry.axis]);var end=View.ToScreen(entry.p);
                var back=new double[n];back[entry.axis]=-Cfg2ProjectionView.AxisLength*.6;
                Cfg2Glow.Dashed(vh,origin,View.ToScreen(View.Project(back,out _)),Cfg2Glow.Alpha(color,.35f),1.6f,3,3);
                Cfg2Glow.Neon(vh,origin,end,color,1.2f,7);
                bool dragged=View.Dragging.HasValue&&(View.Dragging.Value.a==entry.axis||View.Dragging.Value.b==entry.axis);
                Cfg2Glow.Blob(vh,end,dragged?15:11,Cfg2Glow.Alpha(color,.55f));Cfg2Glow.Circle(vh,end,8,color,1.2f,5,24);Cfg2Glow.Blob(vh,end,5,new Color(1,1,1,.9f));
            }
            Cfg2Glow.Blob(vh,origin,14,new Color(1,.86f,.55f,.75f));Cfg2Glow.Blob(vh,origin,5,new Color(1,.97f,.88f,1));
            // Rotation-plane table: upper triangle incl. the diagonal; the diagonal cell means "directions using this axis".
            float cs=View.CellSize;var o=View.TableOrigin;
            Cfg2Glow.Halo(vh,o.x-26,o.y+22,n*cs+38,n*cs+44,6,new Color(.02f,.03f,.05f,.78f));
            for(int row=0;row<n;row++)for(int column=row;column<n;column++)
            {
                var cell=(row,column);var rect=View.CellRect(row,column);bool isSelected=View.Selected.Contains(cell),isHovered=View.Hovered.HasValue&&View.Hovered.Value==cell,rotating=View.Dragging.HasValue&&View.Dragging.Value==cell;
                var fill=isSelected?SelectedCell:isHovered?HoverCell:PlainCell;var edge=isHovered?HoverEdge:isSelected?SelectedEdge:PlainEdge;
                float x=rect.x+1.5f,y=rect.y+rect.height-1.5f,w=rect.width-3,h=rect.height-3;
                Cfg2Glow.Rect(vh,x,y,w,h,fill);
                Cfg2Glow.Line(vh,new Vector2(x,y),new Vector2(x+w,y),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x+w,y),new Vector2(x+w,y-h),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x+w,y-h),new Vector2(x,y-h),edge,1.2f);Cfg2Glow.Line(vh,new Vector2(x,y-h),new Vector2(x,y),edge,1.2f);
                if(rotating){Cfg2Glow.Line(vh,new Vector2(x-2,y+2),new Vector2(x+w+2,y+2),RotatingEdge,2);Cfg2Glow.Line(vh,new Vector2(x+w+2,y+2),new Vector2(x+w+2,y-h-2),RotatingEdge,2);Cfg2Glow.Line(vh,new Vector2(x+w+2,y-h-2),new Vector2(x-2,y-h-2),RotatingEdge,2);Cfg2Glow.Line(vh,new Vector2(x-2,y-h-2),new Vector2(x-2,y+2),RotatingEdge,2);}
                var ca=Cfg2Projection.AxisColor(SliceAxes.Axes(n)[row]);var cb=Cfg2Projection.AxisColor(SliceAxes.Axes(n)[column]);float inset=cs*.26f;var mid=new Vector2(x+w/2,y-h/2);
                if(row==column)Cfg2Glow.Line(vh,new Vector2(mid.x,y-inset),new Vector2(mid.x,y-h+inset),ca,2.2f);
                else{Cfg2Glow.Line(vh,new Vector2(x+inset,mid.y),new Vector2(x+w-inset,mid.y),ca,2.2f);Cfg2Glow.Line(vh,new Vector2(mid.x,y-inset),new Vector2(mid.x,y-h+inset),cb,2.2f);}
                if(isSelected)Cfg2Glow.Blob(vh,new Vector2(x+w-4,y-4),3.5f,SelectedEdge);
            }
        }
    }
}
