using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Showcase.Unity
{
    /// <summary>Observes HDRP's actual render size. Optional offscreen benchmarking never changes desktop settings.</summary>
    public sealed class ShowcaseRenderOutput : MonoBehaviour
    {
        Camera targetCamera;
        RenderTexture target;
        public int RequestedWidth { get; private set; }
        public int RequestedHeight { get; private set; }
        public int RenderedWidth { get; private set; }
        public int RenderedHeight { get; private set; }
        public int RenderedFrame { get; private set; }=-1;
        public string Surface=>target==null?"native window":"offscreen HDRP render target with scaled window preview; not native-resolution monitor presentation";

        public void Initialize(Camera camera,bool benchmark)
        {
            targetCamera=camera;
            var args=System.Environment.GetCommandLineArgs();
            RequestedWidth=ReadDimension(args,"--benchmark-render-width");
            RequestedHeight=ReadDimension(args,"--benchmark-render-height");
            if((RequestedWidth==0)!=(RequestedHeight==0))throw new ArgumentException("Specify both benchmark render dimensions.");
            if(RequestedWidth!=0)
            {
                if(!benchmark)throw new ArgumentException("Offscreen render dimensions require --benchmark-no-captures; this is a separate performance configuration.");
                if(targetCamera.targetTexture!=null)throw new InvalidOperationException("Cannot replace an existing camera target.");
                target=new RenderTexture(RequestedWidth,RequestedHeight,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
                {name="Explicit benchmark render resolution",antiAliasing=1,useMipMap=false,autoGenerateMips=false};
                if(!target.Create())throw new InvalidOperationException("Could not create the requested benchmark render target.");
                targetCamera.targetTexture=target;
                targetCamera.aspect=(float)RequestedWidth/RequestedHeight;
            }
            RenderPipelineManager.endCameraRendering+=ObserveRender;
        }

        static int ReadDimension(string[] args,string flag)
        {
            int index=Array.IndexOf(args,flag);
            if(index<0)return 0;
            if(index+1>=args.Length||!int.TryParse(args[index+1],out int value)||value<320||value>8192)
                throw new ArgumentException(flag+" requires an integer from 320 to 8192.");
            return value;
        }

        void ObserveRender(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=targetCamera)return;
            var hd=HDCamera.GetOrCreate(camera);
            RenderedWidth=hd.actualWidth;RenderedHeight=hd.actualHeight;RenderedFrame=Time.frameCount;
        }

        public void DrawPreview()
        {
            if(target==null)return;
            float scale=Mathf.Min((float)Screen.width/target.width,(float)Screen.height/target.height);
            float width=target.width*scale,height=target.height*scale;
            GUI.DrawTexture(new Rect((Screen.width-width)*.5f,(Screen.height-height)*.5f,width,height),target,ScaleMode.StretchToFill,false);
        }

        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering-=ObserveRender;
            if(target==null)return;
            if(targetCamera!=null&&targetCamera.targetTexture==target)targetCamera.targetTexture=null;
            target.Release();Destroy(target);
        }
    }
}
