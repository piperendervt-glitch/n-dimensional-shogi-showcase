using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Showcase.Unity
{
    // Isolated GPU fault fixture. Only private diagnostic RTs are writable.
    // The showcase camera depth/color/normal and all real meshes remain untouched.
    public sealed class GpuStaleDepthFault : MonoBehaviour
    {
        readonly List<Pending> pending=new List<Pending>();
        readonly ConcurrentQueue<Readback> completed=new ConcurrentQueue<Readback>();
        Material material;
        public bool IsBusy=>pending.Count>0;
        public string LastError { get; private set; }="";
        public bool HasFailures { get; private set; }

        public void Capture(CustomPassContext ctx,Camera camera,string prefix,SectionPixelAuditor.MeshSnapshot[] historical,SectionPixelAuditor.PixelProbe[] probes,long expectedVersion)
        {
            if(material==null)
            {
                var shader=Resources.Load<Shader>("ShowcaseGpuStaleDepthFault");
                if(shader==null)throw new InvalidOperationException("GPU stale-depth fixture shader missing.");
                material=CoreUtils.CreateEngineMaterial(shader);
            }
            var p=new Pending { prefix=prefix,width=ctx.hdCamera.actualWidth,height=ctx.hdCamera.actualHeight,frame=Time.frameCount,sectionVersion=expectedVersion,
                probes=probes.Where(s=>s.kind=="former-empty-silhouette").Select(Clone).ToArray(),historicalIds=historical.Select(m=>m.id).ToArray(),historicalMeshes=historical };
            if(p.probes.Length==0||historical.Length==0)
            {
                HasFailures=true;
                const string reason="GPU stale-depth fixture has no historical Empty mesh/probes.";
                Debug.LogError(reason);
                File.WriteAllText(prefix+"-gpu-stale-depth-fault.json",JsonUtility.ToJson(new Report{status="NOT_RUN_NO_HISTORICAL_EMPTY_PROBES",scope=Scope,error=reason},true));
                return;
            }
            pending.Add(p);
            for(int i=0;i<3;i++)
            {
                p.targets[i]=new RenderTexture(p.width,p.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) {name="Isolated GPU depth fault "+i,filterMode=FilterMode.Point,useMipMap=false,autoGenerateMips=false,antiAliasing=1};
                p.targets[i].Create();
            }
            ctx.cmd.SetGlobalTexture("_ShowcaseFaultSourceDepth",ctx.cameraDepthBuffer.nameID);
            ctx.cmd.SetGlobalTexture("_ShowcaseFaultSourceNormal",ctx.cameraNormalBuffer.nameID);
            HDUtils.DrawFullScreen(ctx.cmd,new Rect(0,0,p.width,p.height),material,new RenderTargetIdentifier(p.targets[0]),null,0);
            HDUtils.DrawFullScreen(ctx.cmd,new Rect(0,0,p.width,p.height),material,new RenderTargetIdentifier(p.targets[1]),null,1);
            ctx.cmd.CopyTexture(p.targets[0],p.targets[2]);
            // vertices are already in world space. Explicit matrices avoid HDRP
            // camera-relative object transforms or any change to the scene camera.
            var view=camera.worldToCameraMatrix;
            var gpuProjection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            p.view=view;p.gpuProjection=gpuProjection;
            var properties=new MaterialPropertyBlock();properties.SetMatrix("_ShowcaseFaultVP",gpuProjection*view);properties.SetMatrix("_ShowcaseFaultView",view);
            ctx.cmd.SetRenderTarget(p.targets[2]);ctx.cmd.SetViewport(new Rect(0,0,p.width,p.height));
            foreach(var old in historical)
            {
                var mesh=new Mesh { name="Private historical fault mesh "+old.id,indexFormat=IndexFormat.UInt32 };
                mesh.vertices=(Vector3[])old.vertices.Clone();mesh.triangles=(int[])old.triangles.Clone();mesh.RecalculateBounds();p.meshes.Add(mesh);
                ctx.cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,2,properties);
            }
            for(int i=0;i<3;i++)
            {
                int mode=i;
                ctx.cmd.RequestAsyncReadback(p.targets[i],0,TextureFormat.RGBAFloat,r=>
                {
                    var item=new Readback{pending=p,mode=mode,error=r.hasError};
                    if(!r.hasError)item.bytes=r.GetData<byte>().ToArray();
                    completed.Enqueue(item);
                });
            }
            File.WriteAllText(prefix+"-gpu-stale-depth-fault-status.txt","READBACK_PENDING: private GPU depth fault only\n");
        }

        void Update()
        {
            while(completed.TryDequeue(out var item))
            {
                var p=item.pending;
                if(item.error)p.error="GPU diagnostic readback failed";
                else p.raw[item.mode]=item.bytes;
                p.finished++;
                if(p.finished!=3)continue;
                try
                {
                    if(!p.cancelled)Finish(p);
                }
                catch(Exception e){HasFailures=true;LastError=e.ToString();Debug.LogError("GPU stale-depth fixture: "+e);}
                finally {Dispose(p);pending.Remove(p);}
            }
        }

        void Finish(Pending p)
        {
            if(!string.IsNullOrEmpty(p.error))throw new InvalidOperationException(p.error);
            var depth=Depth(p.raw[0]);var normals=Normals(p.raw[1]);var fault=Depth(p.raw[2]);
            var baseline=SectionPixelAuditor.Validate(p.probes.Select(Clone).ToArray(),depth,normals,p.width,p.height);
            // Only baseline-passing probes can demonstrate detection of the newly
            // injected fault. Preserve the baseline report and all original arrays.
            var passing=baseline.samples.Where(s=>s.result=="PASS_SAMPLED_DEPTH_AND_NORMAL"||s.result=="PASS_FORMER_SURFACE_ABSENT").Select(Clone).ToArray();
            var injected=SectionPixelAuditor.Validate(passing,fault,normals,p.width,p.height);
            int changed=0,detected=0;
            foreach(var s in injected.samples)
            {
                int index=s.y*p.width+s.x;
                if(Mathf.Abs(depth[index]-fault[index])>.001f)changed++;
                if(s.result=="FAIL_POSSIBLE_STALE_DEPTH"&&Mathf.Abs(fault[index]-s.formerDepthMetres)<=.001f)detected++;
            }
            bool success=passing.Length>0&&changed>0&&detected>0;
            var report=new Report {status=success?"PASS_GPU_STALE_DEPTH_DETECTED":"FAIL_GPU_STALE_DEPTH_NOT_DETECTED",scope=Scope,frame=p.frame,width=p.width,height=p.height,
                expectedSectionVersion=p.sectionVersion,historicalMeshIds=p.historicalIds,historicalMeshes=p.historicalMeshes,baselinePassingProbes=passing.Length,changedSelectedDepthPixels=changed,detectedStaleDepthPixels=detected,
                baselinePassingSectionSurfaceProbes=passing.Count(s=>s.hasExpectedSectionSurface),baselinePassingAbsenceOnlyProbes=passing.Count(s=>!s.hasExpectedSectionSurface),
                baseline=baseline,injected=injected,worldToCamera=p.view,gpuProjection=p.gpuProjection,
                source="Actual HDRP cameraDepthBuffer and cameraNormalBuffer at BeforePostProcess, copied on the GPU; historical uploaded world-space triangles drawn by CommandBuffer.DrawMesh into a private copy of linear eye depth.",
                expectedFailure="The same SectionPixelAuditor.Validate must report FAIL_POSSIBLE_STALE_DEPTH at a baseline-passing former-empty-silhouette pixel.",
                writes="Three new private ARGBFloat RTs only. No writes to camera color/depth/normal, no changes to real scene Mesh objects or materials. The private copy uses Min blending to retain the nearer linear depth.",
                faultDepthFile=Path.GetFileName(p.prefix+"-gpu-stale-depth-fault.exr")};
            var texture=new Texture2D(p.width,p.height,TextureFormat.RGBAFloat,false,true);
            try {texture.LoadRawTextureData(p.raw[2]);texture.Apply(false,false);File.WriteAllBytes(p.prefix+"-gpu-stale-depth-fault.exr",texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat|Texture2D.EXRFlags.CompressZIP));}
            finally {Destroy(texture);}
            File.WriteAllText(p.prefix+"-gpu-stale-depth-fault.json",JsonUtility.ToJson(report,true));
            File.WriteAllText(p.prefix+"-gpu-stale-depth-fault-status.txt",report.status+"\n");
            if(!success){HasFailures=true;Debug.LogError(report.status+": "+p.prefix);}
        }

        public void CancelPending(string reason="Run restarted")
        {
            LastError="";HasFailures=false;
            foreach(var p in pending){p.cancelled=true;File.WriteAllText(p.prefix+"-gpu-stale-depth-fault-status.txt","CANCELLED: "+reason+"\n");}
        }
        static SectionPixelAuditor.PixelProbe Clone(SectionPixelAuditor.PixelProbe p)=>JsonUtility.FromJson<SectionPixelAuditor.PixelProbe>(JsonUtility.ToJson(p));
        static float[] Depth(byte[] raw)
        {
            var values=new float[raw.Length/4];Buffer.BlockCopy(raw,0,values,0,raw.Length);var result=new float[values.Length/4];
            for(int i=0;i<result.Length;i++)result[i]=values[i*4];return result;
        }
        static Color[] Normals(byte[] raw)
        {
            var values=new float[raw.Length/4];Buffer.BlockCopy(raw,0,values,0,raw.Length);var result=new Color[values.Length/4];
            for(int i=0;i<result.Length;i++)result[i]=new Color(values[i*4],values[i*4+1],values[i*4+2],values[i*4+3]);return result;
        }
        void Dispose(Pending p)
        {
            foreach(var rt in p.targets)if(rt!=null){rt.Release();Destroy(rt);}
            foreach(var mesh in p.meshes)Destroy(mesh);
        }
        void OnDestroy(){foreach(var p in pending){p.cancelled=true;Dispose(p);}pending.Clear();CoreUtils.Destroy(material);}
        const string Scope="Isolated actual GPU stale-one-pass fault test on selected former Empty silhouette pixels. Baseline passing probes include current-section depth/normal matches and separately counted absence-only probes whose rays miss current section meshes. Absence-only probes validate finite actual depth farther than 1 mm behind the former surface, not background depth/normal agreement. Rasterized historical triangles must trigger the same FAIL_POSSIBLE_STALE_DEPTH while current normal/color and real renderer state remain unchanged. This does not validate every pixel, shadow, reflection or temporal history.";
        sealed class Pending
        {
            public string prefix,error;public int width,height,frame,finished;public long sectionVersion;public bool cancelled;
            public string[] historicalIds;public SectionPixelAuditor.MeshSnapshot[] historicalMeshes;public Matrix4x4 view,gpuProjection;public SectionPixelAuditor.PixelProbe[] probes;
            public readonly RenderTexture[] targets=new RenderTexture[3];public readonly byte[][] raw=new byte[3][];public readonly List<Mesh> meshes=new List<Mesh>();
        }
        sealed class Readback{public Pending pending;public int mode;public bool error;public byte[] bytes;}
        [Serializable] sealed class Report
        {
            public string status,scope,error,source,expectedFailure,writes,faultDepthFile;
            public int frame,width,height,baselinePassingProbes,changedSelectedDepthPixels,detectedStaleDepthPixels;
            public int baselinePassingSectionSurfaceProbes,baselinePassingAbsenceOnlyProbes;
            public long expectedSectionVersion;public string[] historicalMeshIds;public SectionPixelAuditor.MeshSnapshot[] historicalMeshes;
            public Matrix4x4 worldToCamera,gpuProjection;public SectionPixelAuditor.AuditResult baseline,injected;
        }
    }
}
