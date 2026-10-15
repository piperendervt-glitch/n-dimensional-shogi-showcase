using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Showcase.Unity
{
    // Presentation-only: reads the current final color once; no previous-frame image,
    // depth write, object duplication, state mutation, or geometry substitution.
    public sealed class DimensionalTransition : MonoBehaviour
    {
        Camera target;
        CustomPassVolume volume;
        public bool EffectEnabled=true;
        public bool Suppressed;
        public float Strength {get;private set;}
        public float Phase {get;private set;}
        public float Travel {get;private set;}
        public void Bind(Camera camera)
        {
            target=camera;
            volume=gameObject.AddComponent<CustomPassVolume>();volume.isGlobal=true;
            volume.targetCamera=camera;volume.injectionPoint=CustomPassInjectionPoint.AfterPostProcess;
            volume.customPasses.Add(new TransitionPass{owner=this,name="Dimensional time suspension / current frame only",
                targetColorBuffer=CustomPass.TargetBuffer.Camera,targetDepthBuffer=CustomPass.TargetBuffer.None});
        }
        public void Set(float strength,float phase,float travel)
        {Strength=Mathf.Clamp01(strength);Phase=phase;Travel=Mathf.Clamp01(travel);}
        sealed class TransitionPass : CustomPass
        {
            public DimensionalTransition owner;
            Material material;
            RTHandle copy;
            readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            protected override void Setup(ScriptableRenderContext renderContext,CommandBuffer cmd)
            {
                material=CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("ShowcaseDimensionalTransition"));
                copy=RTHandles.Alloc(Vector2.one,new RTHandleAllocInfo("Current frame transition color")
                    {slices=TextureXR.slices,dimension=TextureXR.dimension,format=GraphicsFormat.R16G16B16A16_SFloat,useDynamicScale=true});
            }
            protected override void Execute(CustomPassContext ctx)
            {
                if(owner==null||!owner.EffectEnabled||owner.Suppressed||owner.Strength<=0||ctx.hdCamera.camera!=owner.target)return;
                ctx.cmd.SetGlobalTexture("_DimensionSource",ctx.cameraColorBuffer.nameID);
                HDUtils.DrawFullScreen(ctx.cmd,material,copy,properties,0);
                ctx.cmd.SetGlobalTexture("_DimensionSource",copy.nameID);
                properties.SetVector("_DimensionSize",new Vector4(ctx.hdCamera.actualWidth,ctx.hdCamera.actualHeight,1f/ctx.hdCamera.actualWidth,1f/ctx.hdCamera.actualHeight));
                properties.SetVector("_DimensionState",new Vector4(owner.Strength,owner.Phase,owner.Travel,0));
                HDUtils.DrawFullScreen(ctx.cmd,material,ctx.cameraColorBuffer,properties,1);
            }
            protected override void Cleanup(){CoreUtils.Destroy(material);copy?.Release();}
        }
    }
}
