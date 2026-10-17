using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Showcase.Geometry;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity
{
    // Explicit diagnostic fixture, never enabled by normal playback. It leaves
    // RuleCore and the controller's section meshes untouched. A separate real
    // SectionView exercises the same fixed-solid intersection/upload/HDRP path.
    [DefaultExecutionOrder(32000)]
    public sealed class SectionPassRegression : MonoBehaviour
    {
        const int FaultLayer=31, RoiSize=1024;
        const double TimeoutSeconds=180;
        ShowcaseController controller;
        PassEvidenceCapture originalCapture;
        Camera camera,originalCamera,cleanCamera;
        HDAdditionalReflectionData cube,originalCube,cleanCube;
        PlanarReflectionProbe planar,originalPlanar,cleanPlanar;
        EnvironmentDebug environmentDebug;
        SectionView fixture;
        Mesh historical;
        GameObject faultObject;
        MeshRenderer faultRenderer;
        CustomPassVolume volume;
        RegressionPass pass;
        readonly List<RendererState> rendererStates=new List<RendererState>();
        readonly List<Snapshot> snapshots=new List<Snapshot>();
        readonly List<RenderTexture> pendingTargets=new List<RenderTexture>();
        readonly ConcurrentQueue<Readback> readbacks=new ConcurrentQueue<Readback>();
        Schedule schedule;
        string directory,error="",stateHash;
        bool initialized,sceneStateSaved,restored,wasPaused,controllerEnabled,debugEnabled,debugFrozen;
        bool originalCameraEnabled,originalCubeEnabled,originalPlanarEnabled;
        int cameraMask,cubeMask,planarMask,cascades=3;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        float cameraFov;
        Bounds proofBounds;
        long revision;
        Report report=new Report();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if(!System.Environment.GetCommandLineArgs().Contains("--section-pass-regression"))return;
            if(FindFirstObjectByType<SectionPassRegression>()==null)new GameObject("Opt-in section pass regression").AddComponent<SectionPassRegression>();
        }
        void Start()=>StartCoroutine(Drive());

        // Flatten nested enumerators here so exceptions in every capture/wait
        // reach the same restoration path instead of escaping a Unity coroutine.
        IEnumerator Drive()
        {
            double started=Time.realtimeSinceStartupAsDouble;
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            try
            {
                while(stack.Count>0)
                {
                    object next=null;bool moved=false;
                    try
                    {
                        if(Time.realtimeSinceStartupAsDouble-started>TimeoutSeconds)throw new TimeoutException("Section pass regression exceeded its bounded diagnostic runtime.");
                        if(error.Length>0)throw new InvalidOperationException(error);
                        moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;
                    }
                    catch(Exception e){error=e.ToString();break;}
                    if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(next is IEnumerator nested){stack.Push(nested);continue;}
                    yield return next;
                }
            }
            finally
            {
                try{Restore();}catch(Exception e){error+="\nRESTORE: "+e;}
                report.error=error;report.restored=restored;
                report.finishedUtc=DateTime.UtcNow.ToString("O");
                report.status=error.Length>0?"ERROR":report.checks.Count>0&&report.checks.All(c=>c.status=="PASS")?"PASS_SCOPED_FIXTURE_ONLY":"INCONCLUSIVE_OR_FAILED_SCOPED_FIXTURE";
                if(directory!=null)File.WriteAllText(Path.Combine(directory,"section-pass-regression.json"),JsonUtility.ToJson(report,true));
                Debug.Log("Section pass regression: "+report.status+"; "+directory+"; "+error);
                if(System.Environment.GetCommandLineArgs().Contains("--exit-after-run"))Application.Quit(report.status=="PASS_SCOPED_FIXTURE_ONLY"?0:2);
            }
        }

        IEnumerator Run()
        {
            for(int i=0;i<120;i++)
            {
                controller=FindFirstObjectByType<ShowcaseController>();originalCapture=FindFirstObjectByType<PassEvidenceCapture>();
                if(controller!=null&&originalCapture!=null&&originalCapture.BoundCamera!=null)break;
                yield return null;
            }
            if(controller==null||originalCapture==null||originalCapture.BoundCamera==null)
                throw new InvalidOperationException("This fixture requires the Showcase entry point and --capture-passes. It does not run in CFG/PIVOT scenes.");
            var state=controller.InspectPlayback();
            if(state.failure.Length>0||state.ply!=0||state.reserved)throw new InvalidOperationException("Expected the untouched initial showcase before its first reservation.");
            directory=Path.Combine(state.evidenceDirectory,"section-pass-regression");
            if(Directory.Exists(directory))throw new IOException("Refusing to overwrite diagnostic evidence.");
            Directory.CreateDirectory(directory);
            report.startedUtc=DateTime.UtcNow.ToString("O");report.buildGuid=Application.buildGUID;
            var build=Resources.Load<TextAsset>("showcase-build");report.buildStamp=build==null?"MISSING":build.text;
            report.device=SystemInfo.graphicsDeviceName;report.graphicsApi=SystemInfo.graphicsDeviceType.ToString();
            report.arguments=System.Environment.GetCommandLineArgs();stateHash=state.stateHash;report.logicalStateHash=stateHash;
            wasPaused=state.paused;controllerEnabled=controller.enabled;
            if(!state.paused)controller.CommandTogglePause();controller.enabled=false;
            initialized=true;
            while(originalCapture.IsBusy)yield return null;
            originalCamera=camera=originalCapture.BoundCamera;originalCube=cube=originalCapture.BoundReflection;originalPlanar=planar=originalCapture.BoundPlanar;
            if(cube==null||planar==null)throw new InvalidOperationException("Actual dynamic cubemap and planar probes are required.");
            originalCameraEnabled=camera.enabled;originalCubeEnabled=cube.enabled;originalPlanarEnabled=planar.enabled;
            cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;cameraFov=camera.fieldOfView;
            cameraMask=camera.cullingMask;cubeMask=cube.settingsRaw.cameraSettings.culling.cullingMask;planarMask=planar.settingsRaw.cameraSettings.culling.cullingMask;
            sceneStateSaved=true;
            environmentDebug=FindFirstObjectByType<EnvironmentDebug>();
            if(environmentDebug!=null){debugEnabled=environmentDebug.enabled;debugFrozen=environmentDebug.AmbientMotionFrozen;environmentDebug.FreezeAmbientMotion(true);environmentDebug.enabled=false;}
            foreach(var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if(v.gameObject.layer==0&&v.sharedProfile!=null&&v.sharedProfile.TryGet<HDShadowSettings>(out var settings))cascades=settings.cascadeShadowSplitCount.value;
            if(cascades<1||cascades>4)throw new InvalidOperationException("Unsupported directional cascade count.");
            var sections=originalCapture.SnapshotSections();var materialSource=sections.Single(s=>s.Id=="A-Pawn-0");
            foreach(var s in sections)
            {
                rendererStates.Add(new RendererState{s=s.Renderer,enabled=s.Renderer.enabled,casting=s.Renderer.shadowCastingMode});
                if(s.Id!="Board")s.Renderer.enabled=false;
            }
            var args=System.Environment.GetCommandLineArgs();int shapeIndex=Array.IndexOf(args,"--shape");
            ShapeVariant variant=shapeIndex>=0&&shapeIndex+1<args.Length?(ShapeVariant)Enum.Parse(typeof(ShapeVariant),args[shapeIndex+1]):ShapeVariant.C;
            var materials=materialSource.Renderer.sharedMaterials;
            fixture=new SectionView("Regression fixed-4D pawn",ShapeFactory.CreatePiece(variant,1),materials[1],materials[0],false,1,variant==ShapeVariant.C);
            report.shape=variant.ToString();report.fixture=fixture.Id;
            camera.cullingMask=cameraMask&~(1<<FaultLayer);
            cube.settingsRaw.cameraSettings.culling.cullingMask=cubeMask|(1<<FaultLayer);
            planar.settingsRaw.cameraSettings.culling.cullingMask=planarMask|(1<<FaultLayer);
            camera.transform.position=new Vector3(.48f,1.30f,-.65f);camera.transform.LookAt(new Vector3(0,.98f,0));camera.fieldOfView=39;
            SetFixture(3,false);
            historical=Instantiate(fixture.Object.GetComponent<MeshFilter>().sharedMesh);historical.name="Explicit diagnostic historical pawn";
            proofBounds=fixture.Renderer.bounds;
            faultObject=new GameObject("Explicit stale shadow/reflection fault; diagnostic only");faultObject.transform.SetPositionAndRotation(fixture.Object.transform.position,fixture.Object.transform.rotation);
            faultObject.AddComponent<MeshFilter>().sharedMesh=historical;faultRenderer=faultObject.AddComponent<MeshRenderer>();faultRenderer.sharedMaterials=materials;faultRenderer.enabled=false;
            var passObject=new GameObject("Section regression GPU readbacks");volume=passObject.AddComponent<CustomPassVolume>();volume.isGlobal=true;volume.targetCamera=camera;volume.injectionPoint=CustomPassInjectionPoint.BeforePostProcess;
            pass=new RegressionPass{owner=this,name="Actual native shadow ROI and reflection regression",targetColorBuffer=CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None};volume.customPasses.Add(pass);

            yield return Frames(5);
            yield return Capture("present",0,1);
            var present=Get("present-0");var presentNoise=Get("present-1");
            SetFixture(3,true);
            report.emptyAppliedFrame=Time.frameCount;
            yield return Capture("empty",0,1,2,8);
            var empty=Get("empty-8");
            Check("real-section-empty",fixture.Last.Status==SectionStatus.Empty&&fixture.Object.GetComponent<MeshFilter>().sharedMesh.vertexCount==0,"The actual fixed-solid intersection clears the fixture Mesh; logical GameState is unchanged.");
            var depthSensitivity=Compare(present,empty,"main-depth",present,presentNoise);
            Check("main-depth-presence-sensitivity",depthSensitivity.changedPixels>0,"Actual main-camera depth must detect removal of the previously visible fixed-solid pawn.",depthSensitivity);
            foreach(int offset in new[]{0,1,2})
            {
                var diff=Compare(Get("empty-"+offset),empty,"main-depth",present,presentNoise);
                Check("empty-main-depth-frame-"+offset,diff.changedPixels==0,"First absent and subsequent recorded main depths agree with settled Empty, within measured static noise. No whole-scene/background normal claim.",diff);
            }

            CreateFreshContexts();yield return Frames(5);yield return Capture("fresh-empty",0,1);
            var clean=Get("fresh-empty-0");var cleanNoise=Get("fresh-empty-1");
            var cleanShadow=CompareShadow(empty,clean,clean,cleanNoise);
            var cleanCube=CompareReflection(empty,clean,"cube-",clean,cleanNoise);
            var cleanPlanar=Compare(empty,clean,"planar",clean,cleanNoise);
            Check("empty-clean-shadow-reference",cleanShadow.qualified&&cleanShadow.changedPixels==0,"Fresh equivalent main camera and newly rendered probes; native ROI must cover the projected historical AABB with 32 texel margin and retain the same GPU ROI transform.",cleanShadow);
            Check("empty-clean-cube-reference",cleanCube.qualified&&cleanCube.changedPixels==0,"Freshly allocated actual HDRP cubemap, all six faces; absence remains provisional until the reflection fault is detected.",cleanCube);
            Check("empty-clean-planar-reference",cleanPlanar.qualified&&cleanPlanar.changedPixels==0,"Freshly allocated actual HDRP planar render; absence remains provisional until the reflection fault is detected.",cleanPlanar);

            faultObject.layer=0;faultRenderer.shadowCastingMode=ShadowCastingMode.ShadowsOnly;faultRenderer.enabled=true;
            yield return Frames(2);yield return Capture("stale-shadow-fault",0);var shadowFault=Get("stale-shadow-fault-0");
            faultRenderer.enabled=false;yield return Frames(2);yield return Capture("shadow-restored",0);var shadowRestored=Get("shadow-restored-0");
            var shadowSensitivity=CompareShadow(clean,shadowFault,clean,cleanNoise);var shadowRestore=CompareShadow(clean,shadowRestored,clean,cleanNoise);
            Check("stale-shadow-real-hdrp-sensitivity",shadowSensitivity.qualified&&shadowSensitivity.changedPixels>0,"Historical Mesh rendered through the actual HDRP ShadowsOnly path must alter the native shadow ROI; no private substitute shader casts this fault.",shadowSensitivity);
            Check("stale-shadow-restored",shadowRestore.qualified&&shadowRestore.changedPixels==0,"Removing the deliberate stale caster must restore the clean actual shadow ROI.",shadowRestore);
            CheckInvisibleFault(clean,shadowFault,cleanNoise,"shadow");

            faultObject.layer=FaultLayer;faultRenderer.shadowCastingMode=ShadowCastingMode.Off;faultRenderer.enabled=true;
            yield return Frames(2);yield return Capture("stale-reflection-fault",0);var reflectionFault=Get("stale-reflection-fault-0");
            faultRenderer.enabled=false;yield return Frames(2);yield return Capture("reflection-restored",0);var reflectionRestored=Get("reflection-restored-0");
            var cubeSensitivity=CompareReflection(clean,reflectionFault,"cube-",clean,cleanNoise);var planarSensitivity=Compare(clean,reflectionFault,"planar",clean,cleanNoise);
            Check("stale-cube-real-hdrp-sensitivity",cubeSensitivity.qualified&&cubeSensitivity.changedPixels>0,"Historical Mesh is visible only to probe culling masks and casts no shadow; actual cubemap pixels must detect the stale reflection.",cubeSensitivity);
            Check("stale-planar-real-hdrp-sensitivity",planarSensitivity.qualified&&planarSensitivity.changedPixels>0,"Actual planar reflection pixels must detect the same probe-only stale Mesh.",planarSensitivity);
            var cubeRestore=CompareReflection(clean,reflectionRestored,"cube-",clean,cleanNoise);var planarRestore=Compare(clean,reflectionRestored,"planar",clean,cleanNoise);
            Check("cube-reflection-restored",cubeRestore.qualified&&cubeRestore.changedPixels==0,"Restored actual cubemap matches clean reference.",cubeRestore);
            Check("planar-reflection-restored",planarRestore.qualified&&planarRestore.changedPixels==0,"Restored actual planar render matches clean reference.",planarRestore);
            CheckInvisibleFault(clean,reflectionFault,cleanNoise,"reflection");

            bool constructed=ConstructOffscreenView();
            Check("offscreen-camera-construction",constructed,"The full real pawn AABB must be outside every main-camera frustum plane test while a physical mirror image or sun-shadow receiver lies within the view. Construction candidates and selected matrices are recorded.");
            if(constructed)
            {
                proofBounds=fixture.Renderer.bounds;int count=fixture.Object.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                yield return Frames(4);yield return Capture("offscreen-present",0,1);
                var outside=Get("offscreen-present-0");var outsideNoise=Get("offscreen-present-1");
                fixture.Renderer.enabled=false;yield return Frames(2);yield return Capture("offscreen-excluded-control",0);var excluded=Get("offscreen-excluded-control-0");
                fixture.Renderer.enabled=true;yield return Frames(2);yield return Capture("offscreen-restored",0);var outsideRestored=Get("offscreen-restored-0");
                bool stillOutside=!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),fixture.Renderer.bounds);
                Check("offscreen-nonempty-mesh-retained",stillOutside&&fixture.Last.Status==SectionStatus.Volume3D&&fixture.Object.GetComponent<MeshFilter>().sharedMesh.vertexCount==count,"The nonempty fixed section stays in its original Mesh while completely outside the main camera. The exclusion control toggles only Renderer.enabled and preserves vertices.");
                var os=CompareShadow(outside,excluded,outside,outsideNoise);var oc=CompareReflection(outside,excluded,"cube-",outside,outsideNoise);var op=Compare(outside,excluded,"planar",outside,outsideNoise);
                Check("offscreen-shadow-contribution",os.qualified&&os.changedPixels>0,"A named offscreen nonempty pawn must contribute to actual native shadow-map pixels; no sensitivity means INCONCLUSIVE.",os);
                Check("offscreen-cube-contribution",oc.qualified&&oc.changedPixels>0,"A named offscreen pawn must contribute to actual cubemap pixels, controlled by exclusion.",oc);
                Check("offscreen-planar-contribution",op.qualified&&op.changedPixels>0,"A named offscreen pawn must contribute to actual planar pixels, controlled by exclusion.",op);
                var od=Compare(outside,excluded,"main-depth",outside,outsideNoise);Check("offscreen-main-depth-unaffected",od.qualified&&od.changedPixels==0,"Excluding the offscreen pawn must not remove any main-camera depth-writing geometry.",od);
                var rs=CompareShadow(outside,outsideRestored,outside,outsideNoise);var rc=CompareReflection(outside,outsideRestored,"cube-",outside,outsideNoise);var rp=Compare(outside,outsideRestored,"planar",outside,outsideNoise);
                Check("offscreen-controls-restored",rs.qualified&&rc.qualified&&rp.qualified&&rs.changedPixels==0&&rc.changedPixels==0&&rp.changedPixels==0,"Shadow/cubemap/planar outputs must return to their original offscreen-present state.");
            }
            Check("logical-state-preserved",controller.InspectPlayback().stateHash==stateHash&&controller.InspectPlayback().ply==0,"No moves, rule-state mutation, scripted winner, or changes to controller section meshes are part of this render-only fixture.");
            report.snapshots=snapshots.Select(s=>s.label+".json").ToArray();
        }

        void SetFixture(double height,bool empty)
        {
            fixture.Renderer.enabled=true;fixture.UpdateSection(new VecN(0,0,height,empty?1:0),1,0,++revision);
            if(fixture.Last.Status!=(empty?SectionStatus.Empty:SectionStatus.Volume3D))throw new InvalidOperationException("Diagnostic fixed-solid configuration did not produce its required section status.");
        }
        IEnumerator Frames(int count){for(int i=0;i<count;i++){cube?.RequestRenderNextUpdate();planar?.RequestRenderNextUpdate();yield return new WaitForEndOfFrame();}}
        IEnumerator Capture(string label,params int[] offsets)
        {
            if(schedule!=null)throw new InvalidOperationException("Overlapping diagnostic schedules.");
            var list=offsets.Select(offset=>new Snapshot{label=label+"-"+offset,offset=offset,requestFrame=Time.frameCount}).ToArray();
            snapshots.AddRange(list);schedule=new Schedule{items=list};
            while(list.Any(s=>!s.complete)){if(error.Length>0)throw new InvalidOperationException(error);yield return null;}
            schedule=null;
        }
        Snapshot Get(string label)=>snapshots.Single(s=>s.label==label);
        void CreateFreshContexts()
        {
            cleanCube=Instantiate(originalCube.gameObject).GetComponent<HDAdditionalReflectionData>();cleanCube.name="Fresh clean-reference cubemap";
            cleanPlanar=Instantiate(originalPlanar.gameObject).GetComponent<PlanarReflectionProbe>();cleanPlanar.name="Fresh clean-reference planar";
            // HDProbe's realtime RTHandle fields are nonserialized. Require new
            // allocations instead of assuming an Instantiate copy is a clean GPU render.
            if(cleanCube.realtimeTexture!=null||cleanPlanar.realtimeTexture!=null)throw new InvalidOperationException("Fresh probes unexpectedly inherited allocated textures.");
            originalCube.enabled=false;originalPlanar.enabled=false;cube=cleanCube;planar=cleanPlanar;
            cube.enabled=true;planar.enabled=true;cube.RequestRenderNextUpdate();planar.RequestRenderNextUpdate();
            cleanCamera=Instantiate(originalCamera.gameObject).GetComponent<Camera>();cleanCamera.name="Fresh clean-reference main camera";
            foreach(var listener in cleanCamera.GetComponents<AudioListener>())listener.enabled=false;
            originalCamera.enabled=false;cleanCamera.enabled=true;camera=cleanCamera;volume.targetCamera=camera;
        }
        bool ConstructOffscreenView()
        {
            var light=RenderSettings.sun;if(light==null||light.transform.forward.y>=-.01f)return false;
            const float table=.821f;int candidates=0;
            foreach(double height in new[]{3d,4d,6d})
            {
                SetFixture(height,false);var bounds=fixture.Renderer.bounds;Vector3 center=bounds.center;
                var mirror=new Vector3(center.x,2*table-center.y,center.z);
                var receiver=center+light.transform.forward*((table-center.y)/light.transform.forward.y);
                foreach(var position in new[]{new Vector3(.48f,1.30f,-.65f),new Vector3(.10f,1.15f,-.9f),new Vector3(-.48f,1.25f,-.70f)})
                foreach(var look in new[]{Vector3.Lerp(mirror,receiver,.5f),mirror,receiver})
                foreach(float fov in new[]{18f,24f,30f,36f})
                {
                    candidates++;camera.transform.position=position;camera.transform.LookAt(look);camera.fieldOfView=fov;
                    bool outside=!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),bounds);
                    var m=camera.WorldToViewportPoint(mirror);var r=camera.WorldToViewportPoint(receiver);
                    bool visibleMirror=Inside(m),visibleReceiver=Inside(r);
                    if(!outside||!visibleMirror||!visibleReceiver)continue;
                    report.offscreenCandidates=candidates;report.offscreenHeight=height;report.offscreenMirror=mirror;report.offscreenReceiver=receiver;
                    report.offscreenMirrorViewport=m;report.offscreenReceiverViewport=r;return true;
                }
            }
            report.offscreenCandidates=candidates;return false;
        }
        static bool Inside(Vector3 p)=>p.z>0&&p.x>.08f&&p.x<.92f&&p.y>.08f&&p.y<.92f;

        void Record(CustomPassContext ctx,Material standard,Material native)
        {
            if(restored||schedule==null||ctx.hdCamera.camera!=camera)return;
            if(schedule.firstFrame<0)schedule.firstFrame=Time.frameCount;
            var snapshot=schedule.items.FirstOrDefault(s=>s.frame==0&&Time.frameCount-schedule.firstFrame>=s.offset);
            if(snapshot==null)return;
            if(cube.realtimeTexture==null||planar.realtimeTexture==null)throw new InvalidOperationException("Actual reflection textures are unallocated.");
            snapshot.frame=Time.frameCount;snapshot.width=ctx.hdCamera.actualWidth;snapshot.height=ctx.hdCamera.actualHeight;snapshot.revision=revision;
            snapshot.sectionStatus=fixture.Last.Status.ToString();snapshot.meshVertices=fixture.Object.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            snapshot.fixtureEnabled=fixture.Renderer.enabled;snapshot.faultEnabled=faultRenderer.enabled;snapshot.faultMode=faultRenderer.shadowCastingMode.ToString();snapshot.faultLayer=faultObject.layer;
            snapshot.worldToCamera=camera.worldToCameraMatrix;snapshot.projection=camera.projectionMatrix;snapshot.cameraPosition=camera.transform.position;snapshot.cameraRotation=camera.transform.rotation;
            snapshot.boundsMin=proofBounds.min;snapshot.boundsMax=proofBounds.max;snapshot.outsideFrustum=!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),fixture.Renderer.bounds);
            snapshot.cubeTextureId=cube.realtimeTexture.GetInstanceID();snapshot.planarTextureId=planar.realtimeTexture.GetInstanceID();snapshot.cubeUpdateCount=cube.realtimeTexture.updateCount;snapshot.planarUpdateCount=planar.realtimeTexture.updateCount;
            snapshot.cubeRenderPosition=cube.renderData.capturePosition;snapshot.planarView=planar.renderData.worldToCameraRHS;snapshot.planarProjection=planar.renderData.projectionMatrix;
            ctx.cmd.SetGlobalTexture("_ShowcaseCaptureColor",ctx.cameraColorBuffer.nameID);ctx.cmd.SetGlobalTexture("_ShowcaseCaptureDepth",ctx.cameraDepthBuffer.nameID);ctx.cmd.SetGlobalTexture("_ShowcaseCaptureNormal",ctx.cameraNormalBuffer.nameID);
            ctx.cmd.SetGlobalTexture("_ShowcaseCaptureCube",cube.realtimeTexture);ctx.cmd.SetGlobalTexture("_ShowcaseCapturePlanar",planar.realtimeTexture);
            for(int mode=0;mode<=10;mode++)
            {
                if(mode==3)continue;
                int width=mode<3?snapshot.width:mode==10?planar.realtimeTexture.width:cube.realtimeTexture.width;
                int height=mode<3?snapshot.height:mode==10?planar.realtimeTexture.height:cube.realtimeTexture.height;
                string name=mode==0?"main-hdr":mode==1?"main-depth":mode==2?"main-normal":mode==10?"planar":"cube-"+(mode-4);
                var properties=new MaterialPropertyBlock();properties.SetInt("_ShowcaseCaptureMode",mode);properties.SetVector("_ShowcaseCaptureSize",new Vector4(width,height,1f/width,1f/height));
                QueueBuffer(ctx,snapshot,name,width,height,mode==1?1:4,standard,0,properties);
            }
            for(int i=0;i<cascades;i++)
            {
                var properties=NativeProperties(i);QueueBuffer(ctx,snapshot,"shadow-"+i,RoiSize,RoiSize,1,native,0,properties);
            }
            QueueBuffer(ctx,snapshot,"shadow-proof",12,cascades,4,native,1,NativeProperties(0));
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,snapshot.label+"-final-color.png"));
        }
        MaterialPropertyBlock NativeProperties(int cascade)
        {
            var p=new MaterialPropertyBlock();p.SetVector("_RegressionBoundsMin",proofBounds.min);p.SetVector("_RegressionBoundsMax",proofBounds.max);p.SetInt("_RegressionCascade",cascade);p.SetInt("_RegressionRoiSize",RoiSize);return p;
        }
        void QueueBuffer(CustomPassContext ctx,Snapshot snapshot,string name,int width,int height,int channels,Material material,int passIndex,MaterialPropertyBlock properties)
        {
            var rt=new RenderTexture(width,height,0,channels==1?RenderTextureFormat.RFloat:RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){name="Section regression "+name,filterMode=FilterMode.Point,useMipMap=false,autoGenerateMips=false,antiAliasing=1};
            rt.Create();pendingTargets.Add(rt);snapshot.expected++;
            HDUtils.DrawFullScreen(ctx.cmd,new Rect(0,0,width,height),material,new RenderTargetIdentifier(rt),properties,passIndex);
            ctx.cmd.RequestAsyncReadback(rt,0,channels==1?TextureFormat.RFloat:TextureFormat.RGBAFloat,result=>
            {
                var item=new Readback{snapshot=snapshot,name=name,width=width,height=height,channels=channels,target=rt,error=result.hasError};
                if(!result.hasError)item.bytes=result.GetData<byte>().ToArray();readbacks.Enqueue(item);
            });
        }
        void Update()=>Drain();
        void Drain()
        {
            while(readbacks.TryDequeue(out var item))
            {
                try
                {
                    if(item.error)throw new IOException("Actual GPU readback failed: "+item.name);
                    if(restored)continue;
                    var values=new float[item.bytes.Length/4];Buffer.BlockCopy(item.bytes,0,values,0,item.bytes.Length);
                    if(values.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))throw new InvalidOperationException("Nonfinite GPU buffer: "+item.name);
                    string file=item.snapshot.label+"-"+item.name+(item.channels==1?".r32":".rgba32f");
                    File.WriteAllBytes(Path.Combine(directory,file),item.bytes);
                    string hash;using(var algorithm=SHA256.Create())hash=BitConverter.ToString(algorithm.ComputeHash(item.bytes)).Replace("-","").ToLowerInvariant();
                    item.snapshot.buffers.Add(new BufferData{name=item.name,file=file,width=item.width,height=item.height,channels=item.channels,sha256=hash,values=values});
                    item.snapshot.completed++;
                    if(item.snapshot.completed==item.snapshot.expected)
                    {
                        item.snapshot.complete=true;
                        var proof=item.snapshot.buffers.Single(b=>b.name=="shadow-proof");item.snapshot.shadowProof=(float[])proof.values.Clone();
                        File.WriteAllText(Path.Combine(directory,item.snapshot.label+".json"),JsonUtility.ToJson(item.snapshot,true));
                    }
                }
                catch(Exception e){error=e.ToString();}
                finally{pendingTargets.Remove(item.target);item.target.Release();Destroy(item.target);}
            }
        }

        Difference Compare(Snapshot a,Snapshot b,string name,Snapshot noiseA,Snapshot noiseB)
        {
            var x=a.buffers.SingleOrDefault(v=>v.name==name);var y=b.buffers.SingleOrDefault(v=>v.name==name);
            var nx=noiseA.buffers.SingleOrDefault(v=>v.name==name);var ny=noiseB.buffers.SingleOrDefault(v=>v.name==name);
            var d=new Difference{pass=name,baseline=a.label,comparison=b.label,qualified=x!=null&&y!=null&&nx!=null&&ny!=null};
            if(!d.qualified)return d;
            if(x.width!=y.width||x.height!=y.height||x.channels!=y.channels||x.values.Length!=nx.values.Length||nx.values.Length!=ny.values.Length){d.qualified=false;return d;}
            bool depth=name=="main-depth"||name.StartsWith("shadow-",StringComparison.Ordinal);double floor=depth?1e-7:1e-4,relative=depth?0:1e-6;
            double noise=0;for(int i=0;i<nx.values.Length;i++)noise=Math.Max(noise,Math.Abs((double)nx.values[i]-ny.values[i]));
            d.measuredNoiseMaximum=noise;d.absoluteThreshold=Math.Max(floor,noise*8);d.relativeThreshold=relative;d.pixels=x.width*x.height;
            for(int p=0;p<d.pixels;p++)
            {
                bool changed=false;
                for(int channel=0;channel<x.channels;channel++)
                {
                    if(x.channels==4&&channel==3)continue;
                    int i=p*x.channels+channel;double delta=Math.Abs((double)x.values[i]-y.values[i]);d.maximumDifference=Math.Max(d.maximumDifference,delta);
                    if(delta>d.absoluteThreshold+relative*Math.Max(Math.Abs(x.values[i]),Math.Abs(y.values[i])))changed=true;
                }
                if(changed)d.changedPixels++;
            }
            return d;
        }
        Difference CompareReflection(Snapshot a,Snapshot b,string prefix,Snapshot n1,Snapshot n2)
        {
            var result=new Difference{pass=prefix+"all-six-faces",baseline=a.label,comparison=b.label,qualified=true};
            for(int i=0;i<6;i++)Accumulate(result,Compare(a,b,prefix+i,n1,n2));return result;
        }
        Difference CompareShadow(Snapshot a,Snapshot b,Snapshot n1,Snapshot n2)
        {
            var d=new Difference{pass="native-shadow-ROIs",baseline=a.label,comparison=b.label,qualified=true};int qualifiedCascades=0;
            for(int i=0;i<cascades;i++)
            {
                if(!RoiValid(a,i)||!RoiValid(b,i)||!RoiValid(n1,i)||!RoiValid(n2,i))continue;
                if(!SameRoi(a,b,i)||!SameRoi(a,n1,i)||!SameRoi(a,n2,i))continue;
                qualifiedCascades++;Accumulate(d,Compare(a,b,"shadow-"+i,n1,n2));
            }
            d.qualified&=qualifiedCascades>0;d.qualifiedCascades=qualifiedCascades;return d;
        }
        bool RoiValid(Snapshot s,int cascade)=>s.shadowProof!=null&&s.shadowProof.Length==12*cascades*4&&s.shadowProof[(cascade*12+1)*4+3]>.5f;
        static bool SameRoi(Snapshot a,Snapshot b,int cascade)
        {
            // Include atlas dimensions, exact ROI origin and GPU light transform;
            // exclude camera-position diagnostics, which do not identify a texel.
            for(int i=cascade*48;i<cascade*48+36;i++)if(Math.Abs(a.shadowProof[i]-b.shadowProof[i])>1e-5)return false;return true;
        }
        static void Accumulate(Difference target,Difference source)
        {
            target.qualified&=source.qualified;target.pixels+=source.pixels;target.changedPixels+=source.changedPixels;
            target.maximumDifference=Math.Max(target.maximumDifference,source.maximumDifference);target.measuredNoiseMaximum=Math.Max(target.measuredNoiseMaximum,source.measuredNoiseMaximum);
            target.absoluteThreshold=Math.Max(target.absoluteThreshold,source.absoluteThreshold);target.relativeThreshold=Math.Max(target.relativeThreshold,source.relativeThreshold);
        }
        void CheckInvisibleFault(Snapshot clean,Snapshot fault,Snapshot noise,string label)
        {
            var depth=Compare(clean,fault,"main-depth",clean,noise);var normal=Compare(clean,fault,"main-normal",clean,noise);
            Check(label+"-fault-main-geometry-unmodified",depth.qualified&&normal.qualified&&depth.changedPixels==0&&normal.changedPixels==0,"Diagnostic stale Mesh does not write main-camera depth/normal; shading may change through the pass being tested.");
        }
        void Check(string name,bool success,string scope,Difference difference=null)
        {report.checks.Add(new CheckResult{name=name,status=success?"PASS":"INCONCLUSIVE_OR_FAIL",scope=scope,difference=difference});}

        void Restore()
        {
            if(restored)return;
            // Stop rendering diagnostic faults before releasing any pass/resources.
            if(faultRenderer!=null)faultRenderer.enabled=false;
            if(fixture!=null)fixture.Renderer.enabled=false;
            if(volume!=null)volume.enabled=false;schedule=null;
            if(pendingTargets.Count>0){AsyncGPUReadback.WaitAllRequests();Drain();}
            foreach(var rt in pendingTargets){if(rt!=null){rt.Release();Destroy(rt);}}pendingTargets.Clear();
            foreach(var r in rendererStates)if(r.s!=null){r.s.enabled=r.enabled;r.s.shadowCastingMode=r.casting;}
            if(sceneStateSaved&&originalCamera!=null){originalCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);originalCamera.fieldOfView=cameraFov;originalCamera.cullingMask=cameraMask;originalCamera.enabled=originalCameraEnabled;}
            if(sceneStateSaved&&originalCube!=null){originalCube.settingsRaw.cameraSettings.culling.cullingMask=cubeMask;originalCube.enabled=originalCubeEnabled;originalCube.RequestRenderNextUpdate();}
            if(sceneStateSaved&&originalPlanar!=null){originalPlanar.settingsRaw.cameraSettings.culling.cullingMask=planarMask;originalPlanar.enabled=originalPlanarEnabled;originalPlanar.RequestRenderNextUpdate();}
            if(cleanCamera!=null){cleanCamera.enabled=false;Destroy(cleanCamera.gameObject);}
            if(cleanCube!=null){cleanCube.enabled=false;Destroy(cleanCube.gameObject);}
            if(cleanPlanar!=null){cleanPlanar.enabled=false;Destroy(cleanPlanar.gameObject);}
            if(environmentDebug!=null){environmentDebug.FreezeAmbientMotion(debugFrozen);environmentDebug.enabled=debugEnabled;}
            if(volume!=null)Destroy(volume.gameObject);
            if(faultObject!=null)Destroy(faultObject);if(historical!=null)Destroy(historical);fixture?.Dispose();fixture=null;
            if(initialized&&controller!=null)
            {
                var now=controller.InspectPlayback();
                if(now.paused!=wasPaused)controller.CommandTogglePause();controller.enabled=controllerEnabled;
                if(now.stateHash!=stateHash)throw new InvalidOperationException("Rule state changed during the diagnostic fixture.");
            }
            restored=true;
        }
        void OnDestroy(){if(!restored)Restore();}
        [Serializable] sealed class RegressionPass:CustomPass
        {
            [NonSerialized] public SectionPassRegression owner;Material standard,native;
            protected override void Setup(ScriptableRenderContext context,CommandBuffer cmd)
            {
                var a=Resources.Load<Shader>("ShowcasePassEvidence");var b=Resources.Load<Shader>("SectionPassRegression");
                if(a==null||b==null)throw new InvalidOperationException("Diagnostic resource shader missing.");
                standard=CoreUtils.CreateEngineMaterial(a);native=CoreUtils.CreateEngineMaterial(b);
            }
            protected override void Execute(CustomPassContext ctx){if(owner==null)return;try{owner.Record(ctx,standard,native);}catch(Exception e){owner.error=e.ToString();}}
            protected override void Cleanup(){CoreUtils.Destroy(standard);CoreUtils.Destroy(native);}
        }
        sealed class Schedule{public int firstFrame=-1;public Snapshot[] items;}
        sealed class Readback{public Snapshot snapshot;public string name;public int width,height,channels;public bool error;public byte[] bytes;public RenderTexture target;}
        sealed class RendererState{public MeshRenderer s;public bool enabled;public ShadowCastingMode casting;}
        [Serializable] sealed class BufferData
        {public string name,file,sha256;public int width,height,channels;public string encoding="IEEE754 float32 little-endian, interleaved GPU readback row order; R or RGBA; no resampling";[NonSerialized]public float[] values;}
        [Serializable] sealed class Snapshot
        {
            public string label,sectionStatus,faultMode;public int offset,requestFrame,frame,width,height,meshVertices,faultLayer,cubeTextureId,planarTextureId;
            public long revision;public uint cubeUpdateCount,planarUpdateCount;public bool fixtureEnabled,faultEnabled,outsideFrustum;
            public Vector3 cameraPosition,boundsMin,boundsMax,cubeRenderPosition;public Quaternion cameraRotation;public Matrix4x4 worldToCamera,projection,planarView,planarProjection;
            public float[] shadowProof;public string shadowProofLayout="12 RGBA texels/cascade, raw GPU values: 0 atlas width/height,cascade,shadowIndex; 1 native ROI origin x/y,size,coverage valid; 2 projected historical AABB min/max; 3 cascade atlas offset pixels and size; 4 worldTexelSize,normalBias,32-texel margin,valid; 5 projection; 6/7/8 rotation row and position component; 9 camera world position; 10/11 original world AABB min/max.";
            public List<BufferData> buffers=new List<BufferData>();[NonSerialized]public int expected,completed;[NonSerialized]public bool complete;
        }
        [Serializable] sealed class Difference
        {public string pass,baseline,comparison;public bool qualified;public int pixels,changedPixels,qualifiedCascades;public double maximumDifference,measuredNoiseMaximum,absoluteThreshold,relativeThreshold;}
        [Serializable] sealed class CheckResult{public string name,status,scope;public Difference difference;}
        [Serializable] sealed class Report
        {
            public string schema="section-pass-regression/1",status,startedUtc,finishedUtc,error,buildGuid,buildStamp,device,graphicsApi,fixture,shape,logicalStateHash;
            public string scope="Opt-in representative fixed-solid pawn fixture on actual HDRP paths. Other real pieces are disabled only for attribution and restored; the real board/environment remain. Core logical state and original section meshes are unchanged. This does not prove all pieces, all angles, all receiver pixels, contact shadows, all final-color temporal artifacts, performance, or human visual acceptance. Any insensitive/incomparable pass is INCONCLUSIVE_OR_FAIL, never PASS. Raw shadow evidence is an unresampled native ROI whose GPU projection proof must enclose the historical AABB; out-of-ROI/cascade footprints are excluded explicitly.";
            public bool restored;public string[] arguments,snapshots;public int emptyAppliedFrame,offscreenCandidates;public double offscreenHeight;
            public Vector3 offscreenMirror,offscreenReceiver,offscreenMirrorViewport,offscreenReceiverViewport;public List<CheckResult> checks=new List<CheckResult>();
        }
    }
}
