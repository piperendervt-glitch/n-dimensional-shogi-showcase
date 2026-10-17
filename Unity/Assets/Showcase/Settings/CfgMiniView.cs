using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    internal sealed class CfgMiniView : MonoBehaviour
    {
        Cfg1App app;ReferenceView reference;CfgMiniGraphic graphic;Text title;AxisDrag drag;
        public void Initialize(Cfg1App owner,ReferenceView view)
        {
            app=owner;reference=view;drag=new AxisDrag(app.Slice,app.Ownership);
            title=CfgUi.Label(transform,view.ToString(),6,2,148,22,14);
            var r=CfgUi.Rect(transform,"Actual 3D intersection",0,25,160,80);graphic=r.gameObject.AddComponent<CfgMiniGraphic>();graphic.raycastTarget=false;
            var basis=SliceAxes.Reference(view);
            foreach(LogicalAxis axis in Enum.GetValues(typeof(LogicalAxis)))
            {
                var world=SliceAxes.WorldAxis(axis);var coord=new Vec3(VecN.Dot(world,basis.AxisX),VecN.Dot(world,basis.AxisY),VecN.Dot(world,basis.AxisZ));
                if(coord.Length<.1)continue;
                var tip=CfgMiniGraphic.Project(coord*1.4);
                var handle=CfgUi.Panel(transform,"Drag world "+axis,80+tip.x-11.5f,67-tip.y-11.5f,23,23,new Color(.08f,.14f,.23f,.75f));
                var label=CfgUi.Label(handle,axis.ToString(),0,0,23,23,15);label.alignment=TextAnchor.MiddleCenter;
                var input=handle.gameObject.AddComponent<CfgPointerSurface>();LogicalAxis captured=axis;
                input.Down=e=>{if(e.button!=PointerEventData.InputButton.Left)return;var direction=tip*app.UiScale;if(!drag.Begin(captured,e.pointerId,e.position.x,e.position.y,direction.x,direction.y))app.InputNotice("軸操作を開始できません。回転完了を待つか別の小窓を選んでください。");else app.Trace("AxisDown",view+" / "+captured);};
                input.Drag=e=>{if(drag.Update(e.pointerId,e.position.x,e.position.y))app.InvalidateGeometry();};
                input.Up=e=>{if(drag.Complete(e.pointerId))app.Trace("AxisCommit",captured+" / "+app.SliceCenter);};
            }
        }
        public void Refresh(SliceFrame frame)
        {var relation=HyperplaneRelation.Build(frame,reference,1,app.Slice.ViewRevision);graphic.Relation=relation;graphic.SetVerticesDirty();title.text=reference+(relation.IsCoincident?"  一致":"  実交差");}
        public void CancelDrag(){if(drag==null)return;drag.Cancel();app.InvalidateGeometry();}
    }
    internal sealed class CfgMiniGraphic : Graphic
    {
        public HyperplaneRelation Relation;
        public static Vector2 Project(Vec3 p)
        {var v=Quaternion.Euler(24,-32,0)*SectionView.MapVector(p);return new Vector2(v.x,v.y)*23;}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Vector2 center=new Vector2(80,-42);
            Vector2 P(Vec3 p)=>center+Project(p);
            if(Relation!=null&&!Relation.IsCoincident&&Relation.Vertices.Count>=3)
            {
                var vertices=Relation.Vertices;for(int i=1;i+1<vertices.Count;i++)Triangle(vh,P(vertices[0]),P(vertices[i]),P(vertices[i+1]),new Color(.32f,.59f,1,.38f));
                for(int i=0;i<vertices.Count;i++)Line(vh,P(vertices[i]),P(vertices[(i+1)%vertices.Count]),new Color(.53f,.75f,1,.95f),1.3f);
            }
            var cube=new Vec3[8];for(int i=0;i<8;i++)cube[i]=new Vec3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1);
            for(int i=0;i<8;i++)for(int a=0;a<3;a++){int j=i^(1<<a);if(i<j)Line(vh,P(cube[i]),P(cube[j]),Relation?.IsCoincident==true?new Color(.45f,.75f,1,.9f):new Color(.5f,.6f,.7f,.45f),1);}
            foreach(var a in new[]{new Vec3(1.4,0,0),new Vec3(0,1.4,0),new Vec3(0,0,1.4)})Line(vh,center,P(a),new Color(.94f,.84f,.56f,.95f),1.7f);
        }
        static void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int i=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
        static void Line(VertexHelper vh,Vector2 a,Vector2 b,Color color,float width)
        {var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Triangle(vh,a-n,a+n,b+n,color);Triangle(vh,a-n,b+n,b-n,color);}
    }
}
