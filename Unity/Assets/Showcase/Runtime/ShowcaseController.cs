using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NDShogi.Showcase.Core;
using Showcase.Geometry;
using NDimensionalShogi.Showcase.Environment;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Showcase.Unity
{
    public sealed class ShowcaseController : MonoBehaviour
    {
        EnvironmentRig rig;
        ScenarioRunner runner;
        MoveTransaction tx;
        GameState initialState;
        ReplayFixture replay;
        RuntimeEvidence evidence;
        readonly Dictionary<string,SectionView> pieces=new Dictionary<string,SectionView>();
        readonly Dictionary<string,Material> labels=new Dictionary<string,Material>();
        readonly List<VecN> guideCells=new List<VecN>();
        SectionView board;
        GeometryGuides guides;
        ShapeVariant variant=ShapeVariant.C;
        string profile="ShowcaseRaster",mode="DirectedLive",outputRoot,failed="";
        OcclusionKind occlusion=OcclusionKind.LatticeLine;
        bool paused,debug,exitDialog,previousPause,helpers=true,quitting,recordFrames,exitAtEnd,startPaused;
        int scene=1,runIndex,frameIndex,loopCount=1,seed=ScenarioRunner.DefaultSeed,targetFps=30;
        double angle,elapsed,sectionMs,lastCapture,previousGuideAngle=double.NaN;
        long sectionVersion;
        float speed=1;
        string movingId;
        VecN movingPosition;
        List<PlyRecord> records=new List<PlyRecord>();
        readonly Stopwatch watch=new Stopwatch();
        Texture2D captureTexture;
        GUIStyle overlay,labelStyle;
        Vector3 cameraGoal,lookGoal;
        bool diagnosticComparison,diagnosticLighting,diagnosticGpu;
        bool capturePasses,noScreenshots,captureFinished;
        PassEvidenceCapture passCapture;
        GameObject endingGlowObject;
        DimensionalTransition transition;
        ShowcaseRenderOutput renderOutput;
        bool transitionAllowed=true;
        float transitionPhase,endingGlowLumens;
        int requestedWidth,requestedHeight;
        readonly ParallelOptions sectionWorkers=new ParallelOptions{MaxDegreeOfParallelism=Math.Max(1,Math.Min(8,System.Environment.ProcessorCount-2))};
        struct SectionJob {public SectionView view;public VecN q;public int forward;public SectionResult result;}
        readonly List<SectionJob> sectionJobs=new List<SectionJob>(41);
        long visibleVersion=-1;int visiblePieceSet=-1,visibleCount;string visibleIds="";
        readonly System.Text.StringBuilder visibleBuilder=new System.Text.StringBuilder(512);

        void Start()
        {
            try
            {
                ParseArguments();Application.runInBackground=true;Application.targetFrameRate=targetFps;QualitySettings.vSyncCount=0;
                rig=ShowcaseEnvironment.Build();rig.SetGeometryReference(profile=="GeometryReference");
                renderOutput=gameObject.AddComponent<ShowcaseRenderOutput>();renderOutput.Initialize(rig.Camera,noScreenshots);
                transition=gameObject.AddComponent<DimensionalTransition>();transition.Bind(rig.Camera);
                transition.EffectEnabled=profile=="ShowcaseRaster"&&transitionAllowed;
                rig.Debug.Changed+=message=>Event("LightingDebugChanged",message);
                MakeMaterials();guides=new GeometryGuides();
                board=new SectionView("Board",ShapeFactory.CreateBoard(),labels["Board"],rig.Materials.BrightWood,true);
                if(capturePasses){passCapture=gameObject.AddComponent<PassEvidenceCapture>();passCapture.Bind(rig.Camera,()=>pieces.Values.Concat(new[]{board}),()=>sectionVersion);passCapture.BindReflection(()=>rig.DynamicReflection);passCapture.BindPlanar(()=>rig.TableReflection);}
                RestartRun();
            }
            catch(Exception e){Fail(e);}
        }
        void ParseArguments()
        {
            string[] args=System.Environment.GetCommandLineArgs();
            string Value(string key,string fallback){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
            profile=Value("--profile",profile);if(profile!="ShowcaseRaster"&&profile!="GeometryReference")throw new ArgumentException("Unknown profile");
            mode=Value("--mode",mode);if(mode!="DirectedLive"&&mode!="VerifiedReplay")throw new ArgumentException("Unknown mode");
            variant=(ShapeVariant)Enum.Parse(typeof(ShapeVariant),Value("--shape","C"));
            occlusion=Ruleset.ParseOcclusion(Value("--occlusion","lattice-line/1"));
            seed=int.Parse(Value("--seed",ScenarioRunner.DefaultSeed.ToString()));
            speed=float.Parse(Value("--speed","1"),System.Globalization.CultureInfo.InvariantCulture);if(speed<=0||speed>20)throw new ArgumentOutOfRangeException("speed");
            loopCount=int.Parse(Value("--loops","1"));if(loopCount<1||loopCount>20)throw new ArgumentOutOfRangeException("loops");
            targetFps=int.Parse(Value("--target-fps","30"));if(targetFps<1||targetFps>240)throw new ArgumentOutOfRangeException("target-fps");
            requestedWidth=int.Parse(Value("-screen-width",Screen.width.ToString()));
            requestedHeight=int.Parse(Value("-screen-height",Screen.height.ToString()));
            outputRoot=Value("--evidence",Path.Combine(Application.persistentDataPath,"evidence"));
            recordFrames=args.Contains("--capture-video");exitAtEnd=args.Contains("--exit-after-run");helpers=!args.Contains("--helpers-off");
            startPaused=args.Contains("--start-paused");
            diagnosticComparison=args.Contains("--shape-comparison");
            diagnosticLighting=args.Contains("--lighting-audit");diagnosticGpu=args.Contains("--gpu-ablation");
            capturePasses=args.Contains("--capture-passes");noScreenshots=args.Contains("--benchmark-no-captures");
            transitionAllowed=!args.Contains("--transition-off");
            if(noScreenshots&&(recordFrames||capturePasses))throw new ArgumentException("Benchmark cannot enable recording or pass readback.");
        }
        void MakeMaterials()
        {
            foreach(string name in new[]{"Pawn","Lance","Knight","Silver","Gold","Bishop","Rook","KingA","KingB","Board"})
            {
                var material=new Material(rig.Materials.BrightWood){name="Fixed4D "+name+" label"};
                var texture=Resources.Load<Texture2D>("PieceTextures/"+name);
                if(texture==null)throw new InvalidOperationException("Missing piece texture: "+name);
                texture.wrapMode=TextureWrapMode.Clamp;texture.anisoLevel=8;
                material.SetTexture("_BaseColorMap",texture);material.SetColor("_BaseColor",Color.white);
                EnvironmentMaterials.ApplyPieceSurface(material,name);labels.Add(name,material);
            }
        }
        void RestartRun()
        {
            passCapture?.CancelPending("RestartRun");
            StopAllCoroutines();tx?.Cancel();evidence?.Dispose();foreach(var p in pieces.Values)p.Dispose();pieces.Clear();
            if(endingGlowObject!=null){Destroy(endingGlowObject);endingGlowObject=null;}
            transition?.Set(0,0,0);transitionPhase=0;endingGlowLumens=0;
            movingId=null;angle=0;elapsed=0;frameIndex=0;lastCapture=-1;scene=1;failed="";paused=startPaused&&runIndex==0;exitDialog=false;captureFinished=false;
            records.Clear();guideCells.Clear();previousGuideAngle=double.NaN;
            runner=Bootstrap.CreateScenario(occlusion,seed);tx=new MoveTransaction(runner.Core,runner.Core.InitialState());
            initialState=tx.State;
            if(mode=="VerifiedReplay")
            {
                var fixture=Resources.Load<TextAsset>("verified-replay");if(fixture==null)throw new InvalidOperationException("Verified replay fixture missing.");
                replay=JsonUtility.FromJson<ReplayFixture>(fixture.text);
                if(replay.schema!="showcase-replay/1"||replay.ruleset!=Ruleset.Version||replay.seed!=seed||replay.occlusion!=tx.State.Rules.OcclusionId||replay.initialHash!=tx.State.Hash)
                    throw new InvalidOperationException("Verified replay configuration/hash mismatch; no fallback.");
                ReplayPrevalidator.Validate(replay,runner);
            }
            foreach(var p in tx.State.Pieces)
            {
                float size=p.Kind==PieceKind.King?1.20f:p.Kind==PieceKind.Rook||p.Kind==PieceKind.Bishop?1.12f:p.Kind==PieceKind.Gold||p.Kind==PieceKind.Silver?1.06f:1f;
                string label=p.Kind==PieceKind.King?"King"+p.Side:p.Kind.ToString();
                pieces.Add(p.Id,new SectionView(p.Id,ShapeFactory.CreatePiece(variant,size),labels[label],rig.Materials.BrightWood,false,size,variant==ShapeVariant.C));
            }
            string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+profile+"-"+(++runIndex);
            var stampAsset=Resources.Load<TextAsset>("showcase-build");
            var stamp=stampAsset==null?new BuildStamp():JsonUtility.FromJson<BuildStamp>(stampAsset.text);
            evidence=new RuntimeEvidence(Path.Combine(outputRoot,id),new RunHeader{runId=id,startedUtc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,
                device=SystemInfo.graphicsDeviceName,driver=SystemInfo.graphicsDeviceVersion,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),profile=profile,mode=mode,occlusion=tx.State.Rules.OcclusionId,
                requestedWidth=requestedWidth,requestedHeight=requestedHeight,
                requestedRenderWidth=renderOutput.RequestedWidth,requestedRenderHeight=renderOutput.RequestedHeight,renderingSurface=renderOutput.Surface,
                shape="geometry/0.1 "+variant,material=(variant==ShapeVariant.C?"fixed-affine-f-uplusw/1":"fixed-extruded-f-u/1")+" | "+EnvironmentMaterials.Version,width=Screen.width,height=Screen.height,graphicsMemoryMB=SystemInfo.graphicsMemorySize,seed=seed,
                buildSource=Application.buildGUID+" / uncommitted-local-source; see build manifest",sourceManifestSha256=stamp.sourceManifestSha256,sourceHash=stamp.sourceHash,buildUtc=stamp.builtUtc,
                quality=QualitySettings.names[QualitySettings.GetQualityLevel()]+" / HDRP photographic / PCSS",cpuModel=SystemInfo.processorType,
                targetFrameRate=targetFps,expectedPlies=ScenarioRunner.ExpectedPlies,scenarioVersion=ScenarioRunner.Version,transitionEffect=profile=="ShowcaseRaster"&&transitionAllowed?"time-suspension-current-frame/1":"off",
                lightingVersion=EnvironmentDebug.Version,initialLightingEffects=rig.Debug.Summary,
                captureMode=noScreenshots?"none / clean benchmark":recordFrames?"sampled video frames":capturePasses?"GPU pass readback and screenshots":"screenshots"});
            if(recordFrames)Directory.CreateDirectory(Path.Combine(evidence.DirectoryPath,"video-frames"));
            SetCameraGoal();rig.Camera.transform.position=cameraGoal;rig.Camera.transform.LookAt(lookGoal);
            Event("RunStarted",mode+"; all rules fixed; official 40-piece initial state");RefreshGeometry();
            if(paused)Event("StartupPaused","Waiting for Space before advancing the first scene; R explicitly restarts playback.");
            StartCoroutine(Guarded(Sequence()));if(recordFrames)StartCoroutine(CaptureFrames());
        }
        IEnumerator Sequence()
        {
            if(diagnosticComparison){yield return CompareShapes();yield break;}
            if(diagnosticLighting){yield return CompareLighting();yield break;}
            if(diagnosticGpu){yield return AblateGpu();yield break;}
            yield return Hold(2);Screenshot("scene01-wide");
            cameraGoal=new Vector3(.32f,1.10f,-.40f);lookGoal=new Vector3(0,.87f,-.03f);
            yield return Hold(4);Screenshot("scene01-close");SetCameraGoal();yield return Hold(2);
            while(tx.State.Outcome==GameOutcome.Ongoing&&failed.Length==0)
            {
                int ply=tx.State.Ply+1;scene=SceneNumber(ply);SetCameraGoal();
                var before=tx.State;
                Task<CpuDecision> thinking=Task.Run(()=>ChooseDecision(before));
                while(!thinking.IsCompleted)yield return null;
                if(thinking.IsFaulted){Fail(thinking.Exception);yield break;}
                var decision=thinking.Result;
                File.WriteAllText(Path.Combine(evidence.DirectoryPath,"cpu-"+ply.ToString("D2")+".json"),JsonUtility.ToJson(CpuEvidence.From(ply,decision),true));
                try{tx.Reserve(decision.Move);}
                catch(Exception e){Fail(e);yield break;}
                Event("MoveReserved",decision.Move.ToString());
                bool dimensionalTurn=ScenarioRunner.Requires4DViewForPly(ply);
                if(before.Turn==Side.B&&Math.Abs(angle)>1e-9){Fail(new InvalidOperationException("Every B reply must occur in the original H0 view."));yield break;}
                if(dimensionalTurn)
                {
                    yield return Rotate(90,ply==ScenarioRunner.First4DPly?10:7.5);
                    yield return Hold(1.2);
                    if(ply==ScenarioRunner.First4DPly)Screenshot("scene04-h90");
                    if(ply==ScenarioRunner.ExpectedPlies)Screenshot("scene05-before-capture");
                    yield return new WaitForEndOfFrame();
                }
                UpdateGuideCells(before,decision.Move);
                movingId=decision.Move.PieceId;VecN from=ToVec(decision.Move.From),to=ToVec(decision.Move.To);
                Event("MoveAnimationStarted",decision.Move.ToString());
                double t=0;double duration=ScenarioRunner.IsHeightEscapePly(ply)||dimensionalTurn?2.8:1.7;
                while(t<duration)
                {
                    if(!paused){t+=Time.unscaledDeltaTime*speed;double k=Smooth(Math.Min(1,t/duration));movingPosition=from+(to-from)*k;RefreshGeometry();}
                    yield return null;
                }
                while(paused)yield return null;
                try
                {
                    var after=tx.Commit();var record=runner.RecordCommitted(before,after,decision);records.Add(record);
                    if(mode=="VerifiedReplay"&&after.Hash!=replay.records[after.Ply-1].afterHash)throw new InvalidOperationException("Replay after hash mismatch.");
                    foreach(var id in pieces.Keys.ToArray())if(after.Find(id)==null){pieces[id].Dispose();pieces.Remove(id);}
                    movingId=null;RefreshGeometry();Event("MoveCommitted",decision.Move.ToString());
                    File.WriteAllText(Path.Combine(evidence.DirectoryPath,"state-"+after.Ply.ToString("D2")+".txt"),after.Hash+"\n"+string.Join("\n",after.Pieces.Select(p=>p.Id+" "+p.Position)));
                }
                catch(Exception e){Fail(e);yield break;}
                if(ply==4)Screenshot("scene01-opening");
                if(ply==ScenarioRunner.First3DPly)Screenshot("scene02-height");
                if(ScenarioRunner.IsHeightEscapePly(ply)||ScenarioRunner.IsHeightReplyPly(ply))
                    Event("ThreeDExchange","Actual v-axis escape/reply; ply="+ply);
                if(ply==ScenarioRunner.First4DPly-1){Screenshot("scene03-pressure");yield return Hold(3);}
                if(dimensionalTurn)
                {
                    yield return Hold(1.1);yield return Rotate(0,7.5);guideCells.Clear();RefreshGuides();
                    Event("ReturnedH0","Hero remains in authoritative state; physical section="+pieces["A-Pawn-0"].Last.Status);
                    if(ply==ScenarioRunner.First4DPly){yield return Hold(3);Screenshot("scene04-absence");}
                    else yield return Hold(1.0);
                }
                yield return Hold(.7);
            }
            if(failed.Length>0)yield break;
            try
            {
                var result=new ScenarioResult(initialState,tx.State,records,mode=="DirectedLive"?ExecutionMode.DirectedLive:ExecutionMode.VerifiedReplay,seed);
                IndependentRuleVerifier.Verify(result);
                if(mode=="VerifiedReplay"&&result.TranscriptHash!=replay.transcriptHash)throw new InvalidOperationException("Replay transcript mismatch.");
                File.WriteAllText(Path.Combine(evidence.DirectoryPath,"transcript.json"),JsonUtility.ToJson(ReplayFixture.From(result),true));
                Event("IndependentVerificationPassed",result.TranscriptHash);
            }
            catch(Exception e){Fail(e);yield break;}
            Event("GameEnded",tx.State.Outcome+"; winner="+tx.State.Winner+"; commits="+tx.CommitCount);
            StartCoroutine(EndingGlow());yield return Hold(.35);Screenshot("scene05-ending");
            yield return Hold(7);while(passCapture!=null&&passCapture.IsBusy)yield return null;captureFinished=true;evidence.Dispose();
            if(runIndex<loopCount){RestartRun();yield break;}
            if(exitAtEnd){quitting=true;Application.Quit(AuditExitCode);}
        }
        CpuDecision ChooseDecision(GameState state)
        {
            if(mode=="DirectedLive")return runner.DecideNext(state);
            if(state.Ply>=replay.records.Length)throw new InvalidOperationException("Replay exhausted.");
            var next=replay.records[state.Ply];
            if(next.ply!=state.Ply+1||next.beforeHash!=state.Hash)throw new InvalidOperationException("Replay before hash mismatch.");
            if(!runner.Core.IsLegal(state,next.Move))throw new InvalidOperationException("Illegal replay move.");
            return new CpuDecision("VerifiedReplay/1",next.Move,state.Turn==Side.A?state.Hash:ObservationFactory.ForB(state).Hash,Array.Empty<CandidateScore>(),seed,0,0,"Stored independently verified move; each transition revalidated. No live search.");
        }
        IEnumerator EndingGlow()
        {
            var go=new GameObject("Confirmed king-capture light");endingGlowObject=go;go.transform.position=SectionView.Anchor+new Vector3(0,.065f,.20f);
            var light=go.AddComponent<Light>();light.type=LightType.Point;light.range=.45f;light.color=new Color(1,.73f,.30f);light.shadows=LightShadows.None;
            var hd=go.AddComponent<HDAdditionalLightData>();double t=0;
            while(t<1.8){if(!paused)t+=Time.unscaledDeltaTime*speed;endingGlowLumens=(float)(55*Math.Sin(Math.PI*Math.Min(1,t/1.8)));hd.SetIntensity(endingGlowLumens,LightUnit.Lumen);yield return null;}
            endingGlowLumens=0;Destroy(go);endingGlowObject=null;
        }
        IEnumerator CompareShapes()
        {
            // A separate diagnostic run; no game moves are committed.
            foreach(var p in pieces.Values)p.Dispose();pieces.Clear();
            foreach(var item in new[]{ShapeVariant.A,ShapeVariant.C})
            {
                string id="comparison-"+item;var v=new SectionView(id,ShapeFactory.CreatePawn(item),labels["Pawn"],rig.Materials.BrightWood,false,1,item==ShapeVariant.C);pieces.Add(id,v);
            }
            foreach(double a in new[]{0d,30d,45d,60d,90d})
            {
                angle=a;pieces["comparison-A"].UpdateSection(new VecN(-1.3,0,0,0),1,a,++sectionVersion);
                pieces["comparison-C"].UpdateSection(new VecN(1.3,0,0,0),1,a,sectionVersion);
                board.UpdateSection(default,1,a,sectionVersion);
                rig.Camera.transform.position=new Vector3(.25f,1.14f,-.32f);rig.Camera.transform.LookAt(new Vector3(0,.87f,0));
                yield return Hold(3);Screenshot("shape-AC-"+a);yield return new WaitForEndOfFrame();while(passCapture!=null&&passCapture.IsBusy)yield return null;
            }
            captureFinished=true;evidence.Dispose();if(exitAtEnd)Application.Quit(AuditExitCode);
        }
        IEnumerator Hold(double seconds)
        {double t=0;while(t<seconds){if(!paused)t+=Time.unscaledDeltaTime*speed;yield return null;}}
        IEnumerator CompareLighting()
        {
            // Same camera, geometry and frozen ambient motion for every A/B lighting view.
            rig.Camera.transform.position=new Vector3(.32f,1.10f,-.40f);rig.Camera.transform.LookAt(new Vector3(0,.87f,-.03f));
            string hash=tx.State.Hash;yield return Hold(3);
            yield return LightingShot("lighting-all-on");
            foreach(EnvironmentEffect effect in Enum.GetValues(typeof(EnvironmentEffect)))
            {
                bool before=rig.Debug.GetEffect(effect);rig.Debug.SetEffect(effect,false);
                yield return LightingShot("lighting-off-"+effect);
                rig.Debug.SetEffect(effect,before);
            }
            foreach(var mode in new[]{EnvironmentDiagnostic.AmbientOcclusion,EnvironmentDiagnostic.ContactShadows})
            {rig.Debug.SetDiagnostic(mode);yield return LightingShot("lighting-only-"+mode);}
            rig.Debug.SetDiagnostic(EnvironmentDiagnostic.None);
            try
            {
                foreach(var isolation in new[]{ShadowIsolation.NoEnvironmentCasters,ShadowIsolation.NoSectionCasters,ShadowIsolation.NoCasters,ShadowIsolation.HighBias})
                {
                    rig.Debug.SetShadowIsolation(isolation);
                    yield return LightingShot("lighting-shadow-isolation-"+isolation);
                    rig.Debug.RequestShadowMetadata(Path.Combine(evidence.DirectoryPath,"lighting-shadow-isolation-"+isolation+"-gpu.json"));
                    while(rig.Debug.IsShadowReadbackBusy)yield return null;
                    rig.Debug.SetShadowIsolation(ShadowIsolation.Normal);
                }
            }
            finally {rig.Debug.SetShadowIsolation(ShadowIsolation.Normal);}
            rig.Debug.RequestShadowMetadata(Path.Combine(evidence.DirectoryPath,"lighting-shadow-restored-gpu.json"));
            while(rig.Debug.IsShadowReadbackBusy)yield return null;
            transition.Set(.82f,.32f,.5f);yield return LightingShot("lighting-transition-on");transition.Set(0,0,0);
            yield return LightingShot("lighting-restored");
            if(tx.State.Hash!=hash||tx.State.Ply!=0||tx.CommitCount!=0){Fail(new InvalidOperationException("Lighting diagnostics changed the game state."));yield break;}
            Event("LightingAuditComplete","All shared effect APIs exercised; fixed camera/state; actual output images require pixel and visual review.");
            captureFinished=true;evidence.Dispose();if(exitAtEnd)Application.Quit(AuditExitCode);
        }
        IEnumerator AblateGpu()
        {
            // Same fixed camera and state as the lighting audit; only render settings vary.
            rig.Camera.transform.position=new Vector3(.32f,1.10f,-.40f);rig.Camera.transform.LookAt(new Vector3(0,.87f,-.03f));
            string hash=tx.State.Hash;yield return Hold(2);
            yield return GpuAblation.Run(rig,evidence,transition,Path.Combine(evidence.DirectoryPath,"gpu-ablation.json"),message=>Event("GpuAblation",message));
            if(tx.State.Hash!=hash||tx.State.Ply!=0){Fail(new InvalidOperationException("GPU ablation changed the game state."));yield break;}
            captureFinished=true;evidence.Dispose();if(exitAtEnd)Application.Quit(AuditExitCode);
        }
        IEnumerator LightingShot(string name)
        {
            yield return Hold(.7);Screenshot(name);yield return new WaitForEndOfFrame();
            while(passCapture!=null&&passCapture.IsBusy)yield return null;
        }
        IEnumerator Rotate(double target,double seconds)
        {
            double from=angle,t=0;string hash=tx.State.Hash;Event("SliceRotationStarted",from+" -> "+target);
            bool entering=target>from;
            if(entering){transitionPhase=0;yield return SetTransitionStrength(.82f,.9);}
            while(t<seconds)
            {
                if(!paused)
                {
                    t+=Time.unscaledDeltaTime*speed;double k=Math.Min(t/seconds,1);
                    double eased=k*k*k*(10+k*(-15+6*k));eased-=.055*Math.Sin(2*Math.PI*eased);
                    angle=from+(target-from)*eased;
                    transitionPhase=Mathf.Min(1f,transitionPhase+Time.unscaledDeltaTime*speed/3f);
                    transition.Set(.82f,transitionPhase,(float)(angle/90));RefreshGeometry();
                }
                yield return null;
            }
            angle=target;RefreshGeometry();if(hash!=tx.State.Hash){Fail(new Exception("Slice rotation modified GameState"));yield break;}
            Event("SliceRotationComplete","State hash unchanged");
            if(!entering)yield return SetTransitionStrength(0,.9);
            else transition.Set(.62f,1,1);
        }
        IEnumerator SetTransitionStrength(float target,double duration)
        {
            float from=transition.Strength;double t=0;
            while(t<duration){if(!paused){t+=Time.unscaledDeltaTime*speed;transition.Set(Mathf.Lerp(from,target,(float)Smooth(Math.Min(1,t/duration))),transitionPhase,(float)(angle/90));}yield return null;}
            transition.Set(target,transitionPhase,(float)(angle/90));
        }
        void Update()
        {
            if(tx==null)return;
            if(Input.GetKeyDown(KeyCode.F1))CommandToggleDebug();
            if(Input.GetKeyDown(KeyCode.T))CommandToggleTransition();
            if(Input.GetKeyDown(KeyCode.Space))CommandTogglePause();
            if(Input.GetKeyDown(KeyCode.H)){helpers=!helpers;RefreshGuides();Event("HelpersToggle",helpers.ToString());}
            if(Input.GetKeyDown(KeyCode.R)){CommandRestart();return;}
            if(Input.GetKeyDown(KeyCode.F9))Screenshot("manual");
            if(Input.GetKeyDown(KeyCode.Escape))CommandExitToggle();
            if(Input.GetKeyDown(KeyCode.Return))CommandExitConfirm();
            rig.Debug?.FreezeAmbientMotion(paused||transition.Strength>0||diagnosticLighting||diagnosticGpu);
            transition.Suppressed=rig.Debug!=null&&rig.Debug.IsDiagnosticActive;
            if(!paused)
            {
                elapsed+=Time.unscaledDeltaTime*speed;
                if(!diagnosticComparison&&!diagnosticLighting&&!diagnosticGpu){rig.Camera.transform.position=Vector3.Lerp(rig.Camera.transform.position,cameraGoal,Time.unscaledDeltaTime*.8f*speed);var rot=Quaternion.LookRotation(lookGoal-rig.Camera.transform.position);rig.Camera.transform.rotation=Quaternion.Slerp(rig.Camera.transform.rotation,rot,Time.unscaledDeltaTime*.8f*speed);}
            }
        }
        void LateUpdate()
        {
            if(evidence==null||tx==null)return;
            if(failed.Length==0&&passCapture!=null&&passCapture.LastError.Length>0){Fail(new InvalidOperationException(passCapture.LastError));return;}
            if(visibleVersion!=sectionVersion||visiblePieceSet!=pieces.Count)
            {
                // Visibility only changes when a section is rebuilt; rebuild the id list then, not every frame.
                visibleVersion=sectionVersion;visiblePieceSet=pieces.Count;visibleCount=0;visibleBuilder.Clear();
                foreach(var pair in pieces)
                {
                    if(pair.Value.Last?.Status!=SectionStatus.Volume3D)continue;
                    if(visibleCount++>0)visibleBuilder.Append(',');visibleBuilder.Append(pair.Key);
                }
                visibleIds=visibleBuilder.ToString();
            }
            evidence.Frame(new FrameEvidence{frame=Time.frameCount,ply=tx.State.Ply,scene=scene,elapsed=elapsed,angle=angle,frameMs=Time.unscaledDeltaTime*1000,sectionMs=sectionMs,
                visiblePieces=visibleCount,stateHash=tx.State.Hash,
                transitionStrength=transition.Strength,transitionEnabled=transition.EffectEnabled,visibleIds=visibleIds,
                outputWidth=Screen.width,outputHeight=Screen.height,internalWidth=rig.Camera.scaledPixelWidth,internalHeight=rig.Camera.scaledPixelHeight,
                renderedInternalWidth=renderOutput.RenderedWidth,renderedInternalHeight=renderOutput.RenderedHeight,renderedCameraFrame=renderOutput.RenderedFrame,
                endingGlowLumens=endingGlowLumens});
        }
        void RefreshGeometry()
        {
            watch.Restart();sectionVersion++;sectionJobs.Clear();
            void Queue(SectionView view,VecN q,int forward)
            {if(view.PrepareSection(q,forward,angle,sectionVersion))sectionJobs.Add(new SectionJob{view=view,q=q,forward=forward});}
            Queue(board,default,1);
            foreach(var p in tx.State.Pieces)if(pieces.TryGetValue(p.Id,out var view))Queue(view,p.Id==movingId?movingPosition:ToVec(p.Position),p.Side==Side.A?1:-1);
            try
            {
                // Keep the same section math and version for every pass. Join all workers
                // before any renderer can consume the frame; Unity APIs run only below.
                if(sectionJobs.Count>3)
                {
                    var jobs=sectionJobs.ToArray();double frameAngle=angle;
                    Parallel.For(0,jobs.Length,sectionWorkers,i=>{var job=jobs[i];job.result=job.view.ComputeSection(job.q,job.forward,frameAngle);jobs[i]=job;});
                    foreach(var job in jobs)job.view.ApplySection(job.result);
                }
                else foreach(var job in sectionJobs)job.view.ApplySection(job.view.ComputeSection(job.q,job.forward,angle));
                RefreshGuides();
            }
            catch(Exception e){Fail(e);}
            watch.Stop();sectionMs=watch.Elapsed.TotalMilliseconds;
        }
        void UpdateGuideCells(GameState state,Move selected)
        {
            guideCells.Clear();if(scene<2)return;
            // Logical candidates come from RuleCore; each displayed cell is separately intersected in 4D.
            if(selected.PieceId=="A-Pawn-0")
                foreach(var m in runner.Core.LegalMoves(state).Where(m=>m.PieceId==selected.PieceId))guideCells.Add(ToVec(m.To));
            guideCells.Add(ToVec(selected.From));guideCells.Add(ToVec(selected.To));previousGuideAngle=double.NaN;
        }
        void RefreshGuides()
        {guides.Visible=helpers&&scene>=2&&guideCells.Count>0;if(previousGuideAngle!=angle){guides.Update(guideCells,angle,scene>=4);previousGuideAngle=angle;}}
        void SetCameraGoal()
        {
            cameraGoal=scene==1?new Vector3(.49f,1.25f,-.66f):new Vector3(.51f,1.31f,-.68f);
            lookGoal=new Vector3(0,.875f+(scene>=2?.025f:0),.015f);
            if(scene==5){cameraGoal=new Vector3(.40f,1.22f,-.57f);lookGoal=new Vector3(0,.89f,.055f);}
        }
        static int SceneNumber(int ply)=>ply<ScenarioRunner.First3DPly?1:ply==ScenarioRunner.First3DPly?2:ply<ScenarioRunner.First4DPly?3:ply<=ScenarioRunner.First4DPly+1?4:5;
        static double Smooth(double x)=>x*x*(3-2*x);
        static VecN ToVec(QN q)=>new VecN(q.F,q.U,q.V,q.W);
        public void CommandTogglePause(){if(exitDialog)return;paused=!paused;Event("Pause",paused.ToString());}
        public void CommandToggleDebug(){debug=!debug;Event("DebugToggle","No timeline or camera change");}
        public void CommandToggleTransition(){transitionAllowed=!transitionAllowed;transition.EffectEnabled=profile=="ShowcaseRaster"&&transitionAllowed;Event("TransitionToggle",transition.EffectEnabled.ToString());}
        public void CommandRestart(){try{RestartRun();}catch(Exception e){Fail(e);}}
        public void CommandExitToggle(){if(exitDialog){exitDialog=false;paused=previousPause;}else{previousPause=paused;paused=true;exitDialog=true;}Event("ExitDialog",exitDialog.ToString());}
        // exitCode lets automation report a failed run to its launcher; the Enter key always exits with 0.
        public void CommandExitConfirm(int exitCode=0){if(!exitDialog)return;quitting=true;Event("ExitConfirmed","Shared Enter command; exitCode="+exitCode);evidence?.Dispose();Application.Quit(exitCode);}
        int AuditExitCode=>passCapture!=null&&passCapture.HasAuditFailures?2:0;
        // Runs a playback coroutine with nested enumerators flattened, so an exception at any depth
        // reaches Fail() (overlay and exit code 1) instead of silently stopping the coroutine.
        IEnumerator Guarded(IEnumerator body)
        {
            var stack=new Stack<IEnumerator>();stack.Push(body);
            while(stack.Count>0)
            {
                object next=null;bool moved;
                try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
                catch(Exception e){Fail(e);yield break;}
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator nested){stack.Push(nested);continue;}
                yield return next;
            }
        }
        public PlaybackSnapshot InspectPlayback()=>new PlaybackSnapshot{stateHash=tx?.State.Hash??"",failure=failed,evidenceDirectory=evidence?.DirectoryPath??"",
            ply=tx?.State.Ply??0,commitCount=tx?.CommitCount??0,runIndex=runIndex,scene=scene,pieceViews=pieces.Count,angle=angle,elapsed=elapsed,
            paused=paused,debug=debug,reserved=tx?.HasReservation??false,terminal=tx!=null&&tx.State.Outcome!=GameOutcome.Ongoing,exitDialog=exitDialog,
            transitionStrength=transition?.Strength??0,transitionPhase=transition?.Phase??0,
            cameraPosition=rig?.Camera.transform.position??Vector3.zero,cameraRotation=rig?.Camera.transform.rotation??Quaternion.identity};
        void Event(string kind,string message)=>evidence?.Event(new EventEvidence{kind=kind,message=message,ply=tx?.State.Ply??0,scene=scene,time=elapsed,angle=angle,stateHash=tx?.State.Hash??""});
        void Screenshot(string name)
        {try{if(evidence!=null&&!noScreenshots){string prefix=Path.Combine(evidence.DirectoryPath,name);ScreenCapture.CaptureScreenshot(prefix+".png");passCapture?.Capture(prefix);}}catch(Exception e){Fail(e);}}
        IEnumerator CaptureFrames()
        {
            while(!quitting&&!captureFinished)
            {
                yield return new WaitForEndOfFrame();
                if(captureFinished)yield break;
                if(evidence==null||paused||Time.unscaledTimeAsDouble-lastCapture<1.0/15)continue;
                lastCapture=Time.unscaledTimeAsDouble;
                if(captureTexture==null||captureTexture.width!=Screen.width||captureTexture.height!=Screen.height)
                {if(captureTexture!=null)Destroy(captureTexture);captureTexture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);}
                captureTexture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false);captureTexture.Apply(false);
                string frameName=(frameIndex++).ToString("D6")+".jpg";
                File.WriteAllBytes(Path.Combine(evidence.DirectoryPath,"video-frames",frameName),captureTexture.EncodeToJPG(88));
                File.AppendAllText(Path.Combine(evidence.DirectoryPath,"video-frames","timestamps.csv"),frameName+","+Time.unscaledTimeAsDouble.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\n");
            }
        }
        void Fail(Exception e){StopAllCoroutines();captureFinished=true;transition?.Set(0,0,0);endingGlowLumens=0;tx?.Cancel();movingId=null;failed=e?.ToString()??"Unknown error";Debug.LogError(failed);paused=true;if(endingGlowObject!=null){Destroy(endingGlowObject);endingGlowObject=null;}Event("ScenarioMismatch",failed);if(exitAtEnd){evidence?.Dispose();quitting=true;Application.Quit(1);}}
        void OnGUI()
        {
            renderOutput?.DrawPreview();
            if(overlay==null){overlay=new GUIStyle(GUI.skin.box){fontSize=16,alignment=TextAnchor.UpperLeft,padding=new RectOffset(16,16,12,12)};labelStyle=new GUIStyle(GUI.skin.label){fontSize=20,alignment=TextAnchor.MiddleCenter};}
            if(debug&&tx!=null)GUI.Box(new Rect(16,16,660,195),"SHOWCASE • "+mode+" / "+profile+"\nScene "+scene+"   ply "+tx.State.Ply+"   u–w "+angle.ToString("F1")+"°   "+variant+"\n"+tx.State.Rules.OcclusionId+"  /  "+tx.State.Hash.Substring(0,16)+"\nSpace pause · R restart · H guides · F9 capture · Esc exit\nF2–F8 lighting · Shift+F4/F5 diagnostics · T dimensional effect\n"+(paused?"PAUSED":"PLAYING")+"   section "+sectionMs.ToString("F2")+" ms  / target "+targetFps+"fps",overlay);
            if(exitDialog)GUI.Box(new Rect(Screen.width/2-240,Screen.height/2-55,480,110),"Exit Showcase?\nEnter: exit    Esc: return",labelStyle);
            if(failed.Length>0)GUI.Box(new Rect(30,Screen.height-160,Screen.width-60,130),"ScenarioMismatch — stopped\n"+failed,overlay);
        }
        void OnApplicationQuit(){quitting=true;tx?.Cancel();Event("ApplicationQuit","Pending reservation cancelled; committed state retained");evidence?.Dispose();}
        void OnDestroy(){evidence?.Dispose();if(endingGlowObject!=null)Destroy(endingGlowObject);foreach(var p in pieces.Values)p.Dispose();board?.Dispose();guides?.Dispose();}
    }
}
