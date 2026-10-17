using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Unity.Settings
{
    // Soft-light mesh helpers for the CFG2 diagrams. One shared radial texture: its centre is opaque,
    // so solid shapes sample (.5,.5) and glows stretch the falloff. Plain alpha blending, no extra shader.
    internal static class Cfg2Glow
    {
        static Texture2D texture;
        internal static readonly Vector2 Solid=new Vector2(.5f,.5f);
        internal static Texture2D Texture
        {
            get
            {
                if(texture!=null)return texture;
                const int size=64;
                texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="CFG2 UI soft glow",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
                var pixels=new Color32[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float dx=(x+.5f)/size*2-1,dy=(y+.5f)/size*2-1,r=Mathf.Sqrt(dx*dx+dy*dy);
                    float a=r<.06f?1:Mathf.Clamp01(1-r);a=a*a*(3-2*a);a*=a;
                    pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255));
                }
                texture.SetPixels32(pixels);texture.Apply(false,true);return texture;
            }
        }
        internal static Color Alpha(Color c,float a)=>new Color(c.r,c.g,c.b,c.a*a);
        internal static void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int i=vh.currentVertCount;vh.AddVert(a,color,Solid);vh.AddVert(b,color,Solid);vh.AddVert(c,color,Solid);vh.AddTriangle(i,i+1,i+2);}
        // x,y is the top-left corner in UI space where y grows upwards (rows go to -y).
        internal static void Rect(VertexHelper vh,float x,float y,float w,float h,Color color)=>Gradient(vh,x,y,w,h,color,color);
        internal static void Gradient(VertexHelper vh,float x,float y,float w,float h,Color top,Color bottom)
        {
            int i=vh.currentVertCount;
            vh.AddVert(new Vector3(x,y),top,Solid);vh.AddVert(new Vector3(x+w,y),top,Solid);vh.AddVert(new Vector3(x+w,y-h),bottom,Solid);vh.AddVert(new Vector3(x,y-h),bottom,Solid);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        // Soft-edged stroke: opaque only on its centre line, so the visible core is about half the width.
        internal static void Line(VertexHelper vh,Vector2 a,Vector2 b,Color color,float width)
        {
            var d=b-a;if(d.sqrMagnitude<1e-8f)return;var n=new Vector2(-d.y,d.x).normalized*width*.5f;int i=vh.currentVertCount;
            vh.AddVert(a-n,color,new Vector2(.5f,0));vh.AddVert(a+n,color,new Vector2(.5f,1));vh.AddVert(b+n,color,new Vector2(.5f,1));vh.AddVert(b-n,color,new Vector2(.5f,0));
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        // Wide coloured bloom, saturated stroke and a white-hot core.
        internal static void Neon(VertexHelper vh,Vector2 a,Vector2 b,Color color,float core=1.5f,float glow=9)
        {
            Line(vh,a,b,Alpha(color,.30f),glow);
            Line(vh,a,b,color,core*2.4f);
            Line(vh,a,b,new Color(Mathf.Lerp(color.r,1,.6f),Mathf.Lerp(color.g,1,.6f),Mathf.Lerp(color.b,1,.6f),color.a),core);
        }
        internal static void Dashed(VertexHelper vh,Vector2 a,Vector2 b,Color color,float width,float dash=4,float gap=4)
        {
            float length=(b-a).magnitude;if(length<1e-3f)return;var d=(b-a)/length;
            for(float t=0;t<length;t+=dash+gap)Line(vh,a+d*t,a+d*Mathf.Min(length,t+dash),color,width);
        }
        internal static void Blob(VertexHelper vh,Vector2 c,float radius,Color color)
        {
            int i=vh.currentVertCount;
            vh.AddVert(c+new Vector2(-radius,-radius),color,new Vector2(0,0));vh.AddVert(c+new Vector2(radius,-radius),color,new Vector2(1,0));
            vh.AddVert(c+new Vector2(radius,radius),color,new Vector2(1,1));vh.AddVert(c+new Vector2(-radius,radius),color,new Vector2(0,1));
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        // Rectangle that is solid inside and fades out over `spread` with rounded corners (nine-slice of the radial texture).
        internal static void Halo(VertexHelper vh,float x,float y,float w,float h,float spread,Color color)
        {
            float[] xs={x-spread,x,x+w,x+w+spread},ys={y+spread,y,y-h,y-h-spread},uv={0,.5f,.5f,1};
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
            {
                int n=vh.currentVertCount;
                vh.AddVert(new Vector3(xs[i],ys[j]),color,new Vector2(uv[i],uv[j]));vh.AddVert(new Vector3(xs[i+1],ys[j]),color,new Vector2(uv[i+1],uv[j]));
                vh.AddVert(new Vector3(xs[i+1],ys[j+1]),color,new Vector2(uv[i+1],uv[j+1]));vh.AddVert(new Vector3(xs[i],ys[j+1]),color,new Vector2(uv[i],uv[j+1]));
                vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
            }
        }
        internal static void Circle(VertexHelper vh,Vector2 center,float radius,Color color,float core=1,float glow=6,int steps=48)
        {
            for(int i=0;i<steps;i++)
            {
                float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;
                Neon(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,color,core,glow);
            }
        }
        internal static void Arrow(VertexHelper vh,Vector2 from,Vector2 to,Color color,float core=1.5f)
        {
            var v=to-from;float length=v.magnitude;if(length<3)return;var d=v/length;var n=new Vector2(-d.y,d.x);float head=Mathf.Min(10,length*.32f);
            var foot=to-d*head;
            Neon(vh,from,foot+d,color,core,10);
            Blob(vh,to-d*head*.45f,head*1.6f,Alpha(color,.38f));
            Triangle(vh,to,foot+n*head*.46f,foot-n*head*.46f,color);
            Triangle(vh,to-d*head*.25f,foot+d*head*.2f+n*head*.2f,foot+d*head*.2f-n*head*.2f,new Color(Mathf.Lerp(color.r,1,.6f),Mathf.Lerp(color.g,1,.6f),Mathf.Lerp(color.b,1,.6f),color.a));
        }
        // Coloured halo for UI text that floats over the glowing diagrams.
        internal static void GlowText(Text text,Color color)
        {
            text.fontStyle=FontStyle.Bold;
            var outline=text.GetComponent<Outline>();if(outline==null)outline=text.gameObject.AddComponent<Outline>();outline.effectColor=Alpha(color,.45f);outline.effectDistance=new Vector2(1.2f,-1.2f);
            var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.6f);shadow.effectDistance=new Vector2(1,-1.5f);
        }
    }
}
