using System;
using System.Linq;
using System.IO;
using UnityEngine;
using NDShogi.Showcase.Core;

namespace Showcase.Unity
{
    [Serializable] public sealed class ReplayFixture
    {
        public string schema,ruleset,occlusion,mode,initialHash,transcriptHash,finalHash;
        public int seed;public ReplayPly[] records;
        public static ReplayFixture From(ScenarioResult result)=>new ReplayFixture{schema="showcase-replay/1",ruleset=Ruleset.Version,occlusion=result.InitialState.Rules.OcclusionId,
            mode=result.Mode.ToString(),seed=result.Seed,initialHash=result.InitialState.Hash,finalHash=result.FinalState.Hash,transcriptHash=result.TranscriptHash,
            records=result.Records.Select(r=>new ReplayPly{ply=r.Ply,pieceId=r.Move.PieceId,from=r.Move.From.ToArray(),to=r.Move.To.ToArray(),beforeHash=r.BeforeHash,afterHash=r.AfterHash}).ToArray()};
    }
    [Serializable] public sealed class ReplayPly
    {
        public int ply;public string pieceId,beforeHash,afterHash;public int[] from,to;
        public Move Move=>new Move(pieceId,new QN(from[0],from[1],from[2],from[3]),new QN(to[0],to[1],to[2],to[3]));
    }
    [Serializable] public sealed class CpuEvidence
    {
        public int ply,seed,nodeBudget,nodesVisited;public string cpu,observationHash,fingerprint,preference,selected;
        public bool nonPawnRescue;public ScoredMove[] candidates;
        public static CpuEvidence From(int ply,CpuDecision d)=>new CpuEvidence{ply=ply,seed=d.Seed,nodeBudget=d.NodeBudget,nodesVisited=d.NodesVisited,
            cpu=d.Cpu,observationHash=d.ObservationHash,fingerprint=d.Fingerprint,preference=d.PreferenceExplanation,selected=d.Move.Key,nonPawnRescue=d.NonPawnRescue,
            candidates=d.Candidates.Select(c=>new ScoredMove{move=c.Move.Key,tactical=c.TacticalScore,preference=c.PreferenceScore,total=c.Total,replyNodes=c.ReplyNodes}).ToArray()};
    }
    [Serializable] public sealed class ScoredMove {public string move;public int tactical,preference,total,replyNodes;}
}
