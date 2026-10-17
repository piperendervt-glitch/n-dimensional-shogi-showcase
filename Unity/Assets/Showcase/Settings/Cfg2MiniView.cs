using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    // A finite reference-space intersection drawn around P, so moving C visibly moves the hyperplane. Pieces exist only in the main section.
    internal sealed class Cfg2MiniView : MonoBehaviour
    {
        SliceAnimator slice;InputOwnership ownership;ReferenceView reference;Cfg2RelationGraphic graphic;Text title;AxisDrag drag;Action changed;Action<ReferenceView> requested;Func<float> uiScale;
        readonly Dictionary<LogicalAxis,RectTransform> handles=new Dictionary<LogicalAxis,RectTransform>();
        readonly Dictionary<LogicalAxis,Vector2> tips=new Dictionary<LogicalAxis,Vector2>();
        Vector2 down;int pointer;LogicalAxis pressedAxis;bool pressed,moved;float dragScale=1;
        public ReferenceView Reference=>reference;
        public long ViewRevision {get;private set;}
        public long CameraRevision {get;private set;}
        public bool IsCoincident=>graphic!=null&&graphic.Relation!=null&&graphic.Relation.IsCoincident;
        public string FixedCoordinates {get;private set;}="";
        public HyperplaneRelation Relation=>graphic?.Relation;
        public Vector2 AxisScreenDirection(LogicalAxis axis)=>tips.TryGetValue(axis,out var v)?v:Vector2.zero;
        public void Initialize(SliceAnimator animator,InputOwnership input,ReferenceView view,Action geometryChanged,Action<ReferenceView> onRequested,Func<float> scale)
        {
            CancelDrag();Cfg2Ui.ClearOwned(transform);handles.Clear();tips.Clear();
            slice=animator;ownership=input;reference=view;changed=geometryChanged;requested=onRequested;uiScale=scale;drag=new AxisDrag(slice,ownership);
            float width=((RectTransform)transform).rect.width,height=((RectTransform)transform).rect.height;
            var button=Cfg2Ui.Button(transform,view.ToString(),0,0,width,26,()=>{if(ownership.Owner==InputOwner.None)requested(view);});
            button.GetComponent<Image>().color=new Color(0,0,0,0);button.GetComponentInChildren<Cfg2PanelBorder>().enabled=false;
            title=button.GetComponentInChildren<Text>();title.fontSize=14;title.rectTransform.anchoredPosition=new Vector2(11,-3);Cfg2Glow.GlowText(title,new Color(.55f,.65f,1));button.targetGraphic=title;
            var r=CfgUi.Rect(transform,"Finite C-centered intersection",0,26,width,height-26);graphic=r.gameObject.AddComponent<Cfg2RelationGraphic>();graphic.raycastTarget=false;graphic.Reference=view;
            var basis=SliceAxes.Reference(view);
            foreach(LogicalAxis axis in Enum.GetValues(typeof(LogicalAxis)))
            {
                if(Cfg2Projection.Tangent(basis,SliceAxes.WorldAxis(axis)).Length<.1)continue;
                var handle=Cfg2Ui.Panel(transform,"World axis "+axis,0,0,23,23,new Color(0,0,0,0),true);handle.GetComponentInChildren<Cfg2PanelBorder>().enabled=false;
                var label=CfgUi.Label(handle,axis.ToString(),0,0,23,23,15,Cfg2Projection.AxisColor(axis));label.alignment=TextAnchor.MiddleCenter;Cfg2Glow.GlowText(label,label.color);
                var surface=handle.gameObject.AddComponent<CfgPointerSurface>();var captured=axis;
                surface.Down=e=>OnDown(captured,e);surface.Drag=OnDrag;surface.Up=OnUp;handles.Add(axis,handle);
            }
        }
        void OnDown(LogicalAxis axis,PointerEventData e)
        {
            if(e.button!=PointerEventData.InputButton.Left||ownership.Owner!=InputOwner.None||pressed)return;
            down=e.position;pointer=e.pointerId;pressedAxis=axis;pressed=true;moved=false;dragScale=Mathf.Max(.01f,uiScale?.Invoke()??1);
            var direction=tips[axis];
            // Endpoints are the same ones rendered this frame; use Canvas pixels for both length and displacement.
            if(!drag.Begin(axis,e.pointerId,e.position.x/dragScale,e.position.y/dragScale,direction.x,direction.y))
            {if(!ownership.TryBegin(InputOwner.Axis,e.pointerId,()=>pressed=false))pressed=false;}
        }
        void OnDrag(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer||!ownership.IsOwner(InputOwner.Axis,e.pointerId))return;moved|=(e.position-down).magnitude/dragScale>6;
            if(moved&&drag.Update(e.pointerId,e.position.x/dragScale,e.position.y/dragScale,Cfg2Ui.SnapHeld))changed?.Invoke();
        }
        void OnUp(PointerEventData e)
        {
            if(!pressed||e.pointerId!=pointer)return;pressed=false;if(!ownership.IsOwner(InputOwner.Axis,e.pointerId))return;
            if(drag.IsActive)drag.Complete(e.pointerId);else ownership.Release(InputOwner.Axis,e.pointerId);
            if(!moved)requested(SliceAxes.ViewForAxis(pressedAxis));changed?.Invoke();
        }
        public void Refresh(SliceFrame frame,Quaternion cameraRotation,long cameraRevision,double halfWindow=4.5,Vec4? chartOrigin=null)
        {
            ViewRevision=slice.ViewRevision;CameraRevision=cameraRevision;graphic.CameraRotation=cameraRotation;
            graphic.Relation=HyperplaneRelation.Build(frame,reference,4.5,ViewRevision,halfWindow,chartOrigin);graphic.SetVerticesDirty();
            string axis=reference==ReferenceView.XYZ?"W":reference==ReferenceView.XYW?"Z":reference==ReferenceView.XZW?"Y":"X";
            double coordinate=reference==ReferenceView.XYZ?frame.Center.W:reference==ReferenceView.XYW?frame.Center.X:reference==ReferenceView.XZW?frame.Center.Z:frame.Center.Y;
            FixedCoordinates=axis+"="+coordinate.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
            title.text=reference+"   "+FixedCoordinates;title.color=IsCoincident?Cfg2Ui.Gold:new Color(.88f,.92f,.98f);
            foreach(var item in handles)
            {var tip=graphic.AxisEnd(item.Key);tips[item.Key]=tip;var center=graphic.Center;item.Value.anchoredPosition=new Vector2(center.x+tip.x-11.5f,-26+center.y+tip.y+11.5f);}
        }
        public bool IsPointerOver(Vector2 screen)=>gameObject.activeInHierarchy&&RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform,screen,null);
        void Update(){if(pressed&&!ownership.IsOwner(InputOwner.Axis,pointer)){pressed=false;changed?.Invoke();}}
        public void CancelDrag(){if(drag!=null)drag.Cancel();if(pressed&&ownership!=null&&ownership.IsOwner(InputOwner.Axis,pointer))ownership.Cancel();pressed=false;changed?.Invoke();}
        void OnDisable(){CancelDrag();}
    }
}
