using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // A finite reference-space intersection drawn around P, so moving C visibly moves the hyperplane. Pieces exist only in the main section.
    // In 4D the four panels are the four reference views; in higher N one panel is the current section and the rest are one axis swap away.
    internal sealed class Cfg2MiniView : MonoBehaviour
    {
        Func<SliceAnimator> currentSlice;InputOwnership ownership;ReferenceSection reference;Cfg2RelationGraphic graphic;Text title;Cfg2AxisDragHandle drag;Action<ReferenceSection> requested;
        readonly Dictionary<LogicalAxis,RectTransform> handles=new Dictionary<LogicalAxis,RectTransform>();
        readonly Dictionary<LogicalAxis,Vector2> tips=new Dictionary<LogicalAxis,Vector2>();
        public ReferenceSection Reference=>reference;
        public long ViewRevision {get;private set;}
        public long CameraRevision {get;private set;}
        public long PiecesRevision {get;private set;}
        public bool IsCoincident=>graphic!=null&&graphic.Relation!=null&&graphic.Relation.IsCoincident;
        public string FixedCoordinates {get;private set;}="";
        public HyperplaneRelation Relation=>graphic!=null?graphic.Relation:null;
        // Small swap panels in higher N fade while their intersection is only a line or a point, so the planes stand out.
        public bool DimWhenFlat {get;set;}
        public bool IsFlat=>graphic!=null&&graphic.Relation!=null&&!graphic.Relation.IsCoincident&&graphic.Relation.TriangleIndices.Count==0;
        CanvasGroup fade;
        public Vector2 AxisScreenDirection(LogicalAxis axis)=>tips.TryGetValue(axis,out var v)?v:Vector2.zero;
        // The host panel's own frame survives (ClearOwned keepFrame): its owner colours it for the section shown.
        public void Initialize(Func<SliceAnimator> slice,InputOwnership input,ReferenceSection view,Action geometryChanged,Action<ReferenceSection> onRequested,Func<float> scale)
        {
            CancelDrag();Cfg2Ui.ClearOwned(transform,keepFrame:true);handles.Clear();tips.Clear();legend=null;
            currentSlice=slice;ownership=input;reference=view;requested=onRequested;
            // An axis label click selects the section that axis stands for, relative to the section the display is heading to.
            drag=new Cfg2AxisDragHandle(slice,ownership,scale,geometryChanged,axis=>requested(SliceAxes.SectionForAxis(slice().TargetSection,axis)));
            float width=((RectTransform)transform).rect.width,height=((RectTransform)transform).rect.height;bool compact=width<150;
            var button=Cfg2Ui.Button(transform,view.Name,0,0,width,26,()=>{if(ownership.Owner==InputOwner.None)requested(reference);});
            button.GetComponent<Image>().color=new Color(0,0,0,0);button.GetComponentInChildren<Cfg2PanelBorder>().enabled=false;
            title=button.GetComponentInChildren<Text>();title.fontSize=compact?11:14;title.rectTransform.anchoredPosition=new Vector2(compact?6:11,-3);Cfg2Glow.GlowText(title,new Color(.55f,.65f,1));button.targetGraphic=title;
            var r=CfgUi.Rect(transform,"Finite C-centered intersection",0,26,width,height-26);graphic=r.gameObject.AddComponent<Cfg2RelationGraphic>();graphic.raycastTarget=false;graphic.Reference=view;
            BuildHandles();
            // A left click anywhere on the panel (not a drag) turns the board's section to this panel's section, like its title.
            if(!TryGetComponent<CfgPointerSurface>(out var surface))surface=gameObject.AddComponent<CfgPointerSurface>();
            surface.Down=e=>{if(e.button==PointerEventData.InputButton.Left)pressAt=e.position;};
            surface.Up=e=>{if(e.button==PointerEventData.InputButton.Left&&pressAt.HasValue&&(e.position-pressAt.Value).sqrMagnitude<64&&IsPointerOver(e.position)&&ownership.Owner==InputOwner.None)requested(reference);pressAt=null;};
        }
        Vector2? pressAt;
        // Another section in the same panel: the title and the drawing stay, the axis handles follow the section's axes.
        // The next Refresh draws it.
        public void Retarget(ReferenceSection view)
        {
            if(view==reference||graphic==null)return;
            CancelDrag();reference=view;graphic.Reference=view;gameObject.name="Hyperplane "+view.Name;
            foreach(var handle in handles.Values){CfgUi.ReleaseSelection(handle);handle.gameObject.SetActive(false);Destroy(handle.gameObject);}handles.Clear();tips.Clear();
            BuildHandles();
        }
        void BuildHandles()
        {
            bool compact=((RectTransform)transform).rect.width<150;var basis=reference.Frame();
            foreach(LogicalAxis axis in SliceAxes.Axes(reference.Dimensions))
            {
                if(Cfg2Projection.Tangent(basis,VecN.Unit(SliceAxes.CoreAxis(axis),reference.Dimensions)).Length<.1)continue;
                var handle=Cfg2Ui.Panel(transform,"World axis "+axis,0,0,23,23,new Color(0,0,0,0),true);handle.GetComponentInChildren<Cfg2PanelBorder>().enabled=false;
                var label=CfgUi.Label(handle,SliceAxes.AxisName(axis),0,0,23,23,compact?12:15,Cfg2Projection.AxisColor(axis));label.alignment=TextAnchor.MiddleCenter;Cfg2Glow.GlowText(label,label.color);
                var surface=handle.gameObject.AddComponent<CfgPointerSurface>();var captured=axis;
                surface.Down=e=>drag.Down(captured,e,AxisScreenDirection(captured));surface.Drag=drag.Drag;surface.Up=drag.Up;handles.Add(axis,handle);
            }
        }
        // pieces (world positions) are drawn as dots on the panel's own axes: each piece's coordinates on the three shown axes,
        // whatever its hidden coordinates, so the panel shows where every piece lies in that axis space. Null draws no dots.
        public void Refresh(SliceFrame frame,Quaternion cameraRotation,long cameraRevision,double halfWindow=4.5,VecN? chartOrigin=null,IReadOnlyList<(VecN world,Side side,bool selected)> pieces=null,long piecesRevision=0)
        {
            ViewRevision=currentSlice().ViewRevision;CameraRevision=cameraRevision;PiecesRevision=piecesRevision;graphic.CameraRotation=cameraRotation;
            graphic.Relation=HyperplaneRelation.Build(frame,reference,4.5,ViewRevision,halfWindow,chartOrigin);
            if(pieces==null)graphic.Dots=null;
            else
            {
                // Only the shown axes matter, so the chart basis may sit anywhere along the hidden ones.
                var basis=reference.In(frame.Dimensions).Frame(chartOrigin??frame.Center);var dots=new Cfg2PanelDot[pieces.Count];
                for(int i=0;i<pieces.Count;i++)dots[i]=new Cfg2PanelDot(basis.Project(pieces[i].world),pieces[i].side,pieces[i].selected);
                graphic.Dots=dots;
            }
            graphic.SetVerticesDirty();UpdateLegend(pieces!=null);
            // Every axis the panel holds fixed, with C's coordinate on it: W=0 in 4D, e.g. Z=0  V=0 for an XYW panel in 5D.
            FixedCoordinates=string.Join("  ",reference.Hidden.Select(axis=>SliceAxes.AxisName(SliceAxes.AxisOfCore(axis))+"="+SliceAxes.Coordinate(frame.Center,axis).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)));
            title.text=reference.Name+"   "+FixedCoordinates;title.color=IsCoincident?Cfg2Ui.Gold:new Color(.88f,.92f,.98f);
            if(DimWhenFlat&&fade==null)fade=gameObject.AddComponent<CanvasGroup>();
            foreach(var item in handles)
            {var tip=graphic.AxisEnd(item.Key);tips[item.Key]=tip;var center=graphic.Center;item.Value.anchoredPosition=new Vector2(center.x+tip.x-11.5f,-26+center.y+tip.y+11.5f);}
        }
        // A large panel (the magnifier) names the two dot colours along its bottom edge.
        Text legend;
        void UpdateLegend(bool dots)
        {
            var rect=((RectTransform)transform).rect;bool show=dots&&rect.width>=250&&rect.height>=250;
            if(show&&legend==null)
            {
                string Hex(Color c)=>ColorUtility.ToHtmlStringRGB(c);
                legend=CfgUi.Label(transform,"<color=#"+Hex(Cfg2PanelDot.SideA)+">●</color> 先手 A　<color=#"+Hex(Cfg2PanelDot.SideB)+">●</color> 後手 B",10,rect.height-24,rect.width-20,20,13,new Color(.85f,.89f,.95f));
                legend.supportRichText=true;legend.raycastTarget=false;
            }
            if(legend!=null)legend.gameObject.SetActive(show);
        }
        public bool IsPointerOver(Vector2 screen)=>gameObject.activeInHierarchy&&RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform,screen,null);
        void Update()
        {
            drag?.Update();
            if(fade!=null)fade.alpha=Mathf.MoveTowards(fade.alpha,DimWhenFlat&&IsFlat?.45f:1,Time.unscaledDeltaTime*4);
        }
        public void CancelDrag()=>drag?.Cancel();
        void OnDisable(){CancelDrag();}
    }
}
