using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using NDShogi.Showcase.Core;

namespace Showcase.Unity
{
    [Serializable]
    public sealed class PlaybackSnapshot
    {
        public string stateHash,failure,evidenceDirectory;
        public int ply,commitCount,runIndex,scene,pieceViews;
        public double angle,elapsed;
        public float transitionStrength,transitionPhase;
        public bool paused,debug,reserved,terminal,exitDialog;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
    }
    [Serializable]
    public sealed class PlaybackAutomationCheck
    {
        public string name,status,detail;
        public float realtimeSeconds;
        public PlaybackSnapshot snapshot;
    }
    [Serializable]
    public sealed class PlaybackAutomationReport
    {
        public string schema="showcase-playback-automation/1",status="RUNNING",startedUtc,finishedUtc,unity,device;
        public string mechanism="Standalone programmatic calls to the exact public command methods used by the controller's keyboard handlers.";
        public bool osKeyboardInjection=false,externalProcessExitVerified=false,exitConfirmationRequested;
        public string limitation="This does not test OS input focus or physical key delivery. Process exit is verified by the launching process.";
        public PlaybackAutomationCheck[] checks;
        public string[] completedRunEvidence;
    }

    /// <summary>Opt-in standalone acceptance harness. No commands run without --automation-playback.</summary>
    public sealed class PlaybackAutomation : MonoBehaviour
    {
        ShowcaseController controller;
        readonly List<PlaybackAutomationCheck> checks=new List<PlaybackAutomationCheck>();
        readonly List<string> completedEvidence=new List<string>();
        PlaybackAutomationReport report;
        string output;
        bool failed;
        float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachWhenRequested()
        {
            if(!System.Environment.GetCommandLineArgs().Contains("--automation-playback"))return;
            var go=new GameObject("Opt-in playback command automation");
            DontDestroyOnLoad(go);go.AddComponent<PlaybackAutomation>();
        }
        IEnumerator Start()
        {
            started=Time.realtimeSinceStartup;
            var args=System.Environment.GetCommandLineArgs();int pathIndex=Array.IndexOf(args,"--automation-evidence");
            output=pathIndex>=0&&pathIndex+1<args.Length?args[pathIndex+1]:Path.Combine(Application.persistentDataPath,"playback-automation");
            Directory.CreateDirectory(output);
            report=new PlaybackAutomationReport{startedUtc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName};
            Save();
            float deadline=Time.realtimeSinceStartup+120;
            while(controller==null&&Time.realtimeSinceStartup<deadline)
            {controller=FindFirstObjectByType<ShowcaseController>();yield return null;}
            if(controller==null){Record("controller-initialized",false,"Controller was not found within 120 seconds.",null);Finish();yield break;}
            yield return WaitFor(s=>!string.IsNullOrEmpty(s.stateHash),"controller-initialized",120);
            if(failed){Finish();yield break;}
            var initial=controller.InspectPlayback();string initialHash=initial.stateHash;
            Record("initial-state",initial.ply==0&&initial.commitCount==0&&initial.pieceViews==40,"Initial snapshot has 40 views and zero committed moves.",initial);
            if(args.Contains("--start-paused"))
            {
                Record("startup-paused",initial.paused&&initial.elapsed==0&&!initial.reserved,"Explicit startup pause waits before the first scene advances.",initial);
                yield return HoldAndCheckFrozen(initial,.4f,"startup-pause-stable");
                if(failed){Finish();yield break;}
                controller.CommandTogglePause();
                Record("startup-pause-resume",!controller.InspectPlayback().paused,"Shared Space command starts the paused startup.",controller.InspectPlayback());
            }

            yield return WaitFor(s=>s.reserved,"pending-move-observed",120);
            if(failed){Finish();yield break;}
            controller.CommandTogglePause();var paused=controller.InspectPlayback();
            Record("pause-command",paused.paused&&paused.reserved,"Same public method as Space; reservation remains uncommitted.",paused);
            yield return HoldAndCheckFrozen(paused,.4f,"paused-move-stable");
            var beforeDebug=controller.InspectPlayback();controller.CommandToggleDebug();var afterDebug=controller.InspectPlayback();
            Record("debug-toggle-no-jump",Frozen(beforeDebug,afterDebug)&&beforeDebug.debug!=afterDebug.debug,"Same public method as F1; camera, angle, time, state and commits unchanged.",afterDebug);
            controller.CommandToggleDebug();controller.CommandTogglePause();
            Record("resume-command",!controller.InspectPlayback().paused,"Pause resumes only through the shared user command.",controller.InspectPlayback());

            // Restart while the very same move is still reserved, before another frame can commit it.
            var beforeRestart=controller.InspectPlayback();controller.CommandRestart();var restarted=controller.InspectPlayback();
            Record("restart-cancels-reservation",beforeRestart.reserved&&restarted.runIndex==beforeRestart.runIndex+1&&restarted.ply==0&&
                restarted.commitCount==0&&!restarted.reserved&&restarted.stateHash==initialHash&&restarted.pieceViews==40,
                "Same public method as R; old reservation cancelled, initial state and 40 views rebuilt.",restarted);

            controller.CommandTogglePause();controller.CommandExitToggle();var exitPaused=controller.InspectPlayback();
            Record("exit-dialog-pauses",exitPaused.exitDialog&&exitPaused.paused,"Same public method as Esc.",exitPaused);
            controller.CommandExitToggle();var cancelledPaused=controller.InspectPlayback();
            Record("exit-cancel-restores-paused-state",!cancelledPaused.exitDialog&&cancelledPaused.paused,"Cancelling exit restores the pre-dialog paused state.",cancelledPaused);
            controller.CommandTogglePause();controller.CommandExitToggle();controller.CommandExitToggle();var cancelledPlaying=controller.InspectPlayback();
            Record("exit-cancel-restores-playing-state",!cancelledPlaying.exitDialog&&!cancelledPlaying.paused,"Cancelling exit restores the pre-dialog playing state.",cancelledPlaying);

            yield return WaitFor(s=>s.angle>3&&s.angle<87,"rotation-observed",180);
            if(failed){Finish();yield break;}
            controller.CommandTogglePause();var rotationPause=controller.InspectPlayback();
            yield return HoldAndCheckFrozen(rotationPause,.4f,"paused-rotation-stable");
            controller.CommandTogglePause();

            // --loops 3 provides two natural completed loops after the deliberate first-run restart.
            var terminalRuns=new HashSet<int>();string finalHash=null;int lastRun=controller.InspectPlayback().runIndex,lastPly=controller.InspectPlayback().ply;
            // Two natural v2 loops take roughly 570 seconds at the default speed.
            bool naturalLoopSeen=false;deadline=Time.realtimeSinceStartup+900;
            while(terminalRuns.Count<2&&Time.realtimeSinceStartup<deadline&&!failed)
            {
                var s=controller.InspectPlayback();
                if(!string.IsNullOrEmpty(s.failure)){Record("no-runtime-failure",false,s.failure,s);break;}
                if(s.commitCount!=s.ply){Record("one-commit-per-ply",false,"Commit count differs from committed ply.",s);break;}
                if(s.runIndex==lastRun&&s.ply<lastPly){Record("monotonic-commits",false,"Ply decreased without a run reset.",s);break;}
                if(s.runIndex!=lastRun)
                {
                    naturalLoopSeen=terminalRuns.Contains(lastRun);
                    Record("automatic-loop-reset",naturalLoopSeen&&s.ply==0&&s.commitCount==0&&!s.reserved&&s.stateHash==initialHash&&s.pieceViews==40,
                        "Controller's automatic loop reset restored initial state; no harness restart was called here.",s);
                    lastRun=s.runIndex;
                }
                if(s.terminal&&terminalRuns.Add(s.runIndex))
                {
                    bool valid=s.ply==ScenarioRunner.ExpectedPlies&&s.commitCount==ScenarioRunner.ExpectedPlies&&s.pieceViews==ScenarioRunner.ExpectedRemainingPieceCount&&(finalHash==null||finalHash==s.stateHash);
                    Record("complete-loop-"+terminalRuns.Count,valid,"Exactly "+ScenarioRunner.ExpectedPlies+" commits, "+ScenarioRunner.ExpectedRemainingPieceCount+" surviving piece views, and identical terminal state across loops.",s);
                    finalHash=s.stateHash;completedEvidence.Add(s.evidenceDirectory);
                }
                lastPly=s.ply;yield return null;
            }
            Record("two-natural-completions",terminalRuns.Count==2&&naturalLoopSeen,
                "Requires --loops 3: one deliberate restart followed by two completed runs and an automatic reset.",controller.InspectPlayback());
            if(!failed)
            {
                // Terminal state commits before the presentation coroutine starts the glow.
                // Wait for the actual transient so this restart test cannot pass vacuously.
                float glowDeadline=Time.realtimeSinceStartup+25;
                while(GameObject.Find("Confirmed king-capture light")==null&&Time.realtimeSinceStartup<glowDeadline)yield return null;
                bool glowWasPresent=GameObject.Find("Confirmed king-capture light")!=null;
                Record("terminal-glow-observed",glowWasPresent,"Actual terminal light exists before pause and restart.",controller.InspectPlayback());
                if(!controller.InspectPlayback().paused)controller.CommandTogglePause();
                controller.CommandRestart();yield return null;var terminalRestart=controller.InspectPlayback();
                Record("terminal-restart-clears-transients",terminalRestart.ply==0&&terminalRestart.commitCount==0&&!terminalRestart.reserved&&
                    terminalRestart.stateHash==initialHash&&terminalRestart.pieceViews==40&&GameObject.Find("Confirmed king-capture light")==null,
                    "Restart during terminal pause clears pieces and transient capture light. Light present immediately before restart: "+glowWasPresent+".",terminalRestart);
                controller.CommandExitToggle();var exit=controller.InspectPlayback();
                Record("exit-confirmation-ready",exit.exitDialog&&exit.paused,"Final exit dialog opened through the same command as Esc.",exit);
            }
            Finish();
        }
        IEnumerator WaitFor(Func<PlaybackSnapshot,bool> condition,string name,float seconds)
        {
            float end=Time.realtimeSinceStartup+seconds;
            while(Time.realtimeSinceStartup<end)
            {
                var s=controller.InspectPlayback();
                if(!string.IsNullOrEmpty(s.failure)){Record(name,false,s.failure,s);yield break;}
                if(condition(s)){Record(name,true,"Condition reached.",s);yield break;}
                yield return null;
            }
            Record(name,false,"Timed out after "+seconds+" real seconds.",controller.InspectPlayback());
        }
        IEnumerator HoldAndCheckFrozen(PlaybackSnapshot before,float seconds,string name)
        {
            float end=Time.realtimeSinceStartup+seconds;bool stable=true;PlaybackSnapshot after=before;
            while(Time.realtimeSinceStartup<end){yield return null;after=controller.InspectPlayback();stable&=after.paused&&Frozen(before,after);}
            Record(name,stable,"Sampled every rendered frame for "+seconds+" real seconds: hash, ply, commits, angle, elapsed time, camera position and rotation.",after);
        }
        static bool Frozen(PlaybackSnapshot a,PlaybackSnapshot b)
        {
            return a.stateHash==b.stateHash&&a.ply==b.ply&&a.commitCount==b.commitCount&&a.runIndex==b.runIndex&&
                Math.Abs(a.angle-b.angle)<1e-9&&Math.Abs(a.elapsed-b.elapsed)<1e-9&&
                a.transitionStrength==b.transitionStrength&&a.transitionPhase==b.transitionPhase&&
                (a.cameraPosition-b.cameraPosition).sqrMagnitude<1e-12f&&Quaternion.Angle(a.cameraRotation,b.cameraRotation)<.001f;
        }
        void Record(string name,bool passed,string detail,PlaybackSnapshot snapshot)
        {
            checks.Add(new PlaybackAutomationCheck{name=name,status=passed?"PASS":"FAIL",detail=detail,realtimeSeconds=Time.realtimeSinceStartup-started,snapshot=snapshot});
            if(!passed)failed=true;Save();
        }
        void Save()
        {
            report.checks=checks.ToArray();report.completedRunEvidence=completedEvidence.ToArray();
            File.WriteAllText(Path.Combine(output,"playback-automation.json"),JsonUtility.ToJson(report,true));
        }
        void Finish()
        {
            report.status=failed?"FAIL":"PASS";report.finishedUtc=DateTime.UtcNow.ToString("O");
            bool exit=System.Environment.GetCommandLineArgs().Contains("--automation-exit");report.exitConfirmationRequested=exit;Save();
            if(exit&&controller!=null)
            {
                if(!controller.InspectPlayback().exitDialog)controller.CommandExitToggle();
                controller.CommandExitConfirm();
            }
        }
    }
}
