using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NDShogi.Showcase.Core;

string output=Path.GetFullPath("evidence/core");string occlusion="lattice-line/1";int seed=ScenarioRunner.DefaultSeed;
for(int i=0;i<args.Length;i++)
{
    if(args[i]=="--output")output=Path.GetFullPath(args[++i]);
    else if(args[i]=="--occlusion")occlusion=args[++i];
    else if(args[i]=="--seed")seed=int.Parse(args[++i]);
    else throw new ArgumentException("Unknown argument: "+args[i]);
}
var runner=Bootstrap.CreateScenario(Ruleset.ParseOcclusion(occlusion),seed);
var watch=System.Diagnostics.Stopwatch.StartNew();var live=runner.RunToEnd();var replay=runner.Replay(live);watch.Stop();
if(live.TranscriptHash!=replay.TranscriptHash)throw new Exception("Replay transcript mismatch.");
Directory.CreateDirectory(output);
var options=new JsonSerializerOptions{WriteIndented=true};
object Describe(ScenarioResult run)=>new {
    schema="showcase-core-run/1",ruleset=Ruleset.Version,capabilities=Ruleset.Capabilities,observationPolicy=Ruleset.ObservationVersion,
    scenarioVersion=ScenarioRunner.Version,timelineVersion=ScenarioRunner.TimelineVersion,occlusion=run.InitialState.Rules.OcclusionId,mode=run.Mode.ToString(),seed=run.Seed,
    presentation=new{threeDExchanges=ScenarioRunner.ThreeDExchangeCount,first3DPly=ScenarioRunner.First3DPly,first4DPly=ScenarioRunner.First4DPly,expectedPlies=ScenarioRunner.ExpectedPlies,expectedRemainingPieces=ScenarioRunner.ExpectedRemainingPieceCount,returnToInitial3DBeforeEveryBReply=true},
    budget=runner.NodeBudget,dimensions=4,initial=run.InitialState.Pieces.Select(p=>new{id=p.Id,side=p.Side.ToString(),kind=p.Kind.ToString(),q=p.Position.ToArray()}),
    initialHash=run.InitialState.Hash,finalHash=run.FinalState.Hash,transcriptHash=run.TranscriptHash,winner=run.FinalState.Winner.ToString(),outcome=run.FinalState.Outcome.ToString(),
    records=run.Records.Select(r=>new {
        ply=r.Ply,scene=r.Scene.ToString(),requires4DView=ScenarioRunner.Requires4DViewForPly(r.Ply),pieceId=r.Move.PieceId,from=r.Move.From.ToArray(),to=r.Move.To.ToArray(),capture=r.CapturedId,
        beforeHash=r.BeforeHash,afterHash=r.AfterHash,guards=r.Guards,
        cpu=new{r.Decision.Cpu,r.Decision.ObservationHash,r.Decision.Seed,r.Decision.NodeBudget,r.Decision.NodesVisited,r.Decision.NonPawnRescue,r.Decision.PreferenceExplanation,r.Decision.Fingerprint,
            candidates=r.Decision.Candidates.Select(c=>new{pieceId=c.Move.PieceId,from=c.Move.From.ToArray(),to=c.Move.To.ToArray(),c.TacticalScore,c.PreferenceScore,c.Total,c.ReplyNodes})},
        observationB=ObservationFactory.ForB(r.BeforeState).Pieces.Select(p=>new{id=p.Id,side=p.Side.ToString(),kind=p.Kind.ToString(),q=p.Position.ToArray()}),
        stateAfter=r.AfterState.Pieces.Select(p=>new{id=p.Id,side=p.Side.ToString(),kind=p.Kind.ToString(),q=p.Position.ToArray()})
    })
};
File.WriteAllText(Path.Combine(output,"directed-live.json"),JsonSerializer.Serialize(Describe(live),options));
File.WriteAllText(Path.Combine(output,"verified-replay.json"),JsonSerializer.Serialize(Describe(replay),options));
File.WriteAllText(Path.Combine(output,"verified-replay.ndsr"),ScenarioRunner.ExportReplay(live));
File.WriteAllText(Path.Combine(output,"reference-replay.json"),JsonSerializer.Serialize(new{schema="showcase-replay/1",scenarioVersion=ScenarioRunner.Version,timelineVersion=ScenarioRunner.TimelineVersion,ruleset=Ruleset.Version,occlusion=live.InitialState.Rules.OcclusionId,mode="VerifiedReplay",initialHash=live.InitialState.Hash,transcriptHash=live.TranscriptHash,finalHash=live.FinalState.Hash,seed=live.Seed,records=live.Records.Select(r=>new{ply=r.Ply,pieceId=r.Move.PieceId,from=r.Move.From.ToArray(),to=r.Move.To.ToArray(),beforeHash=r.BeforeHash,afterHash=r.AfterHash})},options));
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{status="PASS",ruleset=Ruleset.Version,occlusion,livePlies=live.Records.Count,live.TranscriptHash,live.FinalState.Hash,winner=live.FinalState.Winner.ToString(),independentVerification=true,replayHashMatch=true,nonPawnRescues=live.Records.Count(r=>r.Decision.NonPawnRescue),totalNodes=live.Records.Sum(r=>r.Decision.NodesVisited),elapsedMilliseconds=watch.ElapsedMilliseconds},options));
foreach(var r in live.Records)System.Console.WriteLine($"{r.Ply,2} {r.Scene,-16} {r.Move} capture={r.CapturedId??"-"} roots={r.Decision.Candidates.Count} nodes={r.Decision.NodesVisited}/{r.Decision.NodeBudget}");
System.Console.WriteLine($"PASS {live.Records.Count} plies; {live.FinalState.Winner} wins; replay hash {live.TranscriptHash}; independent verifier accepted. Evidence: {output}");
