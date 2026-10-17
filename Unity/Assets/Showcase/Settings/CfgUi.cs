using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Showcase.Unity.Settings
{
    internal static class CfgUi
    {
        public static Font Font;
        public static readonly Color PanelColor = new Color(.055f,.075f,.105f,.95f);
        public static readonly Color Blue = new Color(.12f,.34f,.64f,.98f);
        public static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);
            r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        public static void Fill(RectTransform r)
        {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        public static void Line(Transform parent,Vector2 a,Vector2 b,Color color,float width)
        {var delta=b-a;var r=Rect(parent,"Axis guide",a.x,a.y,delta.magnitude,width);r.pivot=new Vector2(0,.5f);r.localRotation=Quaternion.Euler(0,0,-Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);var image=r.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
        public static RectTransform Panel(Transform parent,string name,float x,float y,float w,float h,Color? color=null)
        {
            var r=Rect(parent,name,x,y,w,h);r.gameObject.AddComponent<Image>().color=color??PanelColor;
            var border=r.gameObject.AddComponent<Outline>();border.effectColor=new Color(.38f,.46f,.56f,.6f);border.effectDistance=new Vector2(1,-1);return r;
        }
        public static Text Label(Transform parent,string text,float x,float y,float w,float h,int size=18,Color? color=null)
        {
            var r=Rect(parent,"Text "+text,x,y,w,h);var t=r.gameObject.AddComponent<Text>();
            t.font=Font;t.text=text;t.fontSize=size;t.color=color??new Color(.94f,.95f,.97f);t.raycastTarget=false;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }
        public static Button Button(Transform parent,string title,float x,float y,float w,float h,Action action,bool accent=false)
        {
            var r=Panel(parent,title,x,y,w,h,accent?Blue:new Color(.10f,.14f,.20f,.98f));var b=r.gameObject.AddComponent<Button>();NoNavigation(b);
            b.targetGraphic=r.GetComponent<Image>();var c=b.colors;c.highlightedColor=new Color(.7f,.85f,1);c.pressedColor=new Color(.4f,.65f,1);b.colors=c;
            var text=Label(r,title,7,3,w-14,h-6,17);text.alignment=TextAnchor.MiddleCenter;
            b.onClick.AddListener(()=>action());return b;
        }
        public static InputField Input(Transform parent,string value,float x,float y,float w,float h,Action<string> changed,bool live=false)
        {
            var r=Panel(parent,"Input",x,y,w,h,new Color(.025f,.04f,.065f,.98f));var i=r.gameObject.AddComponent<InputField>();
            var t=Label(r,"",8,4,w-16,h-8,18);t.supportRichText=false;t.verticalOverflow=VerticalWrapMode.Overflow;
            i.textComponent=t;i.targetGraphic=r.GetComponent<Image>();i.text=value;i.characterLimit=1000;
            if(live)i.onValueChanged.AddListener(v=>changed(v));else i.onEndEdit.AddListener(v=>changed(v));return i;
        }
        public static Toggle Toggle(Transform parent,string title,bool value,float x,float y,float w,Action<bool> changed)
        {
            var r=Rect(parent,title,x,y,w,32);var bg=Panel(r,"Check",0,5,22,22,new Color(.12f,.17f,.25f));
            var check=Label(bg,"✓",1,-2,22,27,22,new Color(.55f,.8f,1));check.raycastTarget=false;
            var label=Label(r,title,31,0,w-31,32,17);label.alignment=TextAnchor.MiddleLeft;
            var toggle=r.gameObject.AddComponent<Toggle>();NoNavigation(toggle);toggle.targetGraphic=bg.GetComponent<Image>();toggle.graphic=check;toggle.isOn=value;
            toggle.onValueChanged.AddListener(v=>changed(v));return toggle;
        }
        public static ScrollRect Scroll(Transform parent,string name,float x,float y,float w,float h,out RectTransform content,float contentHeight)
        {
            var root=Rect(parent,name,x,y,w,h);var scroll=root.gameObject.AddComponent<ScrollRect>();
            var viewport=Rect(root,"Viewport",0,0,w,h);viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color=new Color(0,0,0,.01f);
            content=Rect(viewport,"Content",0,0,w-14,contentHeight);scroll.viewport=viewport;scroll.content=content;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
            return scroll;
        }
        public static void Clear(Transform root)
        {ReleaseSelection(root);for(int n=root.childCount-1;n>=0;n--)UnityEngine.Object.Destroy(root.GetChild(n).gameObject);}
        // uGUI keeps a destroyed selection as Unity's fake null, which ?. and ?? do not see: only Unity's == is used here.
        public static GameObject Selected{get{var es=EventSystem.current;if(es==null)return null;var go=es.currentSelectedGameObject;return go!=null?go:null;}}
        public static InputField SelectedInput{get{var go=Selected;return go!=null&&go.TryGetComponent<InputField>(out var field)?field:null;}}
        public static bool EditingText{get{var field=SelectedInput;return field!=null&&field.isFocused;}}
        // Drops the selection before a UI subtree holding it is destroyed. A selection that is already destroyed is
        // cleared too: SetSelectedGameObject(null) alone ignores it, because uGUI compares it with Unity's == as well.
        public static void ReleaseSelection(Transform subtree=null)
        {
            var es=EventSystem.current;if(es==null)return;var go=es.currentSelectedGameObject;
            if(!ReferenceEquals(go,null)&&go==null){es.SetSelectedGameObject(es.gameObject);es.SetSelectedGameObject(null);return;}
            if(go!=null&&(subtree==null||go.transform.IsChildOf(subtree)))es.SetSelectedGameObject(null);
        }
        // Clicks never select a control without keyboard navigation, so Enter/Space reach only the app's own key handling
        // and no selection is left on a control that a rebuild destroys. Input fields still select themselves to take text.
        public static void NoNavigation(Selectable s){var n=s.navigation;n.mode=Navigation.Mode.None;s.navigation=n;}
    }

    // A pointer retains the surface on which it began. EventSystem consumes forms separately.
    internal sealed class CfgPointerSurface : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler,IScrollHandler
    {
        public Action<PointerEventData> Down,Up,Drag,Scroll;
        public void OnPointerDown(PointerEventData e)=>Down?.Invoke(e);
        public void OnPointerUp(PointerEventData e)=>Up?.Invoke(e);
        public void OnDrag(PointerEventData e)=>Drag?.Invoke(e);
        public void OnScroll(PointerEventData e)=>Scroll?.Invoke(e);
    }
}
