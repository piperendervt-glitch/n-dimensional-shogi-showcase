using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // One coordinate subspace of the N-dimensional board spanned by k axes (every other axis fixed at the selected cell), drawn
    // as the k-cube of cells ±4 around that cell: three axes in perspective, a fourth and further axes projected one at a time
    // the way the 投影ビュー does it. A slider that moves k≥3 axes at once has no candidate in any coordinate plane, so for
    // such a piece these take the place of the plane diagrams; the candidates sit on the cube's diagonals.
    internal sealed class Cfg2SubspaceView : MonoBehaviour
    {
        int[] uiAxes=Array.Empty<int>(),coreAxes=Array.Empty<int>();Cfg2SubspaceGraphic graphic;Text heading,condition;readonly List<Text> axisLabels=new List<Text>();
        InputOwnership ownership;Func<float> uiScale;bool interactive;
        QN center;long revision;HashSet<QN> gs=new HashSet<QN>(),ps=new HashSet<QN>(),attacks=new HashSet<QN>();CfgVisualPiece[] pieces=Array.Empty<CfgVisualPiece>();
        bool showCandidates=true,showAttacks=true,showGrid=true;
        // Fixed 3-D camera around the projected cube, and the k×k rotation that keeps the hidden axes apart.
        float yaw=-35,pitch=22;double[,] rotation;
        int pointer=-1;bool pressed,moved;Vector2 lastPosition,downPosition;
        public int Dimensions=4;
        public IReadOnlyList<int> Axes=>uiAxes;
        public int AxisCount=>uiAxes.Length;
        public string Name=>string.Concat(uiAxes.Select(a=>SliceAxes.AxisName((LogicalAxis)a)));
        public long Revision=>revision;
        public int VisibleCandidates=>ps.Count(Contains);
        public int VisibleAttacks=>attacks.Count(Contains);
        public string FixedCoordinates=>condition==null?"":condition.text;
        // Every k-subset of the N UI axes in lexicographic order: XYZ XYW XYV ... for k=3.
        public static IEnumerable<int[]> Subspaces(int dimensions,int k)
        {
            if(k<1||k>dimensions)yield break;var c=new int[k];for(int i=0;i<k;i++)c[i]=i;
            while(true)
            {
                yield return (int[])c.Clone();
                int i=k-1;while(i>=0&&c[i]==dimensions-k+i)i--;if(i<0)yield break;
                c[i]++;for(int j=i+1;j<k;j++)c[j]=c[j-1]+1;
            }
        }
        public static int SubspaceCount(int dimensions,int k)
        {if(k<0||k>dimensions)return 0;long r=1;for(int i=1;i<=k;i++)r=r*(dimensions-k+i)/i;return (int)r;}
        public void Initialize(int[] axes,InputOwnership input=null,Func<float> scale=null,bool orbitable=false)
        {
            if(pressed&&ownership!=null&&ownership.IsOwner(InputOwner.Rotation4D,pointer))ownership.Cancel();pressed=false;
            Cfg2Ui.ClearOwned(transform);axisLabels.Clear();
            uiAxes=(int[])axes.Clone();coreAxes=uiAxes.Select(a=>SliceAxes.CoreAxis((LogicalAxis)a)).ToArray();ownership=input;uiScale=scale;interactive=orbitable;
            yaw=-35;pitch=22;BuildRotation();
            var size=((RectTransform)transform).rect;
            heading=CfgUi.Label(transform,Name,8,2,size.width-16,22,15,Cfg2Ui.Gold);
            var rect=CfgUi.Rect(transform,"Fixed-axes subspace",0,0,size.width,size.height);
            CfgUi.Fill(rect);graphic=rect.gameObject.AddComponent<Cfg2SubspaceGraphic>();graphic.View=this;graphic.color=Color.white;graphic.transform.SetAsFirstSibling();
            condition=CfgUi.Label(transform,"",8,size.height-22,size.width-16,20,11,new Color(.75f,.80f,.86f));
            foreach(int a in uiAxes){var t=CfgUi.Label(transform,SliceAxes.AxisName((LogicalAxis)a),0,0,24,20,orbitable?14:11);t.alignment=TextAnchor.MiddleCenter;var color=Cfg2Projection.AxisColor((LogicalAxis)a);t.color=color;Cfg2Glow.GlowText(t,color);axisLabels.Add(t);}
            graphic.raycastTarget=orbitable;
            if(orbitable){var surface=rect.gameObject.AddComponent<CfgPointerSurface>();surface.Down=OnDown;surface.Drag=OnDrag;surface.Up=OnUp;}
        }
        // Default turns of the hidden axes (4th and beyond) so that they do not fall onto the first three.
        void BuildRotation()
        {
            int k=uiAxes.Length;rotation=new double[k,k];for(int i=0;i<k;i++)rotation[i,i]=1;
            for(int a=3;a<k;a++){Turn(a%3,a,a==3?32:a==4?26:14+6*(a-5));if(a>=4)Turn((a+1)%3,a,10);}
        }
        void Turn(int a,int b,double degrees)
        {
            int k=uiAxes.Length;double t=degrees*Math.PI/180,c=Math.Cos(t),s=Math.Sin(t);
            for(int j=0;j<k;j++){double ra=rotation[a,j],rb=rotation[b,j];rotation[a,j]=c*ra-s*rb;rotation[b,j]=s*ra+c*rb;}
        }
        public void Refresh(IEnumerable<CfgVisualPiece> source,IEnumerable<QN> g,IEnumerable<QN> p,IEnumerable<QN> attacked,QN selected,long stateRevision,bool candidates=true,bool attack=true,bool grid=true)
        {
            center=selected;revision=stateRevision;showCandidates=candidates;showAttacks=attack;showGrid=grid;
            gs=new HashSet<QN>(g??Array.Empty<QN>());ps=new HashSet<QN>(p??Array.Empty<QN>());attacks=new HashSet<QN>(attacked??Array.Empty<QN>());
            pieces=(source??Array.Empty<CfgVisualPiece>()).Where(item=>item.Position.InBounds&&Contains(item.Position)).ToArray();
            condition.text=string.Join("  ",Enumerable.Range(0,Dimensions).Where(i=>!coreAxes.Contains(i)).Select(i=>SliceAxes.AxisName(SliceAxes.AxisOfCore(i))+"="+center.Axis(i)));
            PlaceLabels();graphic.SetVerticesDirty();
        }
        public bool Contains(QN cell)
        {int n=Math.Max(Dimensions,Math.Max(cell.Dimensions,center.Dimensions));for(int axis=0;axis<n;axis++)if(!coreAxes.Contains(axis)&&cell.Axis(axis)!=center.Axis(axis))return false;return true;}
        // ---- projection: cell offsets along the k axes → 3-D (one perspective division per hidden axis) → the fixed camera ----
        const double HiddenCamera=16,Camera3=16,Half=4.5;
        Vector3 Project(double[] offset,out float depth)
        {
            int k=uiAxes.Length;var p=new double[k];for(int i=0;i<k;i++){double v=0;for(int j=0;j<k;j++)v+=rotation[i,j]*offset[j];p[i]=v;}
            double d=0;for(int a=k-1;a>=3;a--){double s=HiddenCamera/Math.Max(2,HiddenCamera-p[a]);d+=p[a]/HiddenCamera;for(int i=0;i<a;i++)p[i]*=s;}
            double x=p[0],y=k>1?p[1]:0,z=k>2?p[2]:0;
            double cy=Math.Cos(yaw*Math.PI/180),sy=Math.Sin(yaw*Math.PI/180),cp=Math.Cos(pitch*Math.PI/180),sp=Math.Sin(pitch*Math.PI/180);
            double x1=x*cy-z*sy,z1=x*sy+z*cy,y2=y*cp-z1*sp,z2=y*sp+z1*cp;
            double s3=Camera3/Math.Max(2,Camera3-z2);depth=(float)d;
            return new Vector3((float)(x1*s3),(float)(y2*s3),(float)z2);
        }
        void Frame(out Vector2 origin,out float scale)
        {
            var rect=((RectTransform)transform).rect;float size=Mathf.Max(9,Mathf.Min(rect.width-16,rect.height-44));
            origin=new Vector2(rect.width*.5f,-22-size*.5f);scale=size/(float)(2*Half*(uiAxes.Length<=3?1.75:2.1));
        }
        Vector2 ToScreen(Vector3 projected,Vector2 origin,float scale)=>origin+new Vector2(projected.x,projected.y)*scale;
        double[] Offset(QN cell){var o=new double[uiAxes.Length];for(int i=0;i<o.Length;i++)o[i]=cell.Axis(coreAxes[i])-center.Axis(coreAxes[i]);return o;}
        void PlaceLabels()
        {
            if(graphic==null)return;Frame(out var origin,out float scale);
            for(int i=0;i<axisLabels.Count;i++){var o=new double[uiAxes.Length];o[i]=Half+.8;var s=ToScreen(Project(o,out _),origin,scale);axisLabels[i].rectTransform.anchoredPosition=new Vector2(s.x-12,s.y+10);}
        }
        // ---- pointer: a drag orbits the 3-D camera of the large view ----
        void OnDown(PointerEventData e)
        {
            if(!interactive||pressed||e.button!=PointerEventData.InputButton.Left||CfgUi.EditingText)return;
            if(ownership!=null&&!ownership.TryBegin(InputOwner.Rotation4D,e.pointerId,()=>pressed=false))return;
            pressed=true;moved=false;pointer=e.pointerId;downPosition=lastPosition=e.position;
        }
        void OnDrag(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer||(ownership!=null&&!ownership.IsOwner(InputOwner.Rotation4D,pointer)))return;
            float scale=Mathf.Max(.01f,uiScale?.Invoke()??1);var delta=(e.position-lastPosition)/scale;lastPosition=e.position;
            if(!moved&&(e.position-downPosition).magnitude/scale<5)return;moved=true;
            yaw+=delta.x*.4f;pitch=Mathf.Clamp(pitch-delta.y*.4f,-80,80);PlaceLabels();graphic.SetVerticesDirty();
        }
        void OnUp(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer)return;pressed=false;
            if(ownership!=null&&ownership.IsOwner(InputOwner.Rotation4D,pointer))ownership.Release(InputOwner.Rotation4D,pointer);
        }
        void OnDisable(){if(pressed&&ownership!=null&&ownership.IsOwner(InputOwner.Rotation4D,pointer))ownership.Cancel();pressed=false;}
        void Update(){if(pressed&&ownership!=null&&!ownership.IsOwner(InputOwner.Rotation4D,pointer))pressed=false;}
        // ---- drawing ----
        internal void Populate(VertexHelper vh)
        {
            vh.Clear();if(uiAxes.Length==0||rotation==null)return;int k=uiAxes.Length;var rect=((RectTransform)transform).rect;Frame(out var origin,out float scale);
            Cfg2Glow.Gradient(vh,0,0,rect.width,rect.height,new Color(.03f,.06f,.13f,.60f),new Color(.01f,.02f,.05f,.60f));
            Cfg2Glow.Blob(vh,origin,scale*(float)Half*2.2f,new Color(.10f,.22f,.60f,.16f));
            var o=new double[k];var q=new double[k];
            Vector2 S(double[] offset){return ToScreen(Project(offset,out _),origin,scale);}
            // Floor grid on the first two axes through the selected cell: the orientation reference.
            if(showGrid)for(int i=-4;i<=5;i++)
            {
                var c=new Color(.32f,.55f,1,i==-4||i==5?.30f:.12f);
                Array.Clear(o,0,k);Array.Clear(q,0,k);o[0]=i-.5;o[1]=-Half;q[0]=i-.5;q[1]=Half;Cfg2Glow.Line(vh,S(o),S(q),c,1.2f);
                Array.Clear(o,0,k);Array.Clear(q,0,k);o[1]=i-.5;o[0]=-Half;q[1]=i-.5;q[0]=Half;Cfg2Glow.Line(vh,S(o),S(q),c,1.2f);
            }
            // The k-cube of the board region: an edge between the two corners that differ in one axis takes that axis's colour.
            int corners=1<<k;
            for(int v=0;v<corners;v++)for(int axis=0;axis<k;axis++)
            {
                if((v>>axis&1)!=0)continue;int w=v|1<<axis;
                for(int i=0;i<k;i++){o[i]=(v>>i&1)!=0?Half:-Half;q[i]=(w>>i&1)!=0?Half:-Half;}
                var color=Cfg2Projection.AxisColor((LogicalAxis)uiAxes[axis]);
                Cfg2Glow.Line(vh,S(o),S(q),Cfg2Glow.Alpha(color,k<=3?.55f:.38f),k<=3?1.6f:1.2f);
            }
            // Axes through the selected cell, brighter, with a dot at the positive end.
            for(int axis=0;axis<k;axis++)
            {
                Array.Clear(o,0,k);Array.Clear(q,0,k);o[axis]=-Half;q[axis]=Half;var color=Cfg2Projection.AxisColor((LogicalAxis)uiAxes[axis]);
                Cfg2Glow.Dashed(vh,S(o),origin,Cfg2Glow.Alpha(color,.45f),1.2f,3,3);Cfg2Glow.Neon(vh,origin,S(q),color,.9f,4);Cfg2Glow.Blob(vh,S(q),3.5f,color);
            }
            // Cells: a ray from the centre to every candidate, then dots; danger red, candidates blue, geometric faint, pieces as squares.
            var cells=new List<(QN cell,int kind,float depth,Vector2 at)>();
            void Add(QN cell,int kind){if(!Contains(cell)||cell==center)return;var off=Offset(cell);var p=Project(off,out float depth);cells.Add((cell,kind,depth,ToScreen(p,origin,scale)));}
            if(showCandidates)foreach(var cell in gs)if(!ps.Contains(cell))Add(cell,0);
            if(showCandidates)foreach(var cell in ps)Add(cell,1);
            if(showAttacks)foreach(var cell in attacks)Add(cell,2);
            foreach(var piece in pieces)if(piece.Position!=center)Add(piece.Position,piece.Side==Side.A?3:4);
            cells.Sort((x,y)=>x.depth.CompareTo(y.depth));
            float dot=Mathf.Clamp(scale*.33f,2.5f,7);
            foreach(var entry in cells)if(entry.kind==1)Cfg2Glow.Line(vh,origin,entry.at,new Color(.30f,.62f,1,.22f),Mathf.Max(1,dot*.5f));
            foreach(var entry in cells)
            {
                float fade=Mathf.Clamp(.9f+entry.depth*1.6f,.4f,1);
                switch(entry.kind)
                {
                    case 0:Cfg2Glow.Blob(vh,entry.at,dot*.9f,new Color(.40f,.58f,.95f,.30f*fade));break;
                    case 1:Cfg2Glow.Blob(vh,entry.at,dot*2.2f,new Color(.18f,.48f,1,.45f*fade));Cfg2Glow.Blob(vh,entry.at,dot,new Color(.62f,.85f,1,fade));break;
                    case 2:Cfg2Glow.Blob(vh,entry.at,dot*2.2f,new Color(1,.22f,.18f,.40f*fade));Cfg2Glow.Blob(vh,entry.at,dot,new Color(1,.62f,.58f,fade));break;
                    default:{float h=dot*1.1f;Cfg2Glow.Rect(vh,entry.at.x-h,entry.at.y+h,2*h,2*h,entry.kind==3?new Color(.95f,.88f,.70f,.9f):new Color(.45f,.30f,.20f,.9f));break;}
                }
            }
            // The selected cell itself, gold.
            Cfg2Glow.Blob(vh,origin,dot*3.2f,new Color(1,.70f,.24f,.55f));Cfg2Glow.Blob(vh,origin,dot*1.4f,new Color(1,.92f,.65f,.97f));
        }
    }
    internal sealed class Cfg2SubspaceGraphic : MaskableGraphic
    {
        public Cfg2SubspaceView View;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        protected override void OnPopulateMesh(VertexHelper vh){if(View==null)vh.Clear();else View.Populate(vh);}
    }
}
