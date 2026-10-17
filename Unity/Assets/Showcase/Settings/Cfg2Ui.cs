using System;
using UnityEngine;
using UnityEngine.UI;
using NDShogi.Showcase.Core;

namespace Showcase.Unity.Settings
{
    // CFG2 appearance helpers use existing fonts and wood textures only.
    internal static class Cfg2Ui
    {
        public static readonly Color Gold=new Color(.91f,.79f,.52f,1);
        // Shift or Ctrl while dragging an axis snaps the 4D centre to whole cells.
        public static bool SnapHeld=>Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)||Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
        public static readonly Color Blue=new Color(.12f,.30f,.51f,.88f);
        public static readonly Color Dark=new Color(.035f,.055f,.085f,.84f);
        static Sprite rounded;
        static Sprite Rounded
        {
            get
            {
                if(rounded!=null)return rounded;
                var texture=new Texture2D(20,20,TextureFormat.RGBA32,false){name="CFG2 UI rounded mask",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                for(int y=0;y<20;y++)for(int x=0;x<20;x++)
                {float dx=Mathf.Max(0,5.5f-Mathf.Min(x,19-x)),dy=Mathf.Max(0,5.5f-Mathf.Min(y,19-y));float alpha=Mathf.Clamp01(6-Mathf.Sqrt(dx*dx+dy*dy));texture.SetPixel(x,y,new Color(1,1,1,alpha));}
                texture.Apply();rounded=Sprite.Create(texture,new Rect(0,0,20,20),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(7,7,7,7));return rounded;
            }
        }
        internal static void ClearOwned(Transform root)
        {for(int i=root.childCount-1;i>=0;i--){var child=root.GetChild(i).gameObject;child.SetActive(false);UnityEngine.Object.Destroy(child);}}
        public static RectTransform Panel(Transform parent,string name,float x,float y,float w,float h,Color? color=null,bool blocksPointer=false)
        {
            var tint=color??Dark;if(tint.a>.4f)tint.a=Mathf.Max(tint.a,.95f);
            var panel=CfgUi.Panel(parent,name,x,y,w,h,tint);
            var image=panel.GetComponent<Image>();image.sprite=Rounded;image.type=Image.Type.Sliced;image.raycastTarget=blocksPointer;
            // Unity's Outline duplicates the entire alpha panel, which tints the
            // whole interior gold. Draw only the geometric perimeter instead.
            panel.GetComponent<Outline>().enabled=false;
            var edge=CfgUi.Rect(panel,"Thin panel border",0,0,w,h);CfgUi.Fill(edge);var border=edge.gameObject.AddComponent<Cfg2PanelBorder>();border.color=new Color(.67f,.59f,.42f,.6f);border.raycastTarget=false;return panel;
        }
        // Horizontal slider in the CFG2 palette; changed receives the raw value while dragging.
        public static Slider Slider(Transform parent,float x,float y,float w,float min,float max,float value,Action<float> changed)
        {
            var root=CfgUi.Rect(parent,"Slider",x,y,w,24);
            var track=CfgUi.Rect(root,"Track",0,9,w,6);track.gameObject.AddComponent<Image>().color=new Color(.10f,.14f,.20f,.95f);
            var fillArea=CfgUi.Rect(root,"Fill area",0,9,w,6);
            var fill=CfgUi.Rect(fillArea,"Fill",0,0,0,6);fill.gameObject.AddComponent<Image>().color=new Color(.30f,.62f,1,.95f);
            var handleArea=CfgUi.Rect(root,"Handle area",9,0,w-18,24);
            var handle=CfgUi.Rect(handleArea,"Handle",0,0,18,24);handle.pivot=new Vector2(.5f,1);handle.anchoredPosition=Vector2.zero;var knob=handle.gameObject.AddComponent<Image>();knob.sprite=Rounded;knob.type=Image.Type.Sliced;knob.color=Gold;
            var slider=root.gameObject.AddComponent<Slider>();slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=knob;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue=min;slider.maxValue=max;slider.value=value;slider.onValueChanged.AddListener(v=>changed(v));return slider;
        }
        public static Text Title(Transform parent,string title,float x,float y,float w,float h,int size=28)
            =>CfgUi.Label(parent,title,x,y,w,h,size,Gold);
        public static Button Button(Transform parent,string title,float x,float y,float w,float h,Action clicked,bool selected=false)
        {
            var panel=Panel(parent,title,x,y,w,h,selected?Blue:new Color(.05f,.07f,.1f,.76f),true);
            var button=panel.gameObject.AddComponent<Button>();button.targetGraphic=panel.GetComponent<Image>();
            if(selected){var outline=panel.GetComponentInChildren<Cfg2PanelBorder>();outline.color=new Color(.16f,.58f,1,.95f);outline.Width=1.7f;outline.Glow=true;}
            var colors=button.colors;colors.highlightedColor=new Color(.76f,.88f,1);colors.pressedColor=new Color(.48f,.7f,1);button.colors=colors;
            var label=CfgUi.Label(panel,title,7,3,w-14,h-6,18);label.alignment=TextAnchor.MiddleLeft;
            button.onClick.AddListener(()=>clicked());return button;
        }
        public static Graphic PieceIcon(Transform parent,string definition,Side owner,float x,float y,float w,float h)
        {
            var rect=CfgUi.Rect(parent,"Wood piece "+definition,x,y,w,h);
            var graphic=rect.gameObject.AddComponent<Cfg2PieceIcon>();graphic.Texture=Resources.Load<Texture2D>("PieceTextures/"+CfgPreviewRenderer.TextureFor(definition,owner));
            if(graphic.Texture==null)throw new InvalidOperationException("Missing existing wood piece texture: "+definition);
            graphic.raycastTarget=false;return graphic;
        }
    }
    internal sealed class Cfg2PanelBorder : MaskableGraphic
    {
        public float Width=1;public bool Glow;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;float radius=5;var points=new System.Collections.Generic.List<Vector2>();
            var centers=new[]{new Vector2(r.xMax-radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMin+radius),new Vector2(r.xMax-radius,r.yMin+radius)};
            for(int corner=0;corner<4;corner++)for(int step=0;step<=8;step++){float angle=(corner*90+step*90f/8)*Mathf.Deg2Rad;points.Add(centers[corner]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius);}
            for(int i=0;i<points.Count;i++){var a=points[i];var b=points[(i+1)%points.Count];if(Glow)Cfg2MeshUi.Line(vh,a,b,new Color(color.r,color.g,color.b,.15f),6);Cfg2MeshUi.Line(vh,a,b,color,Width);}
        }
    }
    internal sealed class Cfg2PieceIcon : MaskableGraphic
    {
        public Texture2D Texture;
        public override Texture mainTexture=>Texture!=null?Texture:base.mainTexture;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            var points=new[]{new Vector2(.5f,1),new Vector2(.93f,.76f),new Vector2(.85f,0),new Vector2(.15f,0),new Vector2(.07f,.76f)};
            foreach(var p in points)vh.AddVert(new Vector3(r.x+p.x*r.width,r.y+p.y*r.height),color,p);
            for(int i=1;i<4;i++)vh.AddTriangle(0,i,i+1);
        }
    }
}
