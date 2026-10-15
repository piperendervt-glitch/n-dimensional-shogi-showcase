using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NDimensionalShogi.Showcase.Environment
{
    public sealed class EnvironmentRig
    {
        public Camera Camera;
        public EnvironmentMaterials Materials;
        public Volume Volume;
        public HDAdditionalReflectionData DynamicReflection;
        public HDAdditionalReflectionData EnvironmentReflection;
        public PlanarReflectionProbe TableReflection;
        public Light Sun;
        public HDAdditionalLightData SunData;
        public EnvironmentDebug Debug;
        public GameObject Root;
        public float TableTop => .82f;
        public const int EnvironmentLayer = 8;
        public void SetGeometryReference(bool reference)
        {
            Debug.SetGeometryReference(reference);
            var hd=Camera.GetComponent<HDAdditionalCameraData>();
            hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
    }

    public static class ShowcaseEnvironment
    {
        public static EnvironmentRig Build()
        {
            var rig=new EnvironmentRig{Root=new GameObject("Sunlit stone courtyard"),Materials=new EnvironmentMaterials()};
            BuildLighting(rig);BuildCourtyard(rig);return rig;
        }
        static void BuildLighting(EnvironmentRig rig)
        {
            var cameraGo=new GameObject("Showcase camera"){tag="MainCamera"};
            rig.Camera=cameraGo.AddComponent<Camera>();rig.Camera.nearClipPlane=.015f;rig.Camera.farClipPlane=100;
            rig.Camera.fieldOfView=39;rig.Camera.allowHDR=true;
            rig.Camera.transform.position=new Vector3(.65f,1.40f,-.84f);rig.Camera.transform.LookAt(new Vector3(0,.88f,0));
            var hd=cameraGo.AddComponent<HDAdditionalCameraData>();hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
            // No TAA, motion blur or temporal effect consumes motion vectors; skip the pass.
            hd.customRenderingSettings=true;
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.MotionVectors]=true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.MotionVectors,false);
            cameraGo.AddComponent<AudioListener>();
            // Back-left sunshine produces grazing highlights on the walnut, while the canopy
            // protects the board. A physical sky supplies blue skylight rather than grey ambient.
            var sunGo=new GameObject("Warm afternoon sun / moving laurel cookie");sunGo.transform.rotation=Quaternion.Euler(43,144,0);
            var light=sunGo.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.965f,.90f);light.shadows=LightShadows.Soft;
            light.lightUnit=LightUnit.Lux;light.intensity=85000;
            var sun=sunGo.AddComponent<HDAdditionalLightData>();sun.angularDiameter=1.15f;sun.EnableShadows(true);sun.SetShadowResolution(4096);
            // Directional PCSS has its own settings in HDRP 17.3; SetPCSSParams controls
            // punctual lights only. Keep the blocker cone close to the actual sun disk
            // and use enough spatial samples for non-temporal antialiasing.
            sun.dirLightPCSSBlockerSampleCount=64;sun.dirLightPCSSFilterSampleCount=64;
            sun.dirLightPCSSBlockerSearchAngularDiameter=2;sun.dirLightPCSSMinFilterMaxAngularDiameter=2;
            sun.dirLightPCSSMinFilterSizeTexels=1.25f;sun.dirLightPCSSMaxPenumbraSize=.10f;
            sun.normalBias=.75f;sun.slopeBias=.5f;RenderSettings.sun=light;rig.Sun=light;rig.SunData=sun;
            sun.useContactShadow.useOverride=true;sun.useContactShadow.@override=true;
            var volumeGo=new GameObject("Fixed daylight and photographic response");rig.Volume=volumeGo.AddComponent<Volume>();rig.Volume.isGlobal=true;rig.Volume.priority=10;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="ShowcaseRaster / daylight";rig.Volume.sharedProfile=profile;
            var visual=profile.Add<VisualEnvironment>(true);visual.skyType.Override((int)SkyType.PhysicallyBased);visual.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky=profile.Add<PhysicallyBasedSky>(true);sky.type.Override(PhysicallyBasedSkyModel.EarthAdvanced);
            sky.groundTint.Override(new Color(.30f,.245f,.17f));sky.horizonTint.Override(new Color(1,.96f,.87f));sky.zenithTint.Override(new Color(.84f,.92f,1));
            sky.aerosolDensity.Override(.012f);sky.exposure.Override(1.25f);sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
            var exposure=profile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12.9f);
            var tonemap=profile.Add<Tonemapping>(true);tonemap.mode.Override(TonemappingMode.ACES);
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.22f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.63f);
            var dof=profile.Add<DepthOfField>(true);dof.focusMode.Override(DepthOfFieldMode.Manual);dof.nearFocusStart.Override(.05f);dof.nearFocusEnd.Override(.15f);dof.farFocusStart.Override(1.8f);dof.farFocusEnd.Override(4.8f);
            var ao=profile.Add<ScreenSpaceAmbientOcclusion>(true);ao.intensity.Override(1.2f);ao.radius.Override(.25f);ao.directLightingStrength.Override(.35f);
            ao.temporalAccumulation.Override(false);ao.quality.Override(ScalableSettingLevelParameter.LevelCount);ao.fullResolution=true;ao.stepCount=8;ao.directionCount=4;ao.maximumRadiusInPixels=48;
            // Cascades 0/1 (0–1.8 m, 1.8–5 m) keep their texel density. Everything past
            // 4.8 m is fully defocused by the DOF below, so one cascade covers 5–18 m.
            var shadows=profile.Add<HDShadowSettings>(true);shadows.maxShadowDistance.Override(18);shadows.cascadeShadowSplitCount.Override(3);shadows.cascadeShadowSplit0.Override(.10f);shadows.cascadeShadowSplit1.Override(.28f);
            shadows.cascadeShadowBorder0.Override(.12f);shadows.cascadeShadowBorder1.Override(.12f);
            // Reflection renders evaluate this extra volume as well: two cascades over 10 m
            // instead of four over 18 m. The main camera's volume mask excludes this layer.
            var probeVolumeGo=new GameObject("Reflection-render shadow budget"){layer=ProbeVolumeLayer};
            var probeVolume=probeVolumeGo.AddComponent<Volume>();probeVolume.isGlobal=true;probeVolume.priority=20;
            var probeProfile=ScriptableObject.CreateInstance<VolumeProfile>();probeProfile.name="ShowcaseRaster / reflection shadow budget";probeVolume.sharedProfile=probeProfile;
            var probeShadows=probeProfile.Add<HDShadowSettings>(true);probeShadows.maxShadowDistance.Override(10);probeShadows.cascadeShadowSplitCount.Override(2);
            probeShadows.cascadeShadowSplit0.Override(.25f);probeShadows.cascadeShadowBorder0.Override(.12f);
            var contact=profile.Add<ContactShadows>(true);contact.enable.Override(true);contact.length.Override(.055f);contact.opacity.Override(.9f);contact.maxDistance.Override(8);contact.fadeDistance.Override(2);
            contact.quality.Override(ScalableSettingLevelParameter.LevelCount);contact.sampleCount=24;contact.rayBias.Override(.08f);contact.thicknessScale.Override(.08f);
            var color=profile.Add<ColorAdjustments>(true);color.saturation.Override(-2);color.contrast.Override(12);
            var indirect=profile.Add<IndirectLightingController>(true);indirect.indirectDiffuseLightingMultiplier.Override(1.15f);
            // A diffuse-only practical fill approximates warm light returned from sunlit paving.
            // This is disclosed as an art-directed bounce, not a baked or path-traced GI result.
            var bounceGo=new GameObject("Warm paving bounce approximation");bounceGo.transform.position=new Vector3(-1.0f,.45f,1.4f);bounceGo.transform.LookAt(new Vector3(0,.92f,0));
            var bounce=bounceGo.AddComponent<Light>();bounce.type=LightType.Rectangle;bounce.areaSize=new Vector2(2.5f,1.4f);bounce.color=new Color(1,.85f,.65f);bounce.range=5;bounce.lightUnit=LightUnit.Nits;bounce.intensity=1600;
            var bounceHd=bounceGo.AddComponent<HDAdditionalLightData>();bounceHd.affectSpecular=false;bounceHd.EnableShadows(false);
            // Dynamic ambient sky and an environment-only probe avoid old chess-piece reflections.
            var probeGo=new GameObject("Environment-only reflection probe"){layer=8};probeGo.transform.position=new Vector3(0,1.2f,0);
            var probe=probeGo.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;probe.resolution=128;probe.size=new Vector3(14,8,14);probe.cullingMask=1<<8;probe.boxProjection=true;
            var hdProbe=probeGo.AddComponent<HDAdditionalReflectionData>();hdProbe.mode=ProbeSettings.Mode.Realtime;rig.EnvironmentReflection=hdProbe;
            hdProbe.realtimeMode=ProbeSettings.RealtimeMode.OnEnable;hdProbe.influenceVolume.boxSize=new Vector3(14,8,14);
            hdProbe.settingsRaw.cameraSettings.culling.cullingMask=1<<8;
            // A bounded, low-resolution local cubemap keeps off-camera dynamic sections in reflections.
            // All six faces are regenerated together each frame; no static chess-piece bake or timeslicing.
            var localProbeGo=new GameObject("Current-section tabletop reflection");localProbeGo.transform.position=new Vector3(0,1.05f,0);
            var localProbe=localProbeGo.AddComponent<ReflectionProbe>();localProbe.mode=ReflectionProbeMode.Realtime;
            localProbe.refreshMode=ReflectionProbeRefreshMode.EveryFrame;localProbe.timeSlicingMode=ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            localProbe.resolution=128;localProbe.cullingMask=(1<<0)|(1<<8);localProbe.nearClipPlane=.01f;localProbe.farClipPlane=30;
            var dynamicProbe=localProbeGo.AddComponent<HDAdditionalReflectionData>();rig.DynamicReflection=dynamicProbe;
            dynamicProbe.mode=ProbeSettings.Mode.Realtime;dynamicProbe.realtimeMode=ProbeSettings.RealtimeMode.EveryFrame;dynamicProbe.timeSlicing=false;
            dynamicProbe.settingsRaw.cubeResolution.useOverride=true;dynamicProbe.settingsRaw.cubeResolution.@override=CubeReflectionResolution.CubeReflectionResolution128;
            dynamicProbe.influenceVolume.boxSize=new Vector3(2,1.3f,2);dynamicProbe.importance=20;
            dynamicProbe.settingsRaw.cameraSettings.culling.cullingMask=(1<<0)|(1<<8);
            dynamicProbe.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw=.01f;dynamicProbe.settingsRaw.cameraSettings.frustum.farClipPlaneRaw=30;
            dynamicProbe.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume=true;
            // Six 128 px faces per frame: sun shadow maps, SSAO and contact shadows are not
            // resolvable at that size, so the faces render direct light without them.
            ConfigureReflectionRender(dynamicProbe,false);
            // A fresh mirrored camera render, with no temporal history, supplies the tabletop's
            // correctly positioned highlights and current section reflections.
            var planarGo=new GameObject("Current-frame walnut planar reflection");planarGo.transform.position=new Vector3(0,.821f,0);
            var planar=planarGo.AddComponent<PlanarReflectionProbe>();rig.TableReflection=planar;
            planar.mode=ProbeSettings.Mode.Realtime;planar.realtimeMode=ProbeSettings.RealtimeMode.EveryFrame;planar.timeSlicing=false;
            // Only the walnut tabletop receives this plane. The board top at .860 m,
            // box lids and pieces use their local cubemap instead of the wrong mirror height.
            planar.influenceVolume.boxSize=new Vector3(1.6f,.018f,1.12f);planar.importance=40;
            planar.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume=true;
            planar.settingsRaw.proxySettings.mirrorPositionProxySpace=Vector3.zero;
            planar.settingsRaw.proxySettings.mirrorRotationProxySpace=Quaternion.Euler(-90,0,0);
            planar.settingsRaw.cameraSettings.culling.cullingMask=(1<<0)|(1<<8);
            planar.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw=.01f;planar.settingsRaw.cameraSettings.frustum.farClipPlaneRaw=30;
            planar.settingsRaw.resolutionScalable.useOverride=true;planar.settingsRaw.resolutionScalable.@override=PlanarReflectionAtlasResolution.Resolution512;
            planar.settingsRaw.roughReflections=true;
            // The mirrored tabletop view keeps real sun shadows (two cascades), without screen-space passes.
            ConfigureReflectionRender(planar,true);
            rig.Debug=rig.Root.AddComponent<EnvironmentDebug>();rig.Debug.Initialize(rig,light,sun,bounce,hdProbe);
        }

        public const int ProbeVolumeLayer=10;
        public static void ConfigureReflectionRender(HDProbe probe,bool shadowMaps)
        {
            ref var camera=ref probe.settingsRaw.cameraSettings;
            camera.volumes.layerMask=(1<<0)|(1<<ProbeVolumeLayer);
            camera.customRenderingSettings=true;
            Override(ref camera,FrameSettingsField.ShadowMaps,shadowMaps);
            Override(ref camera,FrameSettingsField.ContactShadows,false);
            Override(ref camera,FrameSettingsField.SSAO,false);
            Override(ref camera,FrameSettingsField.Volumetrics,false);
        }
        static void Override(ref CameraSettings camera,FrameSettingsField field,bool enabled)
        {
            camera.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field]=true;
            camera.renderingPathCustomFrameSettings.SetEnabled(field,enabled);
        }

        static void BuildCourtyard(EnvironmentRig rig)
        {
            var m=rig.Materials;var b=new CourtyardMeshes();var rng=new System.Random(41337);
            // Individual beveled slabs, with real mortar gaps and seven mineral color variations.
            b.Box(m.Stone,new Vector3(0,-.13f,1),new Vector3(20,.2f,20),.02f);
            for(int x=-10;x<10;x++)for(int z=-9;z<11;z++)
            {
                float jitter=(float)rng.NextDouble()*.004f;
                b.Box(m.Paving[rng.Next(m.Paving.Length)],new Vector3(x+.5f+(z%2)*.5f,-.025f+jitter,z+.5f),new Vector3(.985f,.08f,.985f),.014f);
            }
            // Walnut furniture: joined planks, rolled edges, apron, feet and brass details.
            for(int i=0;i<5;i++)b.Box(m.DarkWood,new Vector3(0,.785f,-.4f+i*.2f),new Vector3(1.5f,.07f,.199f),.003f);
            b.Box(m.DarkWood,new Vector3(0,.728f,0),new Vector3(1.43f,.045f,.95f),.012f);
            foreach(float x in new[]{-.63f,.63f})foreach(float z in new[]{-.38f,.38f})
            {
                b.Box(m.DarkWood,new Vector3(x,.37f,z),new Vector3(.075f,.71f,.075f),.012f);
                b.Box(m.DarkWood,new Vector3(x,.045f,z),new Vector3(.098f,.075f,.098f),.012f);
                b.Box(m.Brass,new Vector3(x,.09f,z),new Vector3(.077f,.012f,.077f),.003f);
            }
            foreach(float z in new[]{-.41f,.41f})b.Box(m.DarkWood,new Vector3(0,.65f,z),new Vector3(1.28f,.13f,.04f),.008f);
            foreach(float x in new[]{-.65f,.65f})b.Box(m.DarkWood,new Vector3(x,.65f,0),new Vector3(.04f,.13f,.81f),.008f);
            // Two fitted wooden piece boxes and shallow trays flank the true 4D board.
            foreach(float x in new[]{-.47f,.47f})
            {
                b.Box(m.DarkWood,new Vector3(x,.848f,.07f),new Vector3(.19f,.055f,.225f),.008f);
                b.Box(m.DarkWood,new Vector3(x,.878f,.07f),new Vector3(.194f,.011f,.229f),.004f);
                b.Box(m.Brass,new Vector3(x,.854f,-.046f),new Vector3(.027f,.017f,.004f),.002f);
            }
            BuildVase(b,m,new Vector3(-.60f,.82f,.32f),rng);
            // Low parapets, pilasters and a colonnade frame the sunny garden.
            for(int x=-7;x<7;x++)
            {b.Box(m.PaleStone,new Vector3(x+.5f,.39f,7),new Vector3(.98f,.78f,.44f),.025f);b.Box(m.PaleStone,new Vector3(x+.5f,.83f,7),new Vector3(1.01f,.12f,.58f),.025f);}
            foreach(float x in new[]{-5.5f,-3.5f,3.5f,5.5f})Column(b,m,new Vector3(x,0,5.7f));
            b.Box(m.PaleStone,new Vector3(-4.5f,3.47f,5.7f),new Vector3(3.3f,.23f,.72f),.04f);
            b.Box(m.PaleStone,new Vector3(4.5f,3.47f,5.7f),new Vector3(3.3f,.23f,.72f),.04f);
            for(int i=0;i<5;i++)
            {
                float x=-6+i*3;
                Planter(b,m,new Vector3(x,0,6.6f),rng,1.35f);
                Tree(b,m,new Vector3(x-.35f,0,8.4f),rng,2.8f+(float)rng.NextDouble());
            }
            Planter(b,m,new Vector3(-3,0,2.4f),rng,1.2f);Planter(b,m,new Vector3(4,0,2.7f),rng,1.15f);
            Tree(b,m,new Vector3(-4.7f,0,2),rng,3.9f);Tree(b,m,new Vector3(5.2f,0,4),rng,4.2f);
            // The tall left canopy is set behind the sun's board sightline. Its dense real
            // leaf shadows fall on paving and the back of the table, rather than turning
            // the whole small board into a noisy PCSS penumbra. The sparse soft cookie
            // supplies a few legible patches of dappled light on the playing surface.
            Tree(b,m,new Vector3(-3.8f,0,6.1f),rng,5.0f);Tree(b,m,new Vector3(2.6f,0,5.0f),rng,4.8f);
            Fountain(b,m,new Vector3(-1.15f,0,4.8f));
            Bench(b,m,new Vector3(-1.7f,0,5.6f));Bench(b,m,new Vector3(3.7f,0,6));
            b.Finish(rig.Root.transform);
        }
        static void Column(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p)
        {
            b.Box(m.PaleStone,p+new Vector3(0,.14f,0),new Vector3(.78f,.28f,.78f),.035f);
            b.Box(m.PaleStone,p+new Vector3(0,.34f,0),new Vector3(.63f,.12f,.63f),.02f);
            var profile=new[]{new Vector2(.31f,.4f),new Vector2(.31f,.45f),new Vector2(.245f,.54f),new Vector2(.21f,2.97f),new Vector2(.26f,3.06f),new Vector2(.31f,3.11f),new Vector2(.31f,3.18f)};
            b.Add(CourtyardMeshes.Lathe(profile,40),m.PaleStone,p,Quaternion.identity,Vector3.one);
            b.Box(m.PaleStone,p+new Vector3(0,3.23f,0),new Vector3(.68f,.13f,.68f),.02f);
        }
        static void Planter(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p,System.Random rng,float width)
        {
            b.Box(m.PaleStone,p+new Vector3(0,.30f,0),new Vector3(width,.60f,.65f),.04f);
            b.Box(m.Stone,p+new Vector3(0,.54f,0),new Vector3(width-.16f,.08f,.49f),.02f);
            foreach(float z in new[]{-.32f,.32f})b.Box(m.PaleStone,p+new Vector3(0,.64f,z),new Vector3(width+.1f,.12f,.12f),.022f);
            foreach(float x in new[]{-width*.5f,width*.5f})b.Box(m.PaleStone,p+new Vector3(x,.64f,0),new Vector3(.12f,.12f,.65f),.022f);
            b.Box(m.Soil,p+new Vector3(0,.61f,0),new Vector3(width-.1f,.02f,.55f),.001f);
            var leaf=CourtyardMeshes.Leaf();
            for(int i=0;i<180;i++)
            {
                var q=p+new Vector3(((float)rng.NextDouble()-.5f)*width,.64f+(float)rng.NextDouble()*.42f,((float)rng.NextDouble()-.5f)*.55f);
                b.Add(leaf,m.Foliage[rng.Next(4)],q,Quaternion.Euler(35+(float)rng.NextDouble()*80,(float)rng.NextDouble()*360,(float)rng.NextDouble()*180),Vector3.one*(.13f+(float)rng.NextDouble()*.13f));
            }
        }
        static void Tree(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p,System.Random rng,float height)
        {
            b.Stem(m.DarkWood,p,p+Vector3.up*height,.10f);
            var leaf=CourtyardMeshes.Leaf();
            for(int branch=0;branch<11;branch++)
            {
                float a=branch*2.4f;var center=p+new Vector3(Mathf.Cos(a),height*.62f+branch*.09f,Mathf.Sin(a));
                b.Stem(m.DarkWood,p+Vector3.up*(height*.48f),center,.035f);
                for(int i=0;i<80;i++)
                {
                    var d=new Vector3((float)rng.NextDouble()*2-1,(float)rng.NextDouble()*1.3f-.4f,(float)rng.NextDouble()*2-1);
                    var q=center+d*.72f;
                    b.Add(leaf,m.Foliage[rng.Next(4)],q,Quaternion.Euler((float)rng.NextDouble()*360,(float)rng.NextDouble()*360,(float)rng.NextDouble()*360),Vector3.one*(.20f+(float)rng.NextDouble()*.2f));
                }
            }
        }
        static void Fountain(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p)
        {
            var profile=new[]{new Vector2(0,0),new Vector2(1.03f,0),new Vector2(1.08f,.09f),new Vector2(1.04f,.16f),new Vector2(1.04f,.48f),new Vector2(1.10f,.51f),new Vector2(1.10f,.59f),new Vector2(.90f,.59f),new Vector2(.87f,.49f),new Vector2(.87f,.18f),new Vector2(0,.18f)};
            b.Add(CourtyardMeshes.Lathe(profile,72),m.PaleStone,p,Quaternion.identity,Vector3.one);
            b.Add(CourtyardMeshes.Lathe(new[]{new Vector2(.88f,.46f),new Vector2(0,.46f)},72),m.Water,p,Quaternion.identity,Vector3.one);
            var pedestal=new[]{new Vector2(.30f,.18f),new Vector2(.35f,.28f),new Vector2(.22f,.39f),new Vector2(.15f,.83f),new Vector2(.22f,.98f),new Vector2(.55f,1.04f),new Vector2(.58f,1.13f),new Vector2(.52f,1.19f),new Vector2(.43f,1.16f),new Vector2(.09f,1.12f),new Vector2(.08f,1.50f),new Vector2(.15f,1.57f),new Vector2(.04f,1.66f)};
            b.Add(CourtyardMeshes.Lathe(pedestal,64),m.PaleStone,p,Quaternion.identity,Vector3.one);
            for(int i=0;i<24;i++)
            {float a=i*Mathf.PI/12;var d=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));for(int j=0;j<5;j++){float t=j/5f;b.Stem(m.Water,p+d*(.48f+t*.12f)+Vector3.up*(1.13f-t*.63f),p+d*(.48f+(t+.15f)*.12f)+Vector3.up*(1.13f-(t+.15f)*.63f),.006f);}}
        }
        static void Bench(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p)
        {
            for(int i=0;i<4;i++)b.Box(m.DarkWood,p+new Vector3(0,.47f,-.16f+i*.105f),new Vector3(1.45f,.045f,.09f),.01f);
            for(int i=0;i<3;i++)b.Box(m.DarkWood,p+new Vector3(0,.72f+i*.13f,.20f),new Vector3(1.45f,.10f,.045f),.012f);
            foreach(float x in new[]{-.60f,.60f}){b.Box(m.Brass,p+new Vector3(x,.24f,0),new Vector3(.045f,.48f,.38f),.01f);b.Box(m.Brass,p+new Vector3(x,.72f,.22f),new Vector3(.04f,.55f,.04f),.008f);}
        }
        static void BuildVase(CourtyardMeshes b,EnvironmentMaterials m,Vector3 p,System.Random rng)
        {
            var profile=new[]{new Vector2(.032f,0),new Vector2(.045f,.008f),new Vector2(.054f,.055f),new Vector2(.047f,.10f),new Vector2(.025f,.14f),new Vector2(.026f,.16f),new Vector2(.021f,.16f),new Vector2(.02f,.14f)};
            b.Add(CourtyardMeshes.Lathe(profile,48),m.Ceramic,p,Quaternion.identity,Vector3.one);
            var leaf=CourtyardMeshes.Leaf();
            for(int i=0;i<8;i++)
            {
                float a=i*2.4f;var end=p+new Vector3(Mathf.Cos(a)*.12f,.30f+(float)rng.NextDouble()*.13f,Mathf.Sin(a)*.09f);
                b.Stem(m.Foliage[0],p+Vector3.up*.13f,end,.0015f);
                for(int j=0;j<3;j++)b.Add(leaf,m.Foliage[2],Vector3.Lerp(p+Vector3.up*.13f,end,j*.2f+.3f),Quaternion.Euler(45,i*61+j*90,30),Vector3.one*.047f);
                for(int petal=0;petal<5;petal++)b.Add(leaf,m.Flower[0],end,Quaternion.Euler(70,petal*72,0),new Vector3(.026f,.022f,.026f));
            }
        }
    }
}
