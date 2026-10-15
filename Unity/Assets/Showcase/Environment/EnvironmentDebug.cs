using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NDimensionalShogi.Showcase.Environment
{
    public enum EnvironmentEffect { ToneMapping, IndirectLighting, DappledShadows, AmbientOcclusion, Reflections, Bloom, DepthOfField, SunShadows, ContactShadows }
    public enum EnvironmentDiagnostic { None, AmbientOcclusion, ContactShadows }
    public enum ShadowIsolation { Normal, NoEnvironmentCasters, NoSectionCasters, NoCasters, HighBias }

    /// <summary>Runtime controls shared by keyboard input and the verification harness.</summary>
    public sealed class EnvironmentDebug : MonoBehaviour
    {
        public const string Version="courtyard-lighting/2.0";
        EnvironmentRig rig;
        Light sun, bounce;
        HDAdditionalLightData sunHd;
        HDAdditionalReflectionData environmentProbe;
        Texture2D cookie;
        readonly bool[] effects={true,true,true,true,true,true,true,true,true};
        float windClock;
        bool frozen;
        EnvironmentDiagnostic diagnostic;
        RTHandle diagnosticBuffer;
        int diagnosticFrame=-1;
        readonly Dictionary<Renderer,ShadowCastingMode> originalCasters=new Dictionary<Renderer,ShadowCastingMode>();
        readonly ConcurrentQueue<ShadowReadback> shadowReadbacks=new ConcurrentQueue<ShadowReadback>();
        string shadowMetadataPath;
        bool shadowReadbackBusy;
        float savedNormalBias,savedSlopeBias;
        bool isolationSaved;
        public bool IsShadowReadbackBusy=>shadowReadbackBusy;
        public event Action<string> Changed;
        public bool IsDiagnosticActive=>diagnostic!=EnvironmentDiagnostic.None;
        public EnvironmentDiagnostic Diagnostic=>diagnostic;
        public bool AmbientMotionFrozen=>frozen;
        public string Summary=>"F2 ACES "+On(EnvironmentEffect.ToneMapping)+" | F3 indirect "+On(EnvironmentEffect.IndirectLighting)+" | F4 leaves "+On(EnvironmentEffect.DappledShadows)+
            " | F5 AO/contact "+On(EnvironmentEffect.AmbientOcclusion)+" | F6 reflections "+On(EnvironmentEffect.Reflections)+" | F7 bloom "+On(EnvironmentEffect.Bloom)+" | F8 DOF "+On(EnvironmentEffect.DepthOfField)+
            "\nShift+F5 AO buffer | Shift+F4 contact-shadow buffer | Ctrl+F4 sun shadow "+On(EnvironmentEffect.SunShadows)+" | Ctrl+F5 contact shadow "+On(EnvironmentEffect.ContactShadows)+" | diagnostic: "+diagnostic;
        string On(EnvironmentEffect effect)=>GetEffect(effect)?"ON":"OFF";

        public void Initialize(EnvironmentRig environment,Light directional,HDAdditionalLightData hdDirectional,Light warmBounce,HDAdditionalReflectionData staticProbe)
        {
            rig=environment;sun=directional;sunHd=hdDirectional;bounce=warmBounce;environmentProbe=staticProbe;
            cookie=MakeLeafCookie();sunHd.SetCookie(cookie,new Vector2(4.8f,4.8f));
            AddDiagnosticPass(CustomPassInjectionPoint.AfterOpaqueAndSky,false);
            AddDiagnosticPass(CustomPassInjectionPoint.AfterPostProcess,true);
        }
        void AddDiagnosticPass(CustomPassInjectionPoint injection,bool present)
        {
            var go=new GameObject(present?"Display actual lighting buffer":"Copy actual HDRP lighting buffer");go.transform.SetParent(transform,false);
            var volume=go.AddComponent<CustomPassVolume>();volume.isGlobal=true;volume.targetCamera=rig.Camera;volume.injectionPoint=injection;
            volume.customPasses.Add(new LightingDiagnosticPass{owner=this,present=present,name=go.name,
                targetColorBuffer=present?CustomPass.TargetBuffer.Camera:CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None});
        }
        public bool GetEffect(EnvironmentEffect effect)=>effects[(int)effect];
        public void SetEffect(EnvironmentEffect effect,bool enabled)
        {
            effects[(int)effect]=enabled;Apply();Changed?.Invoke("Effect "+effect+"="+enabled);
        }
        public void SetGeometryReference(bool enabled)
        {
            effects[(int)EnvironmentEffect.Bloom]=!enabled;effects[(int)EnvironmentEffect.DepthOfField]=!enabled;Apply();
        }
        public void SetDiagnostic(EnvironmentDiagnostic mode)
        {
            diagnostic=mode;diagnosticFrame=-1;Changed?.Invoke("Diagnostic "+mode);
        }
        public void FreezeAmbientMotion(bool freeze)=>frozen=freeze;
        // Deliberate render-only isolation for the opt-in lighting audit. Normal gameplay
        // never calls this API. It restores each original caster flag and exact bias.
        public void SetShadowIsolation(ShadowIsolation mode)
        {
            if(!isolationSaved)
            {
                originalCasters.Clear();
                foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))originalCasters[renderer]=renderer.shadowCastingMode;
                savedNormalBias=sunHd.normalBias;savedSlopeBias=sunHd.slopeBias;isolationSaved=true;
            }
            foreach(var item in originalCasters)
            {
                if(item.Key==null)continue;
                bool omit=mode==ShadowIsolation.NoCasters||mode==ShadowIsolation.NoEnvironmentCasters&&item.Key.gameObject.layer==EnvironmentRig.EnvironmentLayer||mode==ShadowIsolation.NoSectionCasters&&item.Key.gameObject.layer==0;
                item.Key.shadowCastingMode=omit?ShadowCastingMode.Off:item.Value;
            }
            sunHd.normalBias=mode==ShadowIsolation.HighBias?4:savedNormalBias;
            sunHd.slopeBias=mode==ShadowIsolation.HighBias?6:savedSlopeBias;
            if(mode==ShadowIsolation.Normal){isolationSaved=false;originalCasters.Clear();}
            Changed?.Invoke("Shadow isolation "+mode+"; normalBias="+sunHd.normalBias+"; slopeBias="+sunHd.slopeBias);
        }
        public void RequestShadowMetadata(string path)
        {
            if(shadowReadbackBusy)throw new InvalidOperationException("A shadow metadata readback is already pending.");
            shadowMetadataPath=path;shadowReadbackBusy=true;
        }
        void Toggle(EnvironmentEffect effect)=>SetEffect(effect,!GetEffect(effect));
        void ToggleDiagnostic(EnvironmentDiagnostic mode)=>SetDiagnostic(diagnostic==mode?EnvironmentDiagnostic.None:mode);
        void Update()
        {
            if(rig==null)return;
            while(shadowReadbacks.TryDequeue(out var readback))
            {
                try{File.WriteAllText(readback.path,JsonUtility.ToJson(readback,true));}
                finally{shadowReadbackBusy=false;}
            }
            bool shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(Input.GetKeyDown(KeyCode.F2))Toggle(EnvironmentEffect.ToneMapping);
            if(Input.GetKeyDown(KeyCode.F3))Toggle(EnvironmentEffect.IndirectLighting);
            if(Input.GetKeyDown(KeyCode.F4))
            {
                if(shift)ToggleDiagnostic(EnvironmentDiagnostic.ContactShadows);
                else Toggle(ctrl?EnvironmentEffect.SunShadows:EnvironmentEffect.DappledShadows);
            }
            if(Input.GetKeyDown(KeyCode.F5))
            {
                if(shift)ToggleDiagnostic(EnvironmentDiagnostic.AmbientOcclusion);
                else if(ctrl)Toggle(EnvironmentEffect.ContactShadows);
                else {bool next=!GetEffect(EnvironmentEffect.AmbientOcclusion);SetEffect(EnvironmentEffect.AmbientOcclusion,next);SetEffect(EnvironmentEffect.ContactShadows,next);}
            }
            if(Input.GetKeyDown(KeyCode.F6))Toggle(EnvironmentEffect.Reflections);
            if(Input.GetKeyDown(KeyCode.F7))Toggle(EnvironmentEffect.Bloom);
            if(Input.GetKeyDown(KeyCode.F8))Toggle(EnvironmentEffect.DepthOfField);
            if(!frozen)windClock+=Time.unscaledDeltaTime;
            // Moving a directional light's origin translates its cookie without changing
            // the sun direction, irradiance or physical geometry shadow matrices.
            float x=.045f*Mathf.Sin(windClock*.23f)+.016f*Mathf.Sin(windClock*.61f);
            float y=.035f*Mathf.Sin(windClock*.17f+.7f);
            sun.transform.position=sun.transform.right*x+sun.transform.up*y;
        }
        void Apply()
        {
            if(rig==null)return;
            var p=rig.Volume.profile;
            if(p.TryGet<Tonemapping>(out var tone))tone.mode.Override(GetEffect(EnvironmentEffect.ToneMapping)?TonemappingMode.ACES:TonemappingMode.None);
            if(p.TryGet<IndirectLightingController>(out var indirect))
            {
                indirect.indirectDiffuseLightingMultiplier.Override(GetEffect(EnvironmentEffect.IndirectLighting)?1.15f:0);
                indirect.reflectionLightingMultiplier.Override(GetEffect(EnvironmentEffect.Reflections)?1:0);
                indirect.reflectionProbeIntensityMultiplier.Override(GetEffect(EnvironmentEffect.Reflections)?1:0);
            }
            bounce.enabled=GetEffect(EnvironmentEffect.IndirectLighting);
            // HDRP 17.3 SetCookie dereferences its argument, so removing a cookie uses Light.cookie.
            if(GetEffect(EnvironmentEffect.DappledShadows))sunHd.SetCookie(cookie,new Vector2(4.8f,4.8f));else sun.cookie=null;
            sunHd.EnableShadows(GetEffect(EnvironmentEffect.SunShadows));
            if(p.TryGet<ScreenSpaceAmbientOcclusion>(out var ao))ao.intensity.Override(GetEffect(EnvironmentEffect.AmbientOcclusion)?1.2f:0);
            if(p.TryGet<ContactShadows>(out var contact))contact.enable.Override(GetEffect(EnvironmentEffect.ContactShadows));
            if(p.TryGet<Bloom>(out var bloom))bloom.active=GetEffect(EnvironmentEffect.Bloom);
            if(p.TryGet<DepthOfField>(out var dof))dof.active=GetEffect(EnvironmentEffect.DepthOfField);
            rig.DynamicReflection.enabled=GetEffect(EnvironmentEffect.Reflections);rig.TableReflection.enabled=GetEffect(EnvironmentEffect.Reflections);environmentProbe.enabled=GetEffect(EnvironmentEffect.Reflections);
        }
        static Texture2D MakeLeafCookie()
        {
            const int size=1024;var values=new float[size*size];for(int i=0;i<values.Length;i++)values[i]=1;
            var random=new System.Random(61943);
            // Original procedural projected laurel foliage; soft silhouette boundaries
            // represent the broad canopy penumbra, rather than a screen-space dark overlay.
            for(int cluster=0;cluster<9;cluster++)
            {
                float cx=(float)random.NextDouble()*size,cy=(float)random.NextDouble()*size;
                float branchAngle=(float)random.NextDouble()*Mathf.PI*2;
                for(int leaf=0;leaf<32;leaf++)
                {
                    float along=(float)random.NextDouble()*230-115,across=(float)random.NextDouble()*170-85;
                    float px=cx+Mathf.Cos(branchAngle)*along-Mathf.Sin(branchAngle)*across,py=cy+Mathf.Sin(branchAngle)*along+Mathf.Cos(branchAngle)*across;
                    float angle=branchAngle+(float)random.NextDouble()*2.2f-1.1f;
                    Ellipse(values,size,px,py,9+(float)random.NextDouble()*10,22+(float)random.NextDouble()*17,angle,.20f+(float)random.NextDouble()*.16f);
                }
            }
            // A few broad, softly projected leaves near the table's light-space position.
            // They modulate incident sunshine in world space, with no per-object exemption.
            Ellipse(values,size,476,643,15,28,.6f,.55f);
            Ellipse(values,size,538,666,12,30,-.3f,.63f);
            Ellipse(values,size,532,602,13,28,-.5f,.60f);
            var colors=new Color[values.Length];for(int i=0;i<colors.Length;i++)colors[i]=new Color(values[i],values[i],values[i],values[i]);
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true){name="Original laurel daylight cookie",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
            texture.SetPixels(colors);texture.Apply(true,true);return texture;
        }
        static void Ellipse(float[] values,int size,float x,float y,float rx,float ry,float angle,float attenuation)
        {
            int radius=Mathf.CeilToInt(Mathf.Max(rx,ry)*1.22f);float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
            for(int iy=-radius;iy<=radius;iy++)for(int ix=-radius;ix<=radius;ix++)
            {
                float u=(ix*c+iy*s)/rx,v=(-ix*s+iy*c)/ry,d=Mathf.Sqrt(u*u+v*v);
                if(d>=1.2f)continue;
                float edge=Mathf.SmoothStep(attenuation,1,Mathf.InverseLerp(.72f,1.2f,d));
                int px=((Mathf.FloorToInt(x)+ix)%size+size)%size,py=((Mathf.FloorToInt(y)+iy)%size+size)%size;
                values[py*size+px]=Mathf.Min(values[py*size+px],edge);
            }
        }
        void OnDestroy(){if(isolationSaved)SetShadowIsolation(ShadowIsolation.Normal);if(cookie!=null)Destroy(cookie);diagnosticBuffer?.Release();}

        [Serializable] sealed class ShadowReadback
        {
            public string path,source="GPU _DirectionalLightDatas[0] and _HDShadowDatas at AfterOpaqueAndSky; float32 raw values, four cascade rows",layout="8 RGBA texels per cascade: 0 index/worldTexelSize/normalBias/biasMetres; 1 shadowMapSize; 2 blockerSamples/filterSamples/filterParamX/filterParamW; 3 dirLightPCSSParams0; 4 dirLightPCSSParams1; 5 zBufferParam; 6 proj; 7 sunColorRGB/shadowDimmer";
            public string cpuConfiguration,error;
            public int captureFrame;
            public float[] values;
        }

        sealed class LightingDiagnosticPass : CustomPass
        {
            public EnvironmentDebug owner;
            public bool present;
            Material material;
            readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            protected override void Setup(ScriptableRenderContext renderContext,CommandBuffer cmd)
            {material=CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("ShowcaseLightingDiagnostic"));}
            protected override void Execute(CustomPassContext ctx)
            {
                if(owner==null||ctx.hdCamera.camera!=owner.rig.Camera)return;
                if(!present&&owner.shadowMetadataPath!=null)
                {
                    var metadata=new ShadowReadback{path=owner.shadowMetadataPath,captureFrame=Time.frameCount,
                        cpuConfiguration="normalBias="+owner.sunHd.normalBias+" slopeBias="+owner.sunHd.slopeBias+" blocker="+owner.sunHd.dirLightPCSSBlockerSampleCount+" filter="+owner.sunHd.dirLightPCSSFilterSampleCount+" intensity="+owner.sun.intensity+" unit="+owner.sun.lightUnit};
                    owner.shadowMetadataPath=null;
                    var target=new RenderTexture(8,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){name="Actual directional shadow parameters",filterMode=FilterMode.Point};target.Create();
                    HDUtils.DrawFullScreen(ctx.cmd,new Rect(0,0,8,4),material,new RenderTargetIdentifier(target),properties,2);
                    ctx.cmd.RequestAsyncReadback(target,0,TextureFormat.RGBAFloat,result=>
                    {
                        if(result.hasError)metadata.error="GPU_READBACK_FAILED";else metadata.values=result.GetData<float>().ToArray();
                        owner.shadowReadbacks.Enqueue(metadata);target.Release();UnityEngine.Object.Destroy(target);
                    });
                }
                if(!owner.IsDiagnosticActive)return;
                if(!present)
                {
                    if(owner.diagnosticBuffer==null)owner.diagnosticBuffer=RTHandles.Alloc(Vector2.one,new RTHandleAllocInfo("Actual AO or contact-shadow diagnostic")
                        {slices=TextureXR.slices,dimension=TextureXR.dimension,format=GraphicsFormat.R16G16B16A16_SFloat,useDynamicScale=true});
                    properties.SetInt("_EnvironmentDiagnosticMode",(int)owner.diagnostic);
                    HDUtils.DrawFullScreen(ctx.cmd,material,owner.diagnosticBuffer,properties,0);owner.diagnosticFrame=Time.frameCount;
                }
                else if(owner.diagnosticFrame==Time.frameCount)
                {
                    ctx.cmd.SetGlobalTexture("_EnvironmentDiagnostic",owner.diagnosticBuffer.nameID);
                    HDUtils.DrawFullScreen(ctx.cmd,material,ctx.cameraColorBuffer,properties,1);
                }
            }
            protected override void Cleanup()=>CoreUtils.Destroy(material);
        }
    }
}
