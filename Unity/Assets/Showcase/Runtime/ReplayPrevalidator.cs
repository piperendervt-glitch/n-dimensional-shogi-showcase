using System;
using System.Collections.Generic;
using System.IO;
using NDShogi.Showcase.Core;

namespace Showcase.Unity
{
    /// <summary>Checks the complete stored fixture before playback; never runs a CPU.</summary>
    public static class ReplayPrevalidator
    {
        public static ScenarioResult Validate(ReplayFixture fixture, ScenarioRunner runner)
        {
            if(fixture==null||runner==null)throw new ArgumentNullException("Replay fixture and runner are required.");
            var initial=runner.Core.InitialState();
            if(fixture.schema!="showcase-replay/1"||fixture.ruleset!=Ruleset.Version||fixture.mode!="VerifiedReplay"||
                fixture.occlusion!=initial.Rules.OcclusionId||fixture.seed!=runner.Seed||fixture.initialHash!=initial.Hash)
                throw new InvalidDataException("VerifiedReplay header, mode, rules, seed or initial hash mismatch. No fallback.");
            if(fixture.records==null||fixture.records.Length!=ScenarioRunner.ExpectedPlies||string.IsNullOrEmpty(fixture.transcriptHash)||string.IsNullOrEmpty(fixture.finalHash))
                throw new InvalidDataException("VerifiedReplay fixture is incomplete.");
            var state=initial;var records=new List<PlyRecord>(fixture.records.Length);
            foreach(var stored in fixture.records)
            {
                if(stored==null||stored.from==null||stored.to==null||stored.from.Length!=4||stored.to.Length!=4||
                    stored.ply!=state.Ply+1||stored.beforeHash!=state.Hash)
                    throw new InvalidDataException("VerifiedReplay ordering, coordinate or before-hash mismatch.");
                var move=stored.Move;
                if(!runner.Core.IsLegal(state,move))throw new InvalidDataException("VerifiedReplay contains an illegal move.");
                var after=runner.Core.Apply(state,move);
                if(after.Hash!=stored.afterHash)throw new InvalidDataException("VerifiedReplay after-hash mismatch.");
                var decision=new CpuDecision("VerifiedReplay/1",move,state.Turn==Side.A?state.Hash:ObservationFactory.ForB(state).Hash,
                    Array.Empty<CandidateScore>(),runner.Seed,0,0,"Complete stored fixture prevalidated before playback; no CPU executed.");
                records.Add(runner.RecordCommitted(state,after,decision));state=after;
            }
            var result=new ScenarioResult(initial,state,records,ExecutionMode.VerifiedReplay,runner.Seed);
            IndependentRuleVerifier.Verify(result);
            if(result.TranscriptHash!=fixture.transcriptHash||result.FinalState.Hash!=fixture.finalHash)
                throw new InvalidDataException("VerifiedReplay final or transcript hash mismatch.");
            return result;
        }
    }
}
