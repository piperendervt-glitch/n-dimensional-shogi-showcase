using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity
{
    [Serializable] public sealed class AblationSample
    {
        public string name,description;
        public int frames;
        public double frameMedianMs,frameP95Ms,gpuMedianMs,gpuP95Ms,cpuMainMedianMs,cpuMainP95Ms,cpuRenderMedianMs,uncappedFps;
        public double gpuDeltaFromBaselineMs;
    }
    [Serializable] public sealed class AblationReport
    {
        public string schema="showcase-gpu-ablation/1",startedUtc,finishedUtc,device,driver,unity,buildGuid;
        public int width,height,warmupFrames,sampleFrames;
        public string method="Fixed camera, fixed H0 state, uncapped frame rate (vSync 0, targetFrameRate 0). Each state is applied, warmed up, sampled with FrameTimingManager, then restored before the next. gpuDeltaFromBaselineMs = state gpu median - baseline gpu median (negative = cheaper than baseline). Single-state deltas do not add linearly.";
        public AblationSample[] samples;
    }

    // Opt-in GPU cost attribution. Never runs during a normal showcase. Every state is
    // applied through the same runtime objects the showcase uses and reverted afterwards.
    public static class GpuAblation
    {
        public const int WarmupFrames=30,SampleFrames=120;
        sealed class State
        {
            public string Name,Description;
            public Action Apply,Revert;
        }
        public static IEnumerator Run(EnvironmentRig rig,RuntimeEvidence evidence,DimensionalTransition transition,string path,Action<string> log)
        {
            var report=new AblationReport{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,driver=SystemInfo.graphicsDeviceVersion,unity=Application.unityVersion,
                buildGuid=Application.buildGUID,width=Screen.width,height=Screen.height,warmupFrames=WarmupFrames,sampleFrames=SampleFrames};
            var samples=new List<AblationSample>();
            int previousTarget=Application.targetFrameRate;Application.targetFrameRate=0;QualitySettings.vSyncCount=0;
            var profile=rig.Volume.profile;var sun=rig.SunData;var cube=rig.DynamicReflection;var planar=rig.TableReflection;
            var hdCamera=rig.Camera.GetComponent<HDAdditionalCameraData>();
            var foliage=rig.Root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Laurel foliage")).ToArray();
            var environmentRenderers=rig.Root.GetComponentsInChildren<MeshRenderer>().ToArray();
            profile.TryGet<HDShadowSettings>(out var shadows);profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao);profile.TryGet<ContactShadows>(out var contact);
            int blocker=sun.dirLightPCSSBlockerSampleCount,filter=sun.dirLightPCSSFilterSampleCount,cascades=shadows.cascadeShadowSplitCount.value,shadowResolution=sun.shadowResolution.@override;
            var states=new List<State>
            {
                new State{Name="baseline",Description="All showcase effects as shipped",Apply=()=>{},Revert=()=>{}},
                new State{Name="sun-shadows-off",Description="Directional shadow maps disabled (F4+Ctrl)",Apply=()=>rig.Debug.SetEffect(EnvironmentEffect.SunShadows,false),Revert=()=>rig.Debug.SetEffect(EnvironmentEffect.SunShadows,true)},
                new State{Name="pcss-32-32",Description="PCSS blocker/filter 32/32 instead of 64/64",Apply=()=>{sun.dirLightPCSSBlockerSampleCount=32;sun.dirLightPCSSFilterSampleCount=32;},Revert=()=>{sun.dirLightPCSSBlockerSampleCount=blocker;sun.dirLightPCSSFilterSampleCount=filter;}},
                new State{Name="pcss-16-16",Description="PCSS blocker/filter 16/16",Apply=()=>{sun.dirLightPCSSBlockerSampleCount=16;sun.dirLightPCSSFilterSampleCount=16;},Revert=()=>{sun.dirLightPCSSBlockerSampleCount=blocker;sun.dirLightPCSSFilterSampleCount=filter;}},
                new State{Name="shadow-res-2048",Description="Directional cascade resolution 2048 instead of 4096",Apply=()=>sun.SetShadowResolution(2048),Revert=()=>sun.SetShadowResolution(shadowResolution)},
                new State{Name="cascades-2",Description="Two cascades for the main camera",Apply=()=>shadows.cascadeShadowSplitCount.Override(2),Revert=()=>shadows.cascadeShadowSplitCount.Override(cascades)},
                new State{Name="foliage-no-shadow-cast",Description="Tree and planter leaves do not cast shadows",Apply=()=>{foreach(var r in foliage)r.shadowCastingMode=ShadowCastingMode.Off;},Revert=()=>{foreach(var r in foliage)r.shadowCastingMode=ShadowCastingMode.On;}},
                new State{Name="foliage-hidden",Description="Tree and planter leaves not rendered at all",Apply=()=>{foreach(var r in foliage)r.enabled=false;},Revert=()=>{foreach(var r in foliage)r.enabled=true;}},
                new State{Name="environment-hidden",Description="Whole courtyard hidden; board, pieces, sky, post only",Apply=()=>{foreach(var r in environmentRenderers)r.enabled=false;},Revert=()=>{foreach(var r in environmentRenderers)r.enabled=true;}},
                new State{Name="cube-probe-off",Description="Per-frame 128px local cubemap disabled",Apply=()=>cube.enabled=false,Revert=()=>cube.enabled=true},
                new State{Name="cube-probe-lean",Description="Cubemap keeps rendering every frame but without shadow maps, SSAO, contact shadows, volumetrics",Apply=()=>Lean(cube),Revert=()=>ShowcaseEnvironment.ConfigureReflectionRender(cube,false)},
                new State{Name="cube-probe-timesliced",Description="Cubemap time sliced across frames",Apply=()=>cube.timeSlicing=true,Revert=()=>cube.timeSlicing=false},
                new State{Name="planar-off",Description="Per-frame 512px planar reflection disabled",Apply=()=>planar.enabled=false,Revert=()=>planar.enabled=true},
                new State{Name="planar-lean",Description="Planar keeps rendering every frame but without shadow maps, SSAO, contact shadows, volumetrics",Apply=()=>Lean(planar),Revert=()=>ShowcaseEnvironment.ConfigureReflectionRender(planar,true)},
                new State{Name="planar-256",Description="Planar reflection at 256px",Apply=()=>planar.settingsRaw.resolutionScalable.@override=PlanarReflectionAtlasResolution.Resolution256,Revert=()=>planar.settingsRaw.resolutionScalable.@override=PlanarReflectionAtlasResolution.Resolution512},
                new State{Name="both-probes-lean",Description="Cubemap and planar both without shadow maps, SSAO, contact shadows, volumetrics",Apply=()=>{Lean(cube);Lean(planar);},Revert=()=>{ShowcaseEnvironment.ConfigureReflectionRender(cube,false);ShowcaseEnvironment.ConfigureReflectionRender(planar,true);}},
                new State{Name="reflections-off",Description="All reflection probes disabled (F6)",Apply=()=>rig.Debug.SetEffect(EnvironmentEffect.Reflections,false),Revert=()=>rig.Debug.SetEffect(EnvironmentEffect.Reflections,true)},
                new State{Name="ao-off",Description="SSAO intensity 0 (pass skipped)",Apply=()=>ao.intensity.Override(0),Revert=()=>ao.intensity.Override(1.2f)},
                new State{Name="ao-half-res",Description="SSAO at half resolution with bilateral upsample",Apply=()=>{ao.fullResolution=false;ao.bilateralUpsample=true;},Revert=()=>ao.fullResolution=true},
                new State{Name="ao-4-steps-2-dirs",Description="SSAO 4 steps / 2 directions, full resolution",Apply=()=>{ao.stepCount=4;ao.directionCount=2;},Revert=()=>{ao.stepCount=8;ao.directionCount=4;}},
                new State{Name="contact-off",Description="Contact shadows disabled",Apply=()=>contact.enable.Override(false),Revert=()=>contact.enable.Override(true)},
                new State{Name="contact-12-samples",Description="Contact shadows 12 samples instead of 24",Apply=()=>contact.sampleCount=12,Revert=()=>contact.sampleCount=24},
                new State{Name="dof-off",Description="Depth of field off (F8)",Apply=()=>rig.Debug.SetEffect(EnvironmentEffect.DepthOfField,false),Revert=()=>rig.Debug.SetEffect(EnvironmentEffect.DepthOfField,true)},
                new State{Name="bloom-off",Description="Bloom off (F7)",Apply=()=>rig.Debug.SetEffect(EnvironmentEffect.Bloom,false),Revert=()=>rig.Debug.SetEffect(EnvironmentEffect.Bloom,true)},
                new State{Name="cookie-off",Description="Leaf cookie off (F4)",Apply=()=>rig.Debug.SetEffect(EnvironmentEffect.DappledShadows,false),Revert=()=>rig.Debug.SetEffect(EnvironmentEffect.DappledShadows,true)},
                new State{Name="smaa-off",Description="No anti-aliasing",Apply=()=>hdCamera.antialiasing=HDAdditionalCameraData.AntialiasingMode.None,Revert=()=>hdCamera.antialiasing=HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing},
                new State{Name="transition-on",Description="Dimensional time-suspension effect at strength .82",Apply=()=>transition.Set(.82f,.5f,.5f),Revert=()=>transition.Set(0,0,0)},
                new State{Name="baseline-end",Description="All showcase effects as shipped, sampled again after every state was restored (warm steady state)",Apply=()=>{},Revert=()=>{}},
            };
            var frameMs=new List<double>(SampleFrames);var gpu=new List<double>(SampleFrames);var cpu=new List<double>(SampleFrames);var render=new List<double>(SampleFrames);
            // Shader warm-up and GPU clock ramp settle before the first sample.
            for(int i=0;i<WarmupFrames*4;i++)yield return null;
            foreach(var state in states)
            {
                log("Ablation "+state.Name);
                try{state.Apply();}catch(Exception e){Debug.LogError("Ablation apply failed: "+state.Name+" "+e);state.Revert();continue;}
                for(int i=0;i<WarmupFrames;i++)yield return null;
                frameMs.Clear();gpu.Clear();cpu.Clear();render.Clear();
                for(int i=0;i<SampleFrames;i++)
                {
                    yield return null;
                    var f=evidence.LastFrame;if(f==null)continue;
                    frameMs.Add(f.frameMs);if(f.gpuMs>0)gpu.Add(f.gpuMs);if(f.cpuMainMs>0)cpu.Add(f.cpuMainMs);if(f.cpuRenderMs>0)render.Add(f.cpuRenderMs);
                }
                state.Revert();
                samples.Add(new AblationSample{name=state.Name,description=state.Description,frames=frameMs.Count,
                    frameMedianMs=RuntimeEvidence.Percentile(frameMs,.5),frameP95Ms=RuntimeEvidence.Percentile(frameMs,.95),
                    gpuMedianMs=RuntimeEvidence.Percentile(gpu,.5),gpuP95Ms=RuntimeEvidence.Percentile(gpu,.95),
                    cpuMainMedianMs=RuntimeEvidence.Percentile(cpu,.5),cpuMainP95Ms=RuntimeEvidence.Percentile(cpu,.95),cpuRenderMedianMs=RuntimeEvidence.Percentile(render,.5),
                    uncappedFps=frameMs.Count>0?1000/frameMs.Average():0});
                // Let the restored state settle before the next experiment.
                for(int i=0;i<10;i++)yield return null;
            }
            double baseline=samples.Count>0?samples[0].gpuMedianMs:0;
            foreach(var s in samples)s.gpuDeltaFromBaselineMs=s.gpuMedianMs-baseline;
            report.samples=samples.ToArray();report.finishedUtc=DateTime.UtcNow.ToString("O");
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Application.targetFrameRate=previousTarget;
            log("Ablation complete: "+path);
        }
        static void Lean(HDProbe probe)
        {
            ref var settings=ref probe.settingsRaw.cameraSettings;
            settings.customRenderingSettings=true;
            foreach(var field in new[]{FrameSettingsField.ShadowMaps,FrameSettingsField.ContactShadows,FrameSettingsField.SSAO,FrameSettingsField.Volumetrics,FrameSettingsField.AtmosphericScattering})
            {
                settings.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field]=true;
                settings.renderingPathCustomFrameSettings.SetEnabled(field,false);
            }
        }
    }
}
