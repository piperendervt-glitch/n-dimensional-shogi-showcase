using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Showcase.Geometry;

namespace Showcase.Unity
{
    // Diagnostic readback only: it never renders into the scene camera targets.
    // Capture costs are deliberately separate from normal playback performance.
    public sealed class PassEvidenceCapture : MonoBehaviour
    {
        Camera targetCamera;
        Func<IEnumerable<SectionView>> sectionProvider;
        Func<long> versionProvider;
        Func<HDAdditionalReflectionData> reflectionProvider;
        Func<PlanarReflectionProbe> planarProvider;
        readonly Queue<CaptureRequest> requests=new Queue<CaptureRequest>();
        readonly ConcurrentQueue<CompletedBuffer> completed=new ConcurrentQueue<CompletedBuffer>();
        readonly List<CaptureRequest> active=new List<CaptureRequest>();
        readonly Dictionary<string,SectionPixelAuditor.MeshSnapshot> history=new Dictionary<string,SectionPixelAuditor.MeshSnapshot>();
        CustomPassVolume volume;
        EvidencePass pass;
        GpuStaleDepthFault gpuFault;
        string captureError="";
        bool auditFailures;
        public float DepthDisplayRangeMetres=5;
        public bool CaptureDirectionalShadowAtlas=true;
        public bool IsBusy => requests.Count>0||active.Count>0||(gpuFault!=null&&gpuFault.IsBusy);
        public string LastError { get=>captureError.Length>0?captureError:gpuFault==null?"":gpuFault.LastError;private set=>captureError=value; }
        public bool HasAuditFailures { get=>auditFailures||(gpuFault!=null&&gpuFault.HasFailures);private set=>auditFailures=value; }
        public void BindReflection(Func<HDAdditionalReflectionData> probe)=>reflectionProvider=probe;
        public void BindPlanar(Func<PlanarReflectionProbe> probe)=>planarProvider=probe;
        // Read-only diagnostic access. The opt-in section regression owns its own
        // captures and restores every scene change; normal playback never calls it.
        public Camera BoundCamera=>targetCamera;
        public SectionView[] SnapshotSections()=>sectionProvider==null?Array.Empty<SectionView>():sectionProvider().Where(v=>v!=null).ToArray();
        public HDAdditionalReflectionData BoundReflection=>reflectionProvider==null?null:reflectionProvider();
        public PlanarReflectionProbe BoundPlanar=>planarProvider==null?null:planarProvider();

