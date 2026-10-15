using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Showcase.Unity
{
    [Serializable] public sealed class RunHeader
    {
        public string runId, startedUtc, unity, device, driver, graphicsApi, profile, mode, occlusion, shape, material, buildSource;
        public string sourceManifestSha256,sourceHash,buildUtc;
        public int width,height,graphicsMemoryMB,seed;
        public string rules="showcase-asymmetric-4d-king-capture/0.1", observation="logical-w-zero/1", slice="uw-observer-rotation/1";
        public string antialiasing="SMAA", internalResolution="100%", frameGeneration="off", cpuBudget="5000 nodes";
        public string quality,cpuModel;
        public string captureMode;
        public string scenarioVersion,transitionEffect,lightingVersion,initialLightingEffects;
        public int targetFrameRate,expectedPlies;
    }
    [Serializable] public sealed class BuildStamp
    {
        public string sourceManifestSha256,sourceHash,builtUtc;
    }
    [Serializable] public sealed class FrameEvidence
    {
        public int frame,ply,scene,visiblePieces; public double elapsed,angle,frameMs,cpuMainMs,cpuRenderMs,cpuPresentWaitMs,gpuMs,sectionMs;
        public long monoBytes,totalAllocatedBytes,gcDelta,gcGen1Delta,gcGen2Delta; public string stateHash,visibleIds;
        public float transitionStrength;public bool transitionEnabled;
    }
    [Serializable] public sealed class EventEvidence
    {
        public string kind,message,stateHash;public int ply,scene;public double time,angle;
    }
    [Serializable] public sealed class PerformanceSummary
    {
        public int samples,width,height;public string profile,mode,runId,measurement;
        public double medianMs,p95Ms,p99Ms,worstMs,averageFps,gpuMedianMs,gpuP95Ms,cpuMedianMs,cpuP95Ms;
        public long gcCollections,gcGen1Collections,gcGen2Collections,monoBytes,totalAllocatedBytes;
        public string gcMeasurement="GC.CollectionCount(0) delta; gen1/gen2 reported separately, never summed into a collection count. Occupancy bytes are not allocation throughput.";
        public string gpuTimingAvailability,vramAvailability="Hardware VRAM capacity in header; process-resident VRAM requires external measurement.", conclusion;
    }
    public sealed class RuntimeEvidence : IDisposable
    {
        public readonly string DirectoryPath;
        public readonly RunHeader Header;
        StreamWriter frames,events;
        readonly List<double> times=new List<double>(30000),gpuTimes=new List<double>(30000),cpuTimes=new List<double>(30000);
        readonly FrameTiming[] timing=new FrameTiming[1];
        int initialGC,initialGC1,initialGC2; bool closed;
        public FrameEvidence LastFrame { get; private set; }
        public RuntimeEvidence(string path,RunHeader header)
        {
            Header=header;DirectoryPath=path;Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"header.json"),JsonUtility.ToJson(header,true));
            frames=new StreamWriter(Path.Combine(path,"frames.jsonl"));events=new StreamWriter(Path.Combine(path,"events.jsonl"));
            initialGC=GC.CollectionCount(0);initialGC1=GC.CollectionCount(1);initialGC2=GC.CollectionCount(2);
        }
        public void Event(EventEvidence e){if(closed)return;events.WriteLine(JsonUtility.ToJson(e));events.Flush();}
        public void Frame(FrameEvidence f)
        {
            if(closed)return;
            FrameTimingManager.CaptureFrameTimings();
            uint n=FrameTimingManager.GetLatestTimings(1,timing);
            if(n>0){f.cpuMainMs=timing[0].cpuMainThreadFrameTime;f.cpuRenderMs=timing[0].cpuRenderThreadFrameTime;f.cpuPresentWaitMs=timing[0].cpuMainThreadPresentWaitTime;f.gpuMs=timing[0].gpuFrameTime;}
            f.monoBytes=Profiler.GetMonoUsedSizeLong();f.totalAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong();
            f.gcDelta=GC.CollectionCount(0)-initialGC;f.gcGen1Delta=GC.CollectionCount(1)-initialGC1;f.gcGen2Delta=GC.CollectionCount(2)-initialGC2;
            frames.WriteLine(JsonUtility.ToJson(f));times.Add(f.frameMs);LastFrame=f;
            if(f.gpuMs>0)gpuTimes.Add(f.gpuMs);if(f.cpuMainMs>0)cpuTimes.Add(f.cpuMainMs);
        }
        public static double Percentile(List<double> values,double p)
        {if(values.Count==0)return 0;var a=values.OrderBy(x=>x).ToArray();return a[(int)Math.Min(a.Length-1,Math.Ceiling(p*a.Length)-1)];}
        public void Dispose()
        {
            if(closed)return;closed=true;frames.Dispose();events.Dispose();
            var s=new PerformanceSummary{samples=times.Count,width=Header.width,height=Header.height,profile=Header.profile,mode=Header.mode,runId=Header.runId,
                measurement="Standalone real rendered frames, includes cold frames. Capture mode: "+Header.captureMode,medianMs=Percentile(times,.5),p95Ms=Percentile(times,.95),p99Ms=Percentile(times,.99),worstMs=Percentile(times,1),
                averageFps=times.Count>0?1000/times.Average():0,gpuMedianMs=Percentile(gpuTimes,.5),gpuP95Ms=Percentile(gpuTimes,.95),cpuMedianMs=Percentile(cpuTimes,.5),cpuP95Ms=Percentile(cpuTimes,.95),
                gcCollections=GC.CollectionCount(0)-initialGC,gcGen1Collections=GC.CollectionCount(1)-initialGC1,gcGen2Collections=GC.CollectionCount(2)-initialGC2,monoBytes=Profiler.GetMonoUsedSizeLong(),totalAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),
                gpuTimingAvailability=gpuTimes.Count>0?"available":"NOT_AVAILABLE",conclusion="HUMAN_PENDING; compare distributions to "+(1000.0/Math.Max(1,Header.targetFrameRate)).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" ms target; do not infer acceptance from average FPS."};
            File.WriteAllText(Path.Combine(DirectoryPath,"performance.json"),JsonUtility.ToJson(s,true));
        }
    }
}
