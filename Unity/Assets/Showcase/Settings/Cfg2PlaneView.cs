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
    internal enum Cfg2Plane { XY,XZ,XW,YZ }
    internal sealed class Cfg2PlaneView : MonoBehaviour
    {
        Cfg2Plane plane;Cfg2PlaneGraphic graphic;Text heading,condition;Action<QN,long> clicked;InputOwnership ownership;
        readonly Dictionary<string,Graphic> icons=new Dictionary<string,Graphic>();
        QN center,downCell;long revision,downRevision;int pointer;bool hasDown;Vector2 downPosition;
        Func<long> stateVersion,draftVersion;long downStateVersion,downDraftVersion,contentVersion,downContentVersion;
        int horizontal,vertical;bool showCandidates,showAttacks,showGrid;
        HashSet<QN> gs=new HashSet<QN>(),ps=new HashSet<QN>(),attacks=new HashSet<QN>();
        public Cfg2Plane Plane=>plane;
        public long Revision=>revision;
        public int VisiblePieceCount {get;private set;}
        public int VisibleCandidates=>ps.Count(Contains);
        public int VisibleAttacks=>attacks.Count(Contains);
        public string FixedCoordinates=>condition==null?"":condition.text;
        public int RejectedStaleCount {get;private set;}
        public void Initialize(Cfg2Plane view,Action<QN,long> selected,InputOwnership input=null,Func<long> stateRevision=null,Func<long> draftRevision=null)
        {
            if(hasDown&&ownership!=null&&ownership.IsOwner(InputOwner.Board,pointer))ownership.Cancel();hasDown=false;
            Cfg2Ui.ClearOwned(transform);icons.Clear();
            plane=view;clicked=selected;ownership=input;stateVersion=stateRevision;draftVersion=draftRevision;
            horizontal=view==Cfg2Plane.YZ?2:1;vertical=view==Cfg2Plane.XY?2:view==Cfg2Plane.XW?3:0;
            heading=CfgUi.Label(transform,view.ToString(),8,2,110,22,15,Cfg2Ui.Gold);
            var rect=CfgUi.Rect(transform,"Fixed two-axis cells",0,0,((RectTransform)transform).rect.width,((RectTransform)transform).rect.height);
            CfgUi.Fill(rect);graphic=rect.gameObject.AddComponent<Cfg2PlaneGraphic>();graphic.View=this;graphic.color=Color.white;
            graphic.transform.SetAsFirstSibling();
            condition=CfgUi.Label(transform,"",8,((RectTransform)transform).rect.height-22,((RectTransform)transform).rect.width-16,20,11,new Color(.75f,.80f,.86f));
            graphic.raycastTarget=selected!=null;
            if(selected!=null){var surface=rect.gameObject.AddComponent<CfgPointerSurface>();surface.Down=OnDown;surface.Up=OnUp;}
        }
        public void Refresh(IEnumerable<CfgVisualPiece> source,IEnumerable<QN> g,IEnumerable<QN> p,IEnumerable<QN> attacked,QN selected,long stateRevision,bool candidates=true,bool attack=true,bool grid=true)
        {
            center=selected;revision=stateRevision;contentVersion++;showCandidates=candidates;showAttacks=attack;showGrid=grid;
            gs=new HashSet<QN>(g??Array.Empty<QN>());ps=new HashSet<QN>(p??Array.Empty<QN>());attacks=new HashSet<QN>(attacked??Array.Empty<QN>());
            condition.text=string.Join("  ",Enumerable.Range(0,4).Where(i=>i!=horizontal&&i!=vertical).Select(i=>AxisName(i)+"="+center[i]));
            var live=new HashSet<string>();VisiblePieceCount=0;
            foreach(var item in source??Array.Empty<CfgVisualPiece>())
            {
                if(!Contains(item.Position)||!item.Position.InBounds)continue;live.Add(item.Id);VisiblePieceCount++;
                if(!icons.TryGetValue(item.Id,out var icon)){icon=Cfg2Ui.PieceIcon(transform,item.DefinitionId,item.Side,0,0,16,20);icons.Add(item.Id,icon);}
                var pieceIcon=(Cfg2PieceIcon)icon;pieceIcon.Texture=Resources.Load<Texture2D>("PieceTextures/"+CfgPreviewRenderer.TextureFor(item.DefinitionId,item.Side));pieceIcon.SetMaterialDirty();
                GridRect(out float left,out float top,out float cell);var r=icon.rectTransform;r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(cell*.66f,cell*.79f);
                r.anchoredPosition=new Vector2(left+(item.Position[horizontal]+4.5f)*cell,-top-(4.5f-item.Position[vertical])*cell);
                r.localRotation=Quaternion.Euler(0,0,item.Side==Side.B?180:0);icon.gameObject.SetActive(true);
            }
            foreach(var entry in icons.Where(x=>!live.Contains(x.Key)).ToArray()){Destroy(entry.Value.gameObject);icons.Remove(entry.Key);}
            graphic.SetVerticesDirty();
        }
        static string AxisName(int axis)=>axis==0?"Z":axis==1?"X":axis==2?"Y":"W";
        public bool Contains(QN cell)
        {for(int axis=0;axis<4;axis++)if(axis!=horizontal&&axis!=vertical&&cell[axis]!=center[axis])return false;return true;}
        public QN Cell(int h,int v)
        {var q=center.ToArray();q[horizontal]=h;q[vertical]=v;return new QN(q[0],q[1],q[2],q[3]);}
        public Vector2 CellScreenCenter(QN cell,Camera eventCamera=null)
        {
            GridRect(out float left,out float top,out float pitch);
            return RectTransformUtility.WorldToScreenPoint(eventCamera,graphic.rectTransform.TransformPoint(new Vector3(left+(cell[horizontal]+4.5f)*pitch,-top-(4.5f-cell[vertical])*pitch)));
        }
        public bool TryCellAt(Vector2 screen,Camera eventCamera,out QN cell)
        {
            cell=default;if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(graphic.rectTransform,screen,eventCamera,out var local))return false;
            GridRect(out float left,out float top,out float pitch);float x=local.x-left,y=-local.y-top;
            if(x<0||y<0||x>=pitch*9||y>=pitch*9)return false;
            cell=Cell(Mathf.FloorToInt(x/pitch)-4,4-Mathf.FloorToInt(y/pitch));return true;
        }
        void OnDown(PointerEventData e)
        {
            if(e.button!=PointerEventData.InputButton.Left||hasDown||!TryCellAt(e.position,e.pressEventCamera,out var hit))return;
            if(ownership!=null&&!ownership.TryBegin(InputOwner.Board,e.pointerId,()=>hasDown=false))return;
            hasDown=true;pointer=e.pointerId;downCell=hit;downPosition=e.position;downRevision=revision;downContentVersion=contentVersion;
            downStateVersion=stateVersion?.Invoke()??0;downDraftVersion=draftVersion?.Invoke()??0;
        }
        void OnUp(PointerEventData e)
        {
            if(!hasDown||e.pointerId!=pointer)return;hasDown=false;
            if(ownership!=null&&!ownership.Release(InputOwner.Board,e.pointerId))return;
            if(downRevision!=revision||downContentVersion!=contentVersion||downStateVersion!=(stateVersion?.Invoke()??0)||downDraftVersion!=(draftVersion?.Invoke()??0)){RejectedStaleCount++;return;}
            if(Vector2.Distance(e.position,downPosition)>8||!TryCellAt(e.position,e.pressEventCamera,out var cell)||cell!=downCell)return;
            clicked?.Invoke(cell,downRevision);
        }
        void OnDisable(){if(hasDown&&ownership!=null&&ownership.IsOwner(InputOwner.Board,pointer))ownership.Cancel();hasDown=false;}
        void GridRect(out float left,out float top,out float pitch)
        {var rect=((RectTransform)transform).rect;float size=Mathf.Max(9,Mathf.Min(rect.width-16,rect.height-40));left=(rect.width-size)*.5f;top=19;pitch=size/9;}
        enum Lit {None,Geometric,Candidate,Danger,Selected}
        internal void Populate(VertexHelper vh)
        {
            vh.Clear();GridRect(out float left,out float top,out float pitch);float size=pitch*9;var rect=((RectTransform)transform).rect;
            Vector2 CellCenter(int h,int v)=>new Vector2(left+(h+4.5f)*pitch,-top-(4.5f-v)*pitch);
            // Deep backdrop with a cool light pool around the reference cell.
            Cfg2Glow.Gradient(vh,0,0,rect.width,rect.height,new Color(.03f,.06f,.13f,.60f),new Color(.01f,.02f,.05f,.60f));
            var focus=CellCenter(center[horizontal],center[vertical]);
            Cfg2Glow.Blob(vh,new Vector2(left+size*.5f,-top-size*.5f),size*.85f,new Color(.10f,.22f,.60f,.16f));
            Cfg2Glow.Blob(vh,focus,size*.55f,new Color(.16f,.36f,1,.26f));
            Cfg2Glow.Rect(vh,left-1.5f,-top+1.5f,size+3,size+3,new Color(.015f,.03f,.07f,.80f));
            float gap=showGrid?Mathf.Max(.6f,pitch*.04f):0;int maxDistance=1;
            var lit=new Lit[9,9];
            for(int h=-4;h<=4;h++)for(int v=-4;v<=4;v++)
            {
                var q=Cell(h,v);var kind=q==center?Lit.Selected:showAttacks&&attacks.Contains(q)?Lit.Danger:showCandidates&&ps.Contains(q)?Lit.Candidate:showCandidates&&gs.Contains(q)?Lit.Geometric:Lit.None;
                lit[h+4,v+4]=kind;if(kind==Lit.Candidate)maxDistance=Math.Max(maxDistance,Distance(h,v));
                float x=left+(h+4)*pitch+gap,y=-top-(4-v)*pitch-gap,w=pitch-gap*2;
                Cfg2Glow.Gradient(vh,x,y,w,w,new Color(.065f,.105f,.18f,.94f),new Color(.035f,.06f,.115f,.94f));
            }
            if(showGrid)for(int i=0;i<=9;i++)
            {
                var c=new Color(.32f,.55f,1,i==0||i==9?.38f:.17f);float o=i*pitch;
                Cfg2Glow.Line(vh,new Vector2(left+o,-top),new Vector2(left+o,-top-size),c,1.8f);Cfg2Glow.Line(vh,new Vector2(left,-top-o),new Vector2(left+size,-top-o),c,1.8f);
            }
            // Halos first so neighbouring tiles stay crisp on top of the bloom.
            for(int pass=0;pass<2;pass++)for(int h=-4;h<=4;h++)for(int v=-4;v<=4;v++)
            {
                var kind=lit[h+4,v+4];if(kind==Lit.None)continue;
                float k=kind==Lit.Candidate?1-.62f*(Distance(h,v)-1)/Math.Max(1,maxDistance-1):1;
                Color upper,lower,halo,edge;
                switch(kind)
                {
                    case Lit.Selected:upper=new Color(1,.84f,.45f,.97f);lower=new Color(.88f,.56f,.16f,.97f);halo=new Color(1,.70f,.24f,.55f);edge=new Color(1,.92f,.65f,.95f);break;
                    case Lit.Danger:upper=new Color(1,.40f,.36f,.92f);lower=new Color(.66f,.08f,.10f,.92f);halo=new Color(1,.22f,.18f,.45f);edge=new Color(1,.62f,.58f,.85f);break;
                    case Lit.Candidate:upper=new Color(.30f,.62f,1,.30f+.65f*k);lower=new Color(.08f,.30f,.92f,.30f+.65f*k);halo=new Color(.18f,.48f,1,.58f*k);edge=new Color(.62f,.85f,1,.85f*k);break;
                    default:upper=new Color(.14f,.26f,.52f,.72f);lower=new Color(.07f,.14f,.34f,.72f);halo=new Color(.18f,.36f,.90f,.12f);edge=new Color(.40f,.58f,.95f,.30f);break;
                }
                float x=left+(h+4)*pitch+gap,y=-top-(4-v)*pitch-gap,w=pitch-gap*2;
                if(pass==0){Cfg2Glow.Halo(vh,x,y,w,w,pitch*(kind==Lit.Geometric?.30f:.60f),halo);continue;}
                Cfg2Glow.Gradient(vh,x,y,w,w,upper,lower);
                Cfg2Glow.Blob(vh,CellCenter(h,v),w*.48f,new Color(1,1,1,kind==Lit.Geometric?.05f:.16f*k));
                float e=Mathf.Max(1.4f,pitch*.05f);
                Cfg2Glow.Line(vh,new Vector2(x,y),new Vector2(x+w,y),edge,e);Cfg2Glow.Line(vh,new Vector2(x,y-w),new Vector2(x+w,y-w),edge,e);
                Cfg2Glow.Line(vh,new Vector2(x,y),new Vector2(x,y-w),edge,e);Cfg2Glow.Line(vh,new Vector2(x+w,y),new Vector2(x+w,y-w),edge,e);
            }
        }
        int Distance(int h,int v)=>Math.Abs(h-center[horizontal])+Math.Abs(v-center[vertical]);
    }
    internal sealed class Cfg2PlaneGraphic : Graphic
    {
        public Cfg2PlaneView View;
        public override Texture mainTexture=>Cfg2Glow.Texture;
        protected override void OnPopulateMesh(VertexHelper vh){if(View==null)vh.Clear();else View.Populate(vh);}
        public static void Quad(VertexHelper vh,float x,float y,float w,float h,Color color)=>Cfg2Glow.Rect(vh,x,y,w,h,color);
    }
}