        public void Bind(Camera camera,Func<IEnumerable<SectionView>> sections,Func<long> sectionVersion)
        {
            targetCamera=camera;sectionProvider=sections;versionProvider=sectionVersion;
            if(volume!=null)return;
            gpuFault=gameObject.AddComponent<GpuStaleDepthFault>();
            var child=new GameObject("Read-only HDRP pass evidence");child.transform.SetParent(transform,false);
            volume=child.AddComponent<CustomPassVolume>();volume.isGlobal=true;volume.targetCamera=camera;volume.injectionPoint=CustomPassInjectionPoint.BeforePostProcess;
            pass=new EvidencePass { owner=this,name="Read actual color depth normals",targetColorBuffer=CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None };
            volume.customPasses.Add(pass);
        }
        public void Capture(string absolutePrefix)
        {
            if(targetCamera==null||volume==null)throw new InvalidOperationException("Pass evidence must be bound to the showcase camera.");
            if(requests.Count+active.Count>=3)throw new InvalidOperationException("Pass evidence capture capacity reached.");
            string prefix=Path.GetFullPath(absolutePrefix);Directory.CreateDirectory(Path.GetDirectoryName(prefix));
            var request=new CaptureRequest { prefix=prefix,requestFrame=Time.frameCount,queuedAt=Time.realtimeSinceStartup };
            requests.Enqueue(request);
            File.WriteAllText(prefix+"-passes-status.txt","QUEUED: waiting for the bound camera's HDRP BeforePostProcess pass.\n");
        }
        public void CancelPending(string reason="Run restarted")
        {
            LastError="";
            HasAuditFailures=false;
            if(gpuFault!=null)gpuFault.CancelPending(reason);
            while(requests.Count>0)
            {
                var r=requests.Dequeue();r.cancelled=true;
                File.WriteAllText(r.prefix+"-passes-status.txt","CANCELLED before GPU capture: "+reason+"\n");
            }
            foreach(var r in active){r.cancelled=true;r.errors.Add("Capture cancelled: "+reason);WriteManifest(r,"CANCELLED");}
            history.Clear();
        }
        void Record(CustomPassContext ctx,Material material)
        {
            if(targetCamera==null||ctx.hdCamera.camera!=targetCamera||requests.Count==0)return;
            var request=requests.Dequeue();active.Add(request);
            // A failure while setting up must not leave the request active forever (IsBusy would never clear).
            var readbacks=new bool[request.targets.Length];
            try{RecordRequest(ctx,material,request,readbacks);}
            catch(Exception e)
            {
                request.errors.Add("Capture setup failed: "+e.Message);LastError=e.ToString();HasAuditFailures=true;Debug.LogError("Pass evidence: "+e);
                for(int mode=0;mode<readbacks.Length;mode++)
                    if(!readbacks[mode]&&request.targets[mode]!=null){request.targets[mode].Release();Destroy(request.targets[mode]);request.targets[mode]=null;}
                request.expectedBuffers=readbacks.Count(x=>x);
                if(request.expectedBuffers==0)
                {
                    active.Remove(request);
                    try{WriteManifest(request,"READBACK_FAILED");File.WriteAllText(request.prefix+"-passes-status.txt","READBACK_FAILED\n");}
                    catch(Exception write){Debug.LogError("Pass evidence: "+write);}
                }
            }
        }
        void RecordRequest(CustomPassContext ctx,Material material,CaptureRequest request,bool[] readbacks)
        {
            request.width=ctx.hdCamera.actualWidth;request.height=ctx.hdCamera.actualHeight;request.captureFrame=Time.frameCount;
            request.expectedVersion=versionProvider==null?-1:versionProvider();request.depthDisplayRange=DepthDisplayRangeMetres;
            var views=sectionProvider==null?Array.Empty<SectionView>():sectionProvider().Where(v=>v!=null).ToArray();
            request.stamps=views.Select(RendererContractAuditor.DeclaredStamp).ToArray();
            request.probes=SectionPixelAuditor.BuildProbes(views,targetCamera,request.width,request.height,history,out var oracleMeshes);
            File.WriteAllText(request.prefix+"-mesh-oracle.json",JsonUtility.ToJson(new SectionPixelAuditor.GeometrySnapshot { width=request.width,height=request.height,cameraPosition=targetCamera.transform.position,cameraRotation=targetCamera.transform.rotation,worldToCamera=targetCamera.worldToCameraMatrix,projection=targetCamera.projectionMatrix,meshes=oracleMeshes }));
            request.audit=RendererContractAuditor.Audit(request.stamps,request.expectedVersion);
            request.faultInjectionDetected=RendererContractAuditor.IsolatedStaleDepthIsDetected();
            var reflection=reflectionProvider==null?null:reflectionProvider();
            var cube=reflection==null?null:reflection.realtimeTexture;
            var planar=planarProvider==null?null:planarProvider();var planarTexture=planar==null?null:planar.realtimeTexture;
            request.expectedBuffers=CaptureDirectionalShadowAtlas?4:3;
            request.reflectionDescription=cube==null?"NOT_CAPTURED: no allocated realtime cubemap":("Actual HDProbe.realtimeTexture; "+cube.width+" px; mode="+reflection.realtimeMode+"; timeSlicing="+reflection.timeSlicing+"; texture updateCount="+cube.updateCount+"; position="+reflection.transform.position);
            if(cube!=null){request.expectedBuffers+=6;ctx.cmd.SetGlobalTexture("_ShowcaseCaptureCube",cube);}
            request.planarDescription=planarTexture==null?"NOT_CAPTURED: no allocated planar texture":("Actual PlanarReflectionProbe.realtimeTexture; "+planarTexture.width+"x"+planarTexture.height+"; mode="+planar.realtimeMode+"; updateCount="+planarTexture.updateCount+"; mirrorPlane="+planar.transform.position);
            if(planarTexture!=null){request.expectedBuffers++;ctx.cmd.SetGlobalTexture("_ShowcaseCapturePlanar",planarTexture);}
            request.manifestPath=request.prefix+"-passes.json";
            WriteManifest(request,"READBACK_PENDING");
            ctx.cmd.SetGlobalTexture("_ShowcaseCaptureColor",ctx.cameraColorBuffer.nameID);
            ctx.cmd.SetGlobalTexture("_ShowcaseCaptureDepth",ctx.cameraDepthBuffer.nameID);
            ctx.cmd.SetGlobalTexture("_ShowcaseCaptureNormal",ctx.cameraNormalBuffer.nameID);
            for(int mode=0;mode<11;mode++)
            {
                if(mode==3&&!CaptureDirectionalShadowAtlas||mode>=4&&mode<10&&cube==null||mode==10&&planarTexture==null)continue;
                int capturedMode=mode;
                int width=mode==10?planarTexture.width:mode>=4?cube.width:mode==3?1024:request.width,height=mode==10?planarTexture.height:mode>=4?cube.height:mode==3?1024:request.height;
                request.bufferWidths[mode]=width;request.bufferHeights[mode]=height;
                var rt=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) { name="Evidence "+mode,filterMode=FilterMode.Point,useMipMap=false,autoGenerateMips=false,antiAliasing=1 };
                rt.Create();request.targets[mode]=rt;
                var props=new MaterialPropertyBlock();props.SetInt("_ShowcaseCaptureMode",mode);props.SetVector("_ShowcaseCaptureSize",new Vector4(width,height,1f/width,1f/height));
                HDUtils.DrawFullScreen(ctx.cmd,new Rect(0,0,width,height),material,new RenderTargetIdentifier(rt),props,0);
                ctx.cmd.RequestAsyncReadback(rt,0,TextureFormat.RGBAFloat,r=>
                {
                    var data=new CompletedBuffer { request=request,mode=capturedMode,error=r.hasError };
                    if(!r.hasError)data.bytes=r.GetData<byte>().ToArray();
                    completed.Enqueue(data);
                });
                readbacks[mode]=true;
            }
            if(gpuFault!=null&&request.prefix.EndsWith("scene04-absence",StringComparison.Ordinal))
            {
                var formerIds=new HashSet<string>(request.probes.Where(p=>p.kind=="former-empty-silhouette").Select(p=>p.id));
                var historical=views.Where(v=>v.Last!=null&&v.Last.Status==SectionStatus.Empty&&formerIds.Contains(v.Id)&&history.ContainsKey(v.Id)).Select(v=>history[v.Id]).ToArray();
                gpuFault.Capture(ctx,targetCamera,request.prefix,historical,request.probes,request.expectedVersion);
            }
        }
        void Update()
        {
            while(completed.TryDequeue(out var item))
            {
                var r=item.request;
                try
                {
                    if(r.cancelled)continue;
                    if(item.error)throw new InvalidOperationException("GPU readback failed for "+BufferName(item.mode));
                    SaveBuffer(r,item.mode,item.bytes);
                    r.saved[item.mode]=true;
                }
                catch(Exception e) { r.errors.Add(e.Message);LastError=e.ToString();Debug.LogError("Pass evidence: "+e); }
                finally
                {
                    if(r.targets[item.mode]!=null){r.targets[item.mode].Release();Destroy(r.targets[item.mode]);r.targets[item.mode]=null;}
                    r.finished++;
                    if(r.finished==r.expectedBuffers)
                    {
                        if(!r.cancelled&&r.errors.Count==0)
                        {
                            r.pixelAudit=SectionPixelAuditor.Validate(r.probes,r.depthPixels,r.normalPixels,r.width,r.height);
                            r.pixelAudit.isolatedReadbackDepthMutation=SectionPixelAuditor.VerifyDepthMutation(r.pixelAudit,r.depthPixels,r.normalPixels,r.width,r.height);
                            if(r.pixelAudit.failures>0||r.pixelAudit.isolatedReadbackDepthMutation.StartsWith("FAIL",StringComparison.Ordinal))
                            {HasAuditFailures=true;Debug.LogError("Pass pixel audit failed: "+r.prefix+" ("+r.pixelAudit.status+")");}
                            File.WriteAllText(r.prefix+"-pixel-audit.json",JsonUtility.ToJson(r.pixelAudit,true));
                        }
                        string status=r.cancelled?"CANCELLED":r.errors.Count==0?"GPU_READBACK_COMPLETE":"READBACK_FAILED";
                        WriteManifest(r,status);File.WriteAllText(r.prefix+"-passes-status.txt",status+"\n");active.Remove(r);
                    }
                }
            }
            if(requests.Count>0&&Time.realtimeSinceStartup-requests.Peek().queuedAt>15)
            {
                var stalled=requests.Dequeue();LastError="HDRP custom pass did not run within 15 seconds. Check the camera/pipeline CustomPass frame setting.";
                File.WriteAllText(stalled.prefix+"-passes-status.txt","NOT_CAPTURED: "+LastError+"\n");Debug.LogError(LastError);
            }
        }
        void SaveBuffer(CaptureRequest r,int mode,byte[] raw)
        {
            string name=BufferName(mode);
            int width=r.bufferWidths[mode],height=r.bufferHeights[mode];
            var texture=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try
            {
                texture.LoadRawTextureData(raw);texture.Apply(false,false);
                File.WriteAllBytes(r.prefix+"-"+name+".exr",texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat|Texture2D.EXRFlags.CompressZIP));
                Color[] pixels=texture.GetPixels();var display=new Color32[pixels.Length];
                if(mode==1){r.depthPixels=new float[pixels.Length];for(int i=0;i<pixels.Length;i++)r.depthPixels[i]=pixels[i].r;}
                if(mode==2)r.normalPixels=pixels;
                float shadowMin=1,shadowMax=0;
                if(mode==3)
                {
                    r.shadowSourceWidth=(int)pixels[0].g;r.shadowSourceHeight=(int)pixels[0].b;
                    foreach(var p in pixels){shadowMin=Mathf.Min(shadowMin,p.r);shadowMax=Mathf.Max(shadowMax,p.r);}
                    r.shadowDepthRange=new Vector2(shadowMin,shadowMax);
                }
                for(int i=0;i<pixels.Length;i++)
                {
                    Color p=pixels[i];Color c;
                    if(mode==0||mode>=4)c=new Color(Mathf.LinearToGammaSpace(Mathf.Max(0,p.r)/(1+Mathf.Max(0,p.r))),Mathf.LinearToGammaSpace(Mathf.Max(0,p.g)/(1+Mathf.Max(0,p.g))),Mathf.LinearToGammaSpace(Mathf.Max(0,p.b)/(1+Mathf.Max(0,p.b))),1);
                    else if(mode==1){float d=Mathf.Clamp01(p.r/Mathf.Max(.001f,r.depthDisplayRange));c=new Color(d,d,d,1);}
                    else if(mode==3){float d=Mathf.InverseLerp(shadowMin,shadowMax,p.r);c=new Color(d,d,d,1);}
                    else c=p.a<=0?Color.black:new Color(p.r*.5f+.5f,p.g*.5f+.5f,p.b*.5f+.5f,1);
                    display[i]=c;
                }
                var png=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
                try { png.SetPixels32(display);png.Apply(false,false);File.WriteAllBytes(r.prefix+"-"+name+".png",png.EncodeToPNG()); } finally { Destroy(png); }
            }
            finally { Destroy(texture); }
        }
        void WriteManifest(CaptureRequest r,string status)
        {
            var info=new CaptureManifest { status=status,requestFrame=r.requestFrame,captureFrame=r.captureFrame,width=r.width,height=r.height,expectedSectionVersion=r.expectedVersion,
                camera=targetCamera==null?"":targetCamera.name,injectionPoint="BeforePostProcess",depthDisplayRangeMetres=r.depthDisplayRange,source="Actual cameraColorBuffer, cameraDepthBuffer, cameraNormalBuffer from HDRP CustomPassContext",
                color="EXR: raw pre-postprocess camera HDR RGB (includes HDRP exposure convention). PNG: diagnostic Reinhard plus linear-to-sRGB; use normal screenshot for final graded color.",
                depth="EXR RGB: LinearEyeDepth in metres. PNG: clamp(depth / display range). Includes actual depth-writing geometry; transparent non-depth writers are absent.",
                normal="EXR RGB: world-space normal decoded from HDRP normal buffer. PNG: normal * 0.5 + 0.5; no-geometry pixels black. Transparent objects may not write this buffer.",
                orientation="GPU readback row order without manual flip; compare with the companion screenshot when reviewing.",
                scope="Read-only buffers. CPU versions are renderer declarations. Actual optional shadow atlas/cubemap readbacks provide pixels, but do not prove per-piece shadow/reflection identity or complete absence. Pixel auditor validates selected main-camera depth/normal samples only.",
                declaredContractAudit=r.audit,isolatedStaleDepthDetected=r.faultInjectionDetected,declaredStamps=r.stamps,saved=r.saved,errors=r.errors.ToArray(),captureOverheadExcludedFromPerformance=false,pixelAudit=r.pixelAudit,
                expectedBuffers=r.expectedBuffers,bufferWidths=r.bufferWidths,bufferHeights=r.bufferHeights,reflection=r.reflectionDescription,planarReflection=r.planarDescription,
                shadow="Actual GPU _ShadowmapCascadeAtlas, point sampled over whole atlas at1024x1024. EXR R=device depth, G/B=native atlas width/height. PNG min/max-normalized. Contact shadows are not isolated. Per-piece identity/absence NOT_VALIDATED by atlas readback alone.",shadowSourceWidth=r.shadowSourceWidth,shadowSourceHeight=r.shadowSourceHeight,shadowDepthRange=r.shadowDepthRange,
                reflectionScope="Six actual realtime cubemap faces at mip0, sampled at native face resolution. This is a local spatial approximation; no pixel-level per-piece absence verdict. Face labels and sampler direction conventions are in capture shader. Configure EveryFrame/no time slicing; metadata records configuration, not a fabricated GPU section token." };
            File.WriteAllText(r.manifestPath,JsonUtility.ToJson(info,true));
        }
        static string BufferName(int mode)=>mode==0?"camera-hdr":mode==1?"depth-metres":mode==2?"normal-world":mode==3?"shadow-atlas":mode==10?"reflection-planar":("reflection-"+new[]{"positive-x","negative-x","positive-y","negative-y","positive-z","negative-z"}[mode-4]);
        void OnDestroy()
        {
            if(volume!=null)Destroy(volume.gameObject);
            foreach(var r in active)foreach(var rt in r.targets)if(rt!=null){rt.Release();Destroy(rt);}
        }
        [Serializable] sealed class EvidencePass : CustomPass
        {
            [NonSerialized] public PassEvidenceCapture owner;
            Material material;
            protected override void Setup(ScriptableRenderContext renderContext,CommandBuffer cmd)
            {
                var shader=Resources.Load<Shader>("ShowcasePassEvidence");
                if(shader==null)throw new InvalidOperationException("ShowcasePassEvidence resource shader missing.");
                material=CoreUtils.CreateEngineMaterial(shader);
            }
            protected override void Execute(CustomPassContext ctx){if(owner!=null)owner.Record(ctx,material);}
            protected override void Cleanup(){CoreUtils.Destroy(material);}
        }
        sealed class CaptureRequest
        {
            public string prefix,manifestPath,audit,reflectionDescription,planarDescription;
            public int requestFrame,captureFrame,width,height,finished,expectedBuffers,shadowSourceWidth,shadowSourceHeight;
            public Vector2 shadowDepthRange;
            public long expectedVersion;
            public float queuedAt,depthDisplayRange;
            public bool faultInjectionDetected;
            public bool cancelled;
            public float[] depthPixels;
            public Color[] normalPixels;
            public SectionPixelAuditor.PixelProbe[] probes;
            public SectionPixelAuditor.AuditResult pixelAudit;
            public RendererPassStamp[] stamps;
            public readonly bool[] saved=new bool[11];
            public readonly int[] bufferWidths=new int[11],bufferHeights=new int[11];
            public readonly RenderTexture[] targets=new RenderTexture[11];
            public readonly List<string> errors=new List<string>();
        }
        sealed class CompletedBuffer { public CaptureRequest request;public int mode;public bool error;public byte[] bytes; }
        [Serializable] sealed class CaptureManifest
        {
            public string status,camera,injectionPoint,source,color,depth,normal,orientation,scope,declaredContractAudit;
            public int requestFrame,captureFrame,width,height;
            public long expectedSectionVersion;
            public float depthDisplayRangeMetres;
            public bool isolatedStaleDepthDetected,captureOverheadExcludedFromPerformance;
            public RendererPassStamp[] declaredStamps;
            public bool[] saved;
            public string[] errors;
            public SectionPixelAuditor.AuditResult pixelAudit;
            public int expectedBuffers,shadowSourceWidth,shadowSourceHeight;
            public int[] bufferWidths,bufferHeights;
            public string reflection,shadow,reflectionScope,planarReflection;
            public Vector2 shadowDepthRange;
        }
    }
    [Serializable] public sealed class RendererPassStamp
    {
        public string id,status;
        public long color,depth,normal,shadow,reflection;
        public int meshVertices,meshIndices;
    }
    public static class RendererContractAuditor
    {
        public static RendererPassStamp DeclaredStamp(SectionView view)
        {
            var mesh=view.Object.GetComponent<MeshFilter>().sharedMesh;
            int indices=0;if(mesh!=null)for(int s=0;s<mesh.subMeshCount;s++)indices+=(int)mesh.GetIndexCount(s);
            return new RendererPassStamp { id=view.Id,status=view.Last==null?"NotGenerated":view.Last.Status.ToString(),color=view.SectionVersion,depth=view.SectionVersion,normal=view.SectionVersion,shadow=view.SectionVersion,reflection=view.SectionVersion,meshVertices=mesh==null?0:mesh.vertexCount,meshIndices=indices };
        }
        public static string Audit(IEnumerable<RendererPassStamp> stamps,long expected)
        {
            foreach(var s in stamps)
            {
                if(s.color!=expected||s.depth!=expected||s.normal!=expected||s.shadow!=expected||s.reflection!=expected)return "FAIL: stale declared section version for "+s.id;
                if(s.status==SectionStatus.Empty.ToString()&&s.meshIndices!=0)return "FAIL: Empty section retains triangles for "+s.id;
                if(s.status==SectionStatus.Volume3D.ToString()&&s.meshIndices==0)return "FAIL: Volume3D section has no triangles for "+s.id;
            }
            return "PASS: CPU renderer declaration only; GPU shadow/reflection not verified";
        }
        public static bool IsolatedStaleDepthIsDetected()
        {
            var valid=new RendererPassStamp { id="isolated-fault-injection",status="Volume3D",color=42,depth=42,normal=42,shadow=42,reflection=42,meshVertices=8,meshIndices=36 };
            if(!Audit(new[]{valid},42).StartsWith("PASS",StringComparison.Ordinal))return false;
            valid.depth=41;return Audit(new[]{valid},42).StartsWith("FAIL",StringComparison.Ordinal);
        }
    }

    // Compare actual HDRP readback samples with intersections of the currently
    // uploaded meshes. This verifies sampled pixels, not the entire image.
    public static class SectionPixelAuditor
    {
        [Serializable] public sealed class MeshSnapshot { public string id;public Vector3[] vertices,screenVertices;public int[] triangles; }
        [Serializable] public sealed class GeometrySnapshot
        {
            public int width,height;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Matrix4x4 worldToCamera,projection;
            public MeshSnapshot[] meshes;
        }
        [Serializable] public sealed class RasterEdgeCandidate
        {
            public string id;
            public float marginPixels,depthMetres;
            public Vector3 normal;
        }
        [Serializable] public sealed class PixelProbe
        {
            public string id,kind,result;
            // False only for a former Empty silhouette whose ray misses every
            // current section mesh. Such a probe tests absence of the old depth,
            // not agreement with a current section depth or normal.
            public bool hasExpectedSectionSurface=true;
            public int x,y;
            public float expectedDepthMetres,formerDepthMetres,actualDepthMetres,depthErrorMetres,normalDot;
            public float projectedTriangleEdgeMarginPixels;
            public Vector3 expectedNormal;
            public RasterEdgeCandidate[] nearbyForegroundEdges;
            public string rasterBoundaryExplanation;
        }
        [Serializable] public sealed class AuditResult
        {
            public string status,scope;
            public string isolatedReadbackDepthMutation;
            public int probes,matchedDepth,matchedNormals,occluded,stalePixelsExcluded,failures;
            public int formerSurfaceAbsenceOnly;
            public int rasterBoundaryUnresolved;
            public float rasterBoundaryMarginPixels=1f/128;
            public float depthToleranceMetres=.001f,normalDotMinimum=.75f;
            public PixelProbe[] samples;
        }
        public static PixelProbe[] BuildProbes(SectionView[] views,Camera camera,int width,int height,Dictionary<string,MeshSnapshot> history,out MeshSnapshot[] oracleMeshes)
        {
            var current=new List<MeshSnapshot>();
            foreach(var view in views)
            {
                if(view.Last==null||view.Last.Status!=SectionStatus.Volume3D)continue;
                var mesh=view.Object.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++)vertices[i]=view.Object.transform.TransformPoint(vertices[i]);
                var projected=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++){var p=camera.WorldToViewportPoint(vertices[i]);projected[i]=new Vector3(p.x*width,p.y*height,p.z);}
                current.Add(new MeshSnapshot{id=view.Id,vertices=vertices,screenVertices=projected,triangles=mesh.triangles});
            }
            var probes=new List<PixelProbe>();var used=new HashSet<string>();
            foreach(var mesh in current)Sample(mesh,false,current,camera,width,height,probes,used);
            foreach(var view in views)
                if(view.Last!=null&&view.Last.Status==SectionStatus.Empty&&history.TryGetValue(view.Id,out var old))Sample(old,true,current,camera,width,height,probes,used);
            foreach(var mesh in current)history[mesh.id]=mesh;
            oracleMeshes=current.ToArray();
            return probes.ToArray();
        }
        static void Sample(MeshSnapshot mesh,bool former,List<MeshSnapshot> current,Camera camera,int width,int height,List<PixelProbe> probes,HashSet<string> used)
        {
            int step=Math.Max(1,mesh.triangles.Length/3/12);
            for(int tri=0;tri<mesh.triangles.Length/3;tri+=step)
            {
                Vector3 a=mesh.vertices[mesh.triangles[tri*3]],b=mesh.vertices[mesh.triangles[tri*3+1]],c=mesh.vertices[mesh.triangles[tri*3+2]];
                Vector3 normal=NormalizeSmallTriangle(Vector3.Cross(b-a,c-a));
                if(Vector3.Dot(normal,camera.transform.position-(a+b+c)/3)<=0)continue;
                for(int kind=0;kind<2;kind++)
                {
                    Vector3 point=kind==0?(a+b+c)/3:a*.06f+b*.47f+c*.47f;
                    Vector3 screen=camera.WorldToViewportPoint(point);if(screen.z<=0)continue;
                    int x=(int)(screen.x*width),y=(int)(screen.y*height);
                    if(x<1||x>=width-1||y<1||y>=height-1)continue;
                    string key=x+","+y+","+former;if(used.Contains(key))continue;
                    Ray ray=camera.ViewportPointToRay(new Vector3((x+.5f)/width,(y+.5f)/height,0));
                    var pixel=new Vector2(x+.5f,y+.5f);
                    bool hasCurrentSurface=Nearest(current,ray,out var hit,out var hitNormal,out string hitId,out float edgeMargin,camera,width,height,pixel);
                    if(!hasCurrentSurface&&!former)continue;
                    float expected=hasCurrentSurface?camera.WorldToViewportPoint(hit).z:0,oldDepth=0;
                    if(former)
                    {
                        if(!Nearest(new List<MeshSnapshot>{mesh},ray,out var oldHit,out _,out _,out _,camera,width,height,pixel))continue;
                        oldDepth=camera.WorldToViewportPoint(oldHit).z;
                        if(hasCurrentSurface&&expected-oldDepth<.003f)continue;
                    }
                    used.Add(key);probes.Add(new PixelProbe{id=former?mesh.id:hitId,kind=former?"former-empty-silhouette":kind==0?"interior":"near-edge",hasExpectedSectionSurface=hasCurrentSurface,x=x,y=y,expectedDepthMetres=expected,formerDepthMetres=oldDepth,expectedNormal=hitNormal,projectedTriangleEdgeMarginPixels=edgeMargin,nearbyForegroundEdges=hasCurrentSurface?NearbyEdges(current,pixel,expected):Array.Empty<RasterEdgeCandidate>()});
                }
            }
        }
        static bool Nearest(List<MeshSnapshot> meshes,Ray ray,out Vector3 hit,out Vector3 normal,out string id,out float edgeMargin,Camera camera,int width,int height,Vector2 pixel)
        {
            float nearest=float.PositiveInfinity;hit=default;normal=default;id="";edgeMargin=0;
            foreach(var mesh in meshes)for(int i=0;i<mesh.triangles.Length;i+=3)
            {
                Vector3 a=mesh.vertices[mesh.triangles[i]],b=mesh.vertices[mesh.triangles[i+1]],c=mesh.vertices[mesh.triangles[i+2]];
                Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,p);
                if(Mathf.Abs(det)<1e-10f)continue;
                float inverse=1/det;Vector3 t=ray.origin-a;float u=Vector3.Dot(t,p)*inverse;if(u<0||u>1)continue;
                Vector3 q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)*inverse;if(v<0||u+v>1)continue;
                float distance=Vector3.Dot(e2,q)*inverse;if(distance<0||distance>=nearest)continue;
                nearest=distance;hit=ray.GetPoint(distance);normal=NormalizeSmallTriangle(Vector3.Cross(e1,e2));id=mesh.id;
                Vector3 pa=camera.WorldToViewportPoint(a),pb=camera.WorldToViewportPoint(b),pc=camera.WorldToViewportPoint(c);
                pa=new Vector3(pa.x*width,pa.y*height,0);pb=new Vector3(pb.x*width,pb.y*height,0);pc=new Vector3(pc.x*width,pc.y*height,0);
                edgeMargin=Mathf.Min(EdgeDistancePixels(pixel,pa,pb),Mathf.Min(EdgeDistancePixels(pixel,pb,pc),EdgeDistancePixels(pixel,pc,pa)));
            }
            return !float.IsPositiveInfinity(nearest);
        }
        static float EdgeDistancePixels(Vector2 point,Vector3 a,Vector3 b)
        {
            double dx=(double)b.x-a.x,dy=(double)b.y-a.y;
            double length=Math.Sqrt(dx*dx+dy*dy);
            return length>1e-12?(float)(Math.Abs(dx*((double)point.y-a.y)-dy*((double)point.x-a.x))/length):0;
        }
        static Vector3 NormalizeSmallTriangle(Vector3 value)
        {
            // Meshes are measured in metres. Unity's Vector3.normalized threshold
            // treats valid tiny bevel cross products as zero-length vectors.
            float squared=Vector3.Dot(value,value);
            return squared>1e-30f?value/Mathf.Sqrt(squared):Vector3.zero;
        }
        static RasterEdgeCandidate[] NearbyEdges(List<MeshSnapshot> meshes,Vector2 pixel,float expectedDepth)
        {
            var candidates=new List<RasterEdgeCandidate>();
            foreach(var mesh in meshes)for(int t=0;t<mesh.triangles.Length;t+=3)
            {
                int ia=mesh.triangles[t],ib=mesh.triangles[t+1],ic=mesh.triangles[t+2];
                Vector3 a=mesh.screenVertices[ia],b=mesh.screenVertices[ib],c=mesh.screenVertices[ic];
                if(a.z<=0||b.z<=0||c.z<=0)continue;
                float best=float.PositiveInfinity,depth=0;
                for(int edge=0;edge<3;edge++)
                {
                    Vector3 p=edge==0?a:edge==1?b:c,q=edge==0?b:edge==1?c:a;
                    double dx=(double)q.x-p.x,dy=(double)q.y-p.y,lengthSquared=dx*dx+dy*dy;
                    if(lengthSquared<1e-20)continue;
                    double s=Math.Max(0,Math.Min(1,(((double)pixel.x-p.x)*dx+((double)pixel.y-p.y)*dy)/lengthSquared));
                    double ex=p.x+s*dx-pixel.x,ey=p.y+s*dy-pixel.y;
                    float distance=(float)Math.Sqrt(ex*ex+ey*ey),z=(float)(1/((1-s)/p.z+s/q.z));
                    if(distance<best&&z<=expectedDepth+.001f){best=distance;depth=z;}
                }
                if(best<=1f/128)candidates.Add(new RasterEdgeCandidate { id=mesh.id,marginPixels=best,depthMetres=depth,normal=NormalizeSmallTriangle(Vector3.Cross(mesh.vertices[ib]-mesh.vertices[ia],mesh.vertices[ic]-mesh.vertices[ia])) });
            }
            return candidates.ToArray();
        }
        static bool IsRasterBoundary(PixelProbe p,Color actualNormal,float margin)
        {
            if(p.projectedTriangleEdgeMarginPixels<=margin)
            {p.rasterBoundaryExplanation="CPU-hit triangle edge distance "+p.projectedTriangleEdgeMarginPixels+" pixels";return true;}
            if(p.nearbyForegroundEdges!=null)foreach(var c in p.nearbyForegroundEdges)
            {
                float dot=Vector3.Dot(new Vector3(actualNormal.r,actualNormal.g,actualNormal.b).normalized,c.normal);
                if(c.marginPixels<=margin&&Mathf.Abs(c.depthMetres-p.actualDepthMetres)<=.001f&&dot>=.75f)
                {p.rasterBoundaryExplanation="Nearby projected edge of "+c.id+"; margin="+c.marginPixels+" pixels; edge depth="+c.depthMetres+" metres; actual normal dot="+dot;return true;}
            }
            return false;
        }
        public static string VerifyDepthMutation(AuditResult baseline,float[] depth,Color[] normals,int width,int height)
        {
            var sample=baseline.samples.FirstOrDefault(p=>(p.result=="PASS_SAMPLED_DEPTH_AND_NORMAL"||p.result=="PASS_FORMER_SURFACE_ABSENT")&&p.kind=="former-empty-silhouette")
                ??baseline.samples.FirstOrDefault(p=>p.result=="PASS_SAMPLED_DEPTH_AND_NORMAL"&&p.projectedTriangleEdgeMarginPixels>baseline.rasterBoundaryMarginPixels);
            if(sample==null)return "NOT_RUN: no passing baseline sample for isolated readback mutation";
            var copy=new PixelProbe { id=sample.id,kind=sample.kind,hasExpectedSectionSurface=sample.hasExpectedSectionSurface,x=sample.x,y=sample.y,expectedDepthMetres=sample.expectedDepthMetres,formerDepthMetres=sample.formerDepthMetres,expectedNormal=sample.expectedNormal,projectedTriangleEdgeMarginPixels=sample.projectedTriangleEdgeMarginPixels };
            var changed=(float[])depth.Clone();
            changed[copy.y*width+copy.x]=copy.kind=="former-empty-silhouette"?copy.formerDepthMetres:copy.expectedDepthMetres+.02f;
            var injected=Validate(new[]{copy},changed,normals,width,height);
            return (injected.failures==1?"PASS_DETECTED: ":"FAIL_NOT_DETECTED: ")+copy.result+"; isolated CPU mutation of one actual GPU-readback depth pixel; no GPU render fault was injected; original pixels and baseline probes were preserved";
        }
        public static AuditResult Validate(PixelProbe[] samples,float[] depth,Color[] normals,int width,int height)
        {
            var result=new AuditResult { probes=samples==null?0:samples.Length,samples=samples??Array.Empty<PixelProbe>(),scope="Actual HDRP depth/normal readback versus current uploaded mesh ray intersections at sampled interior/near-edge pixels. Historical Empty silhouettes also test selected stale-depth pixels. A former silhouette with hasExpectedSectionSurface=false has no current section ray hit: PASS_FORMER_SURFACE_ABSENT means only that finite actual depth lies more than 1 mm behind the old surface; current background depth/normal agreement is NOT_VALIDATED and does not increment matchedDepth/matchedNormals. Other scene occluders are classified separately. A disagreement within 1/128 pixel of a projected triangle edge is NOT_VALIDATED_RASTER_BOUNDARY, never a passing normal/depth match; it bounds subpixel raster coverage ambiguity. This does not validate every pixel, final color, shadows, reflections, or temporal history." };
            if(depth==null||normals==null||depth.Length!=width*height||normals.Length!=width*height){result.status="NOT_VALIDATED: missing readback arrays";return result;}
            foreach(var p in result.samples)
            {
                int index=p.y*width+p.x;p.actualDepthMetres=depth[index];p.depthErrorMetres=p.hasExpectedSectionSurface?p.actualDepthMetres-p.expectedDepthMetres:0;
                var n=normals[index];p.normalDot=p.hasExpectedSectionSurface?Vector3.Dot(new Vector3(n.r,n.g,n.b).normalized,p.expectedNormal):0;
                if(float.IsNaN(p.actualDepthMetres)||float.IsInfinity(p.actualDepthMetres)){p.result="FAIL_NONFINITE_DEPTH";result.failures++;continue;}
                if(!p.hasExpectedSectionSurface&&p.kind!="former-empty-silhouette"){p.result="FAIL_PROBE_WITHOUT_EXPECTED_SECTION";result.failures++;continue;}
                if(p.kind=="former-empty-silhouette")
                {
                    if(p.actualDepthMetres<p.formerDepthMetres-.001f){p.result="OCCLUDED_BEFORE_FORMER_SURFACE";result.occluded++;continue;}
                    if(p.actualDepthMetres<=p.formerDepthMetres+.001f){p.result="FAIL_POSSIBLE_STALE_DEPTH";result.failures++;continue;}
                    result.stalePixelsExcluded++;
                    if(!p.hasExpectedSectionSurface){p.result="PASS_FORMER_SURFACE_ABSENT";result.formerSurfaceAbsenceOnly++;continue;}
                }
                if(p.depthErrorMetres<-.001f){p.result="OCCLUDED_BY_OTHER_SCENE_GEOMETRY";result.occluded++;continue;}
                if(p.depthErrorMetres>.001f)
                {
                    if(IsRasterBoundary(p,n,result.rasterBoundaryMarginPixels)){p.result="NOT_VALIDATED_RASTER_BOUNDARY_DEPTH";result.rasterBoundaryUnresolved++;continue;}
                    p.result="FAIL_DEPTH_BEHIND_CURRENT_MESH";result.failures++;continue;
                }
                result.matchedDepth++;
                if(n.a<=0||p.normalDot<.75f)
                {
                    if(IsRasterBoundary(p,n,result.rasterBoundaryMarginPixels)){p.result="NOT_VALIDATED_RASTER_BOUNDARY_NORMAL";result.rasterBoundaryUnresolved++;continue;}
                    p.result="FAIL_NORMAL_MISMATCH";result.failures++;continue;
                }
                result.matchedNormals++;p.result="PASS_SAMPLED_DEPTH_AND_NORMAL";
            }
            result.status=result.failures>0?"FAIL_SAMPLED_PIXELS":result.matchedDepth==0&&result.formerSurfaceAbsenceOnly==0?"NOT_VALIDATED_NO_UNOCCLUDED_SAMPLES":result.rasterBoundaryUnresolved>0?"PARTIAL_SAMPLED_PIXELS_RASTER_BOUNDARIES_UNRESOLVED":result.matchedDepth==0?"PASS_SAMPLED_FORMER_SURFACE_ABSENCE_ONLY":"PASS_SAMPLED_PIXELS";
            return result;
        }
    }
}
