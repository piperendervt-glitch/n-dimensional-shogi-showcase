using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // CFG2 uses the live camera for every axis. CFG1's fixed illustrative graphic is unchanged.
    internal static class Cfg2Projection
    {
        internal static Vector2 Project(Vec3 local,Quaternion cameraRotation,float scale)
        {var p=Quaternion.Inverse(cameraRotation)*SectionView.MapVector(local);return new Vector2(p.x,p.y)*scale;}
        internal static Vec3 Tangent(SliceFrame f,VecN world)
            =>new Vec3(VecN.Dot(world,f.AxisX),VecN.Dot(world,f.AxisY),VecN.Dot(world,f.AxisZ));
        // Neon palette: X red, Y amber, Z cyan, W violet; then V green, U orange, T pink, S lime, R teal, Q magenta, P silver.
        static readonly Color[] AxisColors={new Color(1,.36f,.30f),new Color(1,.78f,.26f),new Color(.30f,.78f,1),new Color(.68f,.52f,1),
            new Color(.31f,.88f,.54f),new Color(1,.60f,.18f),new Color(1,.45f,.75f),new Color(.80f,.95f,.35f),new Color(.20f,.85f,.80f),new Color(.95f,.40f,1),new Color(.85f,.85f,.85f)};
        internal static Color AxisColor(LogicalAxis a)=>AxisColors[Mathf.Clamp((int)a,0,AxisColors.Length-1)];
        internal static Color AxisColor(int coreAxis)=>AxisColor(SliceAxes.AxisOfCore(coreAxis));
        // Camera-space depth of a chart direction; positive is behind the diagram's centre.
        internal static float Depth(Vec3 local,Quaternion cameraRotation)=>(Quaternion.Inverse(cameraRotation)*SectionView.MapVector(local)).z;
    }
    internal static class Cfg2MeshUi
    {
        internal static void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int i=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
        internal static void Line(VertexHelper vh,Vector2 a,Vector2 b,Color color,float width)
        {if((b-a).sqrMagnitude<1e-8f)return;var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Triangle(vh,a-n,a+n,b+n,color);Triangle(vh,a-n,b+n,b-n,color);}
        internal static void Circle(VertexHelper vh,Vector2 center,float radius,Color color,float width)
        {for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Line(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,color,width);}}
        internal static void Arrow(VertexHelper vh,Vector2 from,Vector2 to,Color color)
        {if((to-from).magnitude<2)return;Line(vh,from,to,new Color(0,0,0,.75f),4.4f);Line(vh,from,to,color,2.4f);var d=(to-from).normalized;var n=new Vector2(-d.y,d.x);Triangle(vh,to,to-d*8+n*4,to-d*8-n*4,color);}
    }
    // Press, drag and release of one world-axis handle, shared by the XYZW gizmo and the hyperplane panels.
    // A press without movement is a click; a drag moves C along the axis (snapping while Shift or Ctrl is held).
    internal sealed class Cfg2AxisDragHandle
    {
        readonly Func<SliceAnimator> currentSlice;readonly InputOwnership ownership;readonly Func<float> uiScale;readonly Action changed;readonly Action<LogicalAxis> clicked;
        AxisDrag drag;Vector2 down;int pointer;bool pressed,moved;LogicalAxis pressedAxis;float dragScale=1;
        internal Cfg2AxisDragHandle(Func<SliceAnimator> currentSlice,InputOwnership ownership,Func<float> uiScale,Action changed,Action<LogicalAxis> clicked)
        {this.currentSlice=currentSlice;this.ownership=ownership;this.uiScale=uiScale;this.changed=changed;this.clicked=clicked;}
        internal void Down(LogicalAxis axis,PointerEventData e,Vector2 screenDirection)
        {
            if(e.button!=PointerEventData.InputButton.Left||ownership.Owner!=InputOwner.None||pressed)return;
            if(CfgUi.EditingText)return;
            down=e.position;pointer=e.pointerId;pressedAxis=axis;pressed=true;moved=false;dragScale=Mathf.Max(.01f,uiScale?.Invoke()??1);
            // Always the live slice: a match or settings switch replaces it.
            drag=new AxisDrag(currentSlice(),ownership);
            // Endpoints are the ones rendered this frame; Canvas pixels are used for both length and displacement.
            if(!drag.Begin(axis,pointer,e.position.x/dragScale,e.position.y/dragScale,screenDirection.x,screenDirection.y))
                if(!ownership.TryBegin(InputOwner.Axis,pointer,()=>pressed=false))pressed=false;
        }
        internal void Drag(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer||!ownership.IsOwner(InputOwner.Axis,pointer))return;moved|=(e.position-down).magnitude/dragScale>6;
            if(moved&&drag.Update(pointer,e.position.x/dragScale,e.position.y/dragScale,Cfg2Ui.SnapHeld))changed?.Invoke();
        }
        internal void Up(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer)return;pressed=false;if(!ownership.IsOwner(InputOwner.Axis,pointer))return;
            if(drag.IsActive)drag.Complete(pointer);else ownership.Release(InputOwner.Axis,pointer);
            if(!moved)clicked(pressedAxis);changed?.Invoke();
        }
        // Another owner (Esc, focus loss, resize) took the input.
        internal void Update(){if(pressed&&!ownership.IsOwner(InputOwner.Axis,pointer)){pressed=false;changed?.Invoke();}}
        internal void Cancel(bool notify=true){drag?.Cancel();if(pressed&&ownership.IsOwner(InputOwner.Axis,pointer))ownership.Cancel();pressed=false;if(notify)changed?.Invoke();}
    }
    internal sealed class Cfg2AxisGizmo : MonoBehaviour
    {
        Cfg2GizmoGraphic graphic;readonly Dictionary<LogicalAxis,Button> buttons=new Dictionary<LogicalAxis,Button>();
        internal IReadOnlyDictionary<LogicalAxis,Button> Buttons=>buttons;
        internal long ViewRevision {get;private set;}
        internal long CameraRevision {get;private set;}
        readonly Dictionary<LogicalAxis,Vector2> directions=new Dictionary<LogicalAxis,Vector2>();
        Cfg2AxisDragHandle handle;
        internal void Initialize(Action<LogicalAxis> selected,Func<SliceAnimator> currentSlice,InputOwnership ownership,Action changed,Func<float> scale)
        {
            handle=new Cfg2AxisDragHandle(currentSlice,ownership,scale,changed,selected);
            // The globe carries its own light, so the host panel keeps only a faint tint and no frame.
            var host=GetComponent<Image>();if(host!=null)host.color=new Color(.01f,.02f,.05f,.10f);
            foreach(var frame in GetComponentsInChildren<Cfg2PanelBorder>())frame.enabled=false;
            var r=CfgUi.Rect(transform,"Camera-linked XYZW axes",0,0,1,1);CfgUi.Fill(r);graphic=r.gameObject.AddComponent<Cfg2GizmoGraphic>();graphic.raycastTarget=false;
            foreach(LogicalAxis axis in Enum.GetValues(typeof(LogicalAxis)))
            {var captured=axis;var b=Cfg2Ui.Button(transform,axis.ToString(),0,0,48,27,()=>{});var image=b.GetComponent<Image>();image.color=new Color(0,0,0,0);b.GetComponentInChildren<Cfg2PanelBorder>().enabled=false;
                var t=b.GetComponentInChildren<Text>();t.alignment=TextAnchor.MiddleCenter;t.fontSize=18;t.color=Cfg2Projection.AxisColor(axis);t.rectTransform.anchoredPosition=new Vector2(0,-1);t.rectTransform.sizeDelta=new Vector2(48,25);Cfg2Glow.GlowText(t,t.color);b.targetGraphic=t;buttons.Add(axis,b);
                var surface=b.gameObject.AddComponent<CfgPointerSurface>();surface.Down=e=>handle.Down(captured,e,AxisScreenDirection(captured));surface.Drag=handle.Drag;surface.Up=handle.Up;}
        }
        internal Vector2 AxisScreenDirection(LogicalAxis axis)=>directions.TryGetValue(axis,out var value)?value:Vector2.zero;
        internal bool IsPointerOver(Vector2 point)=>gameObject.activeInHierarchy&&RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform,point,null);
        void Update()=>handle?.Update();
        void OnDisable()=>handle?.Cancel(false);
        internal void Refresh(SliceFrame frame,Quaternion rotation,long viewRevision,long cameraRevision)
        {
            ViewRevision=viewRevision;CameraRevision=cameraRevision;graphic.Frame=frame;graphic.CameraRotation=rotation;graphic.SetVerticesDirty();
            var rect=((RectTransform)transform).rect;var center=new Vector2(rect.width*.5f,rect.height*.5f);float scale=Mathf.Min(rect.width*.32f,rect.height*.36f);
            int n=frame.Dimensions;var hidden=new List<LogicalAxis>();
            foreach(var entry in buttons)
            {
                var axis=entry.Key;int core=SliceAxes.CoreAxis(axis);
                // Axes beyond the space of this match stay out of the gizmo.
                if(core>=n){entry.Value.gameObject.SetActive(false);directions[axis]=Vector2.zero;continue;}
                entry.Value.gameObject.SetActive(true);
                var world=VecN.Unit(core,n);var tangent=Cfg2Projection.Tangent(frame,world);var p=Cfg2Projection.Project(tangent,rotation,scale);
                directions[axis]=p;string symbol="";
                if(tangent.Length<1e-7){hidden.Add(axis);continue;}
                if(p.magnitude<4){p=new Vector2(0,-14);symbol=" ·";}
                else p+=p.normalized*13;
                var r=(RectTransform)entry.Value.transform;r.anchoredPosition=new Vector2(center.x+p.x-24,-center.y+p.y+13.5f);
                var label=entry.Value.GetComponentInChildren<Text>();label.fontSize=18;label.text=SliceAxes.AxisName(axis)+symbol;
            }
            // Hidden axes (no direction in the section) line up under the globe with C's coordinate on each: W=0  V=0 ...
            float spacing=Mathf.Min(52,(rect.width-4)/Mathf.Max(1,hidden.Count));
            for(int i=0;i<hidden.Count;i++)
            {
                var axis=hidden[i];var r=(RectTransform)buttons[axis].transform;float x=(i-(hidden.Count-1)*.5f)*spacing;
                r.anchoredPosition=new Vector2(center.x+x-24,-center.y-scale-16+13.5f);
                var label=buttons[axis].GetComponentInChildren<Text>();label.fontSize=hidden.Count>4?11:13;
                label.text=SliceAxes.AxisName(axis)+"="+frame.Center.Axis(SliceAxes.CoreAxis(axis)).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
    internal sealed class Cfg2GizmoGraphic : Graphic
    {
        internal SliceFrame Frame;internal Quaternion CameraRotation;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!Frame.IsOrthonormal())return;var r=rectTransform.rect;var center=new Vector2(r.width*.5f,-r.height*.5f);float scale=Mathf.Min(r.width*.32f,r.height*.36f);
            var inverse=Quaternion.Inverse(CameraRotation);
            // Lit glass globe: the live 3D section space seen from the current camera.
            Cfg2Glow.Blob(vh,center,scale*1.45f,new Color(.28f,.40f,1,.20f));
            Cfg2Glow.Blob(vh,center,scale*.75f,new Color(.55f,.66f,1,.12f));
            Cfg2Glow.Circle(vh,center,scale,new Color(.62f,.72f,1,.38f),.8f,7,64);
            foreach(float latitude in new[]{-.55f,.55f})
                Ring(vh,inverse,center,scale,Vector3.right,Vector3.forward,new Color(.40f,.62f,1,.38f),Vector3.up*latitude,Mathf.Sqrt(1-latitude*latitude));
            Ring(vh,inverse,center,scale,Vector3.right,Vector3.forward,new Color(.38f,.70f,1,.85f));
            Ring(vh,inverse,center,scale,Vector3.right,Vector3.up,new Color(1,.42f,.55f,.72f));
            Ring(vh,inverse,center,scale,Vector3.forward,Vector3.up,new Color(.72f,.50f,1,.72f));
            // Axes are drawn back to front so the nearer neon stays on top.
            var axes=new List<(LogicalAxis axis,Vec3 tangent,float depth)>();
            foreach(LogicalAxis a in SliceAxes.Axes(Frame.Dimensions)){var t=Cfg2Projection.Tangent(Frame,VecN.Unit(SliceAxes.CoreAxis(a),Frame.Dimensions));axes.Add((a,t,Cfg2Projection.Depth(t,CameraRotation)));}
            axes.Sort((x,y)=>y.depth.CompareTo(x.depth));int hiddenRings=0;
            foreach(var entry in axes)
            {
                var color=Cfg2Projection.AxisColor(entry.axis);
                // A hidden axis points straight out of the section: one ring per hidden axis around the centre, widening outwards.
                if(entry.tangent.Length<1e-7)
                {float radius=8+3*hiddenRings++;Cfg2Glow.Circle(vh,center,radius,color,1.1f,7,32);if(hiddenRings==1)Cfg2Glow.Blob(vh,center,10,Cfg2Glow.Alpha(color,.45f));continue;}
                var p=Cfg2Projection.Project(entry.tangent,CameraRotation,scale);float fade=entry.depth>0?.65f:1;
                Cfg2Glow.Dashed(vh,center,center-p*.85f,Cfg2Glow.Alpha(color,.38f*fade),2.2f,3,3);
                Cfg2Glow.Arrow(vh,center,center+p,Cfg2Glow.Alpha(color,fade),1.6f);
            }
            Cfg2Glow.Blob(vh,center,8,new Color(1,.86f,.55f,.75f));Cfg2Glow.Blob(vh,center,2.6f,new Color(1,.97f,.88f,1));
        }
        static void Ring(VertexHelper vh,Quaternion inverse,Vector2 center,float scale,Vector3 a,Vector3 b,Color color,Vector3 offset=default,float radius=1)
        {
            const int steps=72;Vector3 previous=default;
            for(int i=0;i<=steps;i++)
            {
                float t=i*Mathf.PI*2/steps;var q=inverse*((a*Mathf.Cos(t)+b*Mathf.Sin(t))*radius+offset);
                if(i>0){float fade=previous.z+q.z>0?.30f:1;Cfg2Glow.Neon(vh,center+new Vector2(previous.x,previous.y)*scale,center+new Vector2(q.x,q.y)*scale,Cfg2Glow.Alpha(color,fade),.9f,6);}
                previous=q;
            }
        }
    }
    // A piece placed in a panel's chart: its coordinates on the panel's three shown axes, relative to the chart origin.
    internal readonly struct Cfg2PanelDot
    {
        public readonly Vec3 At;public readonly Side Side;public readonly bool Selected;
        public Cfg2PanelDot(Vec3 at,Side side,bool selected){At=at;Side=side;Selected=selected;}
        // 先手 A amber, 後手 B pink: apart from the axis colours and from the blue hyperplane.
        public static readonly Color SideA=new Color(1,.70f,.28f),SideB=new Color(1,.42f,.70f);
        public static Color Of(Side side)=>side==Side.A?SideA:SideB;
    }
    internal sealed class Cfg2RelationGraphic : Graphic
    {
        internal HyperplaneRelation Relation;internal Quaternion CameraRotation;internal ReferenceSection Reference;internal double HalfExtent=4.5;
        // Every piece projected onto the panel's axis space (the hidden coordinates dropped); null draws none.
        internal IReadOnlyList<Cfg2PanelDot> Dots;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        internal Vector2 Center=>new Vector2(rectTransform.rect.width*.5f,-rectTransform.rect.height*.5f);
        internal float ProjectionScale=>Mathf.Min(rectTransform.rect.width*.22f,rectTransform.rect.height*.30f);
        internal Vector2 Project(Vec3 point)=>Cfg2Projection.Project(point,CameraRotation,ProjectionScale);
        internal Vector2 AxisEnd(LogicalAxis axis)
        {var basis=Reference.Frame();return Project(Cfg2Projection.Tangent(basis,VecN.Unit(SliceAxes.CoreAxis(axis),Reference.Dimensions))*1.4);}
        static readonly Color Plane=new Color(.42f,.50f,1),Edge=new Color(.66f,.76f,1),Gold=new Color(1,.84f,.50f);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var center=Center;var r=rectTransform.rect;double h=HalfExtent;Vector2 P(Vec3 p)=>center+Project(p/h);
            // Glass sheen and a cool light pool behind the drawing.
            Cfg2Glow.Gradient(vh,0,0,r.width,r.height,new Color(.60f,.70f,1,.07f),new Color(.10f,.14f,.32f,.02f));
            Cfg2Glow.Blob(vh,center,Mathf.Min(r.width,r.height)*.78f,new Color(.26f,.36f,.95f,.15f));
            // Floor grid on the reference box's bottom face (chart Z is display up).
            for(int i=0;i<=6;i++)
            {
                double t=-h+2*h*i/6;var c=new Color(.58f,.68f,1,i==3?.15f:.07f);
                Cfg2Glow.Line(vh,P(new Vec3(t,-h,-h)),P(new Vec3(t,h,-h)),c,1.6f);Cfg2Glow.Line(vh,P(new Vec3(-h,t,-h)),P(new Vec3(h,t,-h)),c,1.6f);
            }
            var cube=new Vec3[8];for(int i=0;i<8;i++)cube[i]=new Vec3((i&1)==0?-h:h,(i&2)==0?-h:h,(i&4)==0?-h:h);
            for(int i=0;i<8;i++)for(int a=0;a<3;a++)
            {
                int j=i^(1<<a);if(i>j)continue;bool back=Cfg2Projection.Depth((cube[i]+cube[j])*.5,CameraRotation)>.05;
                if(back)Cfg2Glow.Dashed(vh,P(cube[i]),P(cube[j]),new Color(.62f,.70f,.85f,.16f),1.6f,3,3);else Cfg2Glow.Line(vh,P(cube[i]),P(cube[j]),new Color(.70f,.78f,.92f,.26f),1.8f);
            }
            var basis=Reference.Frame();var visible=new List<(LogicalAxis axis,float depth)>();
            foreach(LogicalAxis axis in SliceAxes.Axes(Reference.Dimensions)){var t=Cfg2Projection.Tangent(basis,VecN.Unit(SliceAxes.CoreAxis(axis),Reference.Dimensions));if(t.Length>.5)visible.Add((axis,Cfg2Projection.Depth(t,CameraRotation)));}
            // Negative half-axes sit behind the plane.
            foreach(var entry in visible)Cfg2Glow.Dashed(vh,center,center-AxisEnd(entry.axis)*.8f,Cfg2Glow.Alpha(Cfg2Projection.AxisColor(entry.axis),.30f),2,3,3);
            if(Relation!=null)
            {
                var v=Relation.Vertices;var indices=Relation.TriangleIndices;bool volume=Relation.IsCoincident;
                for(int i=0;i+2<indices.Count;i+=3)Cfg2Glow.Triangle(vh,P(v[indices[i]]),P(v[indices[i+1]]),P(v[indices[i+2]]),new Color(Plane.r,Plane.g,Plane.b,volume?.08f:.24f));
                if(!volume&&v.Count>=3)PlaneGrid(vh,v,Relation.ChartCenter,P);
                var edges=Relation.Edges;for(int i=0;i+1<edges.Count;i+=2)Cfg2Glow.Neon(vh,P(v[edges[i]]),P(v[edges[i+1]]),new Color(Edge.r,Edge.g,Edge.b,volume?.70f:.95f),1.2f,9);
                if(!volume)foreach(var corner in v)Cfg2Glow.Blob(vh,P(corner),5,new Color(.85f,.90f,1,.55f));
            }
            if(Dots!=null&&Dots.Count>0)DrawDots(vh,P,h);
            visible.Sort((x,y)=>y.depth.CompareTo(x.depth));
            foreach(var entry in visible)Cfg2Glow.Arrow(vh,center,center+AxisEnd(entry.axis),Cfg2Projection.AxisColor(entry.axis),1.4f);
            Cfg2Glow.Blob(vh,center,4,new Color(.9f,.93f,1,.8f));
            if(Relation!=null)
            {
                // C, the hyperplane centre, relative to the panel origin P.
                var c=P(Relation.ChartCenter);if((c-center).sqrMagnitude>1)Cfg2Glow.Dashed(vh,center,c,Cfg2Glow.Alpha(Gold,.55f),1.8f,3,2);
                Cfg2Glow.Blob(vh,c,10,Cfg2Glow.Alpha(Gold,.45f));Cfg2Glow.Blob(vh,c,3.2f,new Color(1,.96f,.85f,1));
            }
        }
        // Back to front, so nearer pieces cover farther ones; pieces behind the chart centre fade a little.
        // The selected piece is drawn last, larger and ringed in gold.
        void DrawDots(VertexHelper vh,Func<Vec3,Vector2> P,double h)
        {
            float radius=Mathf.Clamp(ProjectionScale*.065f,1.4f,5);
            var order=new List<(int index,float depth)>(Dots.Count);
            for(int i=0;i<Dots.Count;i++)order.Add((i,Cfg2Projection.Depth(Dots[i].At/h,CameraRotation)));
            order.Sort((x,y)=>y.depth.CompareTo(x.depth));
            foreach(var (index,depth) in order)
            {
                var dot=Dots[index];if(dot.Selected)continue;var at=P(dot.At);var color=Cfg2PanelDot.Of(dot.Side);float fade=Mathf.Clamp(1-depth*.45f,.5f,1);
                Cfg2Glow.Blob(vh,at,radius*3,Cfg2Glow.Alpha(color,.30f*fade));Cfg2Glow.Blob(vh,at,radius*1.6f,new Color(Mathf.Lerp(color.r,1,.25f),Mathf.Lerp(color.g,1,.25f),Mathf.Lerp(color.b,1,.25f),fade));
            }
            foreach(var dot in Dots)
            {
                if(!dot.Selected)continue;var at=P(dot.At);var color=Cfg2PanelDot.Of(dot.Side);
                Cfg2Glow.Blob(vh,at,radius*5,Cfg2Glow.Alpha(Gold,.45f));Cfg2Glow.Circle(vh,at,radius*2.3f,Gold,1,3,20);Cfg2Glow.Blob(vh,at,radius*2.1f,new Color(1,.97f,.88f,1));Cfg2Glow.Blob(vh,at,radius*1.3f,color);
            }
        }
        // Unit lattice inside the convex intersection polygon, aligned to its first edge and passing through C.
        void PlaneGrid(VertexHelper vh,IReadOnlyList<Vec3> polygon,Vec3 anchor,Func<Vec3,Vector2> P)
        {
            var normal=Vec3.Cross(polygon[1]-polygon[0],polygon[2]-polygon[0]);if(normal.Length<1e-9)return;normal=normal.Normalized;
            Vec3 middle=default;foreach(var p in polygon)middle+=p;middle/=polygon.Count;
            var u=(polygon[1]-polygon[0]).Normalized;var w=Vec3.Cross(normal,u);
            double minU=double.MaxValue,maxU=double.MinValue,minW=double.MaxValue,maxW=double.MinValue;
            foreach(var p in polygon){double pu=Vec3.Dot(p-anchor,u),pw=Vec3.Dot(p-anchor,w);minU=Math.Min(minU,pu);maxU=Math.Max(maxU,pu);minW=Math.Min(minW,pw);maxW=Math.Max(maxW,pw);}
            var color=new Color(.70f,.80f,1,.22f);
            foreach(var (across,along,min,max) in new[]{(u,w,minU,maxU),(w,u,minW,maxW)})
            for(int k=(int)Math.Ceiling(min);k<=(int)Math.Floor(max);k++)
            {
                var origin=anchor+across*k;double t0=double.MinValue,t1=double.MaxValue;
                for(int i=0;i<polygon.Count&&t0<t1;i++)
                {
                    var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var inward=Vec3.Cross(normal,b-a);if(Vec3.Dot(inward,middle-a)<0)inward=inward*-1;
                    double s=Vec3.Dot(inward,origin-a),rate=Vec3.Dot(inward,along);
                    if(Math.Abs(rate)<1e-12){if(s<0)t1=t0;continue;}
                    double t=-s/rate;if(rate>0)t0=Math.Max(t0,t);else t1=Math.Min(t1,t);
                }
                if(t1-t0>1e-6)Cfg2Glow.Line(vh,P(origin+along*t0),P(origin+along*t1),k==0?Cfg2Glow.Alpha(color,1.6f):color,1.6f);
            }
        }
    }
}
