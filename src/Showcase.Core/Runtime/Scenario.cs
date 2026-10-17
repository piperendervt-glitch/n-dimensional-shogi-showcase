using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NDShogi.Showcase.Core
{
    public enum ExecutionMode { DirectedLive, VerifiedReplay }
    public enum ShowcaseScene { Opening2D=1, Escape3D=2, Pressure3D=3, Rotate4D=4, Attack4DEnding=5 }
    public sealed class PlyRecord
    {
        public int Ply=>AfterState.Ply;
        public Move Move=>Decision.Move;
        public GameState BeforeState {get;}
        public GameState AfterState {get;}
        public string BeforeHash=>BeforeState.Hash;
        public string AfterHash=>AfterState.Hash;
        public ShowcaseScene Scene {get;}
        public CpuDecision Decision {get;}
        public string? CapturedId {get;}
        public IReadOnlyList<string> Guards {get;}
        public PlyRecord(GameState before,GameState after,CpuDecision decision,ShowcaseScene scene,IEnumerable<string> guards)
        {BeforeState=before;AfterState=after;Decision=decision;Scene=scene;CapturedId=before.At(decision.Move.To)?.Id;Guards=Array.AsReadOnly(guards.ToArray());}
    }
    public sealed class ScenarioResult
    {
        public GameState InitialState {get;}
        public GameState FinalState {get;}
        public IReadOnlyList<PlyRecord> Records {get;}
        public ExecutionMode Mode {get;}
        public int Seed {get;}
        public string TranscriptHash {get;}
        public ScenarioResult(GameState initial,GameState final,IEnumerable<PlyRecord> records,ExecutionMode mode,int seed)
        {InitialState=initial;FinalState=final;Records=Array.AsReadOnly(records.ToArray());Mode=mode;Seed=seed;TranscriptHash=StableHash.Of(string.Join(";",Records.Select(r=>r.Ply+":"+r.Move.Key+":"+r.BeforeHash+":"+r.AfterHash)));}
    }
    public sealed class ScenarioRunner
    {
        public const string Version="central-pawn-directed/2";
        public const string TimelineVersion="showcase-alternating-dimensions/0.2";
        public const int ThreeDExchangeCount=3;
        public const int First3DPly=5;
        public const int First4DPly=First3DPly+2*ThreeDExchangeCount;
        public const int ExpectedPlies=27;
        public const int ExpectedRemainingPieceCount=39;
        // Presentation-only metadata. Core legality/observation never read a display angle.
        // Each hidden A move is shown in the rotated section, then the display returns to H0
        // before the intervening B move. The final king capture is shown in H90, followed
        // by the same return to H0 before the terminal light.
        public static bool IsHeightEscapePly(int ply)=>ply>=First3DPly&&ply<First4DPly&&(ply&1)==1;
        public static bool IsHeightReplyPly(int ply)=>ply>First3DPly&&ply<First4DPly&&(ply&1)==0;
        public static bool Requires4DViewForPly(int ply)=>ply>=First4DPly&&ply<=ExpectedPlies&&(ply&1)==1;
        public const int DefaultSeed=20261015;
        public RuleCore Core {get;}
        public int Seed {get;}
        public int NodeBudget {get;}
        private readonly PawnFirst4D a=new PawnFirst4D();
        private readonly Tactical3D b;
        public ScenarioRunner(Ruleset? rules=null,int seed=DefaultSeed,int nodeBudget=5000)
        {Core=new RuleCore(rules);Seed=seed;NodeBudget=nodeBudget;b=new Tactical3D(Core.Rules.Occlusion);}
        public static ShowcaseScene SceneForPly(int ply)=>ply<First3DPly?ShowcaseScene.Opening2D:ply<First4DPly?(IsHeightEscapePly(ply)?ShowcaseScene.Escape3D:ShowcaseScene.Pressure3D):ply<=First4DPly+1?ShowcaseScene.Rotate4D:ShowcaseScene.Attack4DEnding;
        public CpuDecision DecideNext(GameState state)
        {
            if(state.Outcome!=GameOutcome.Ongoing)throw new InvalidOperationException("Scenario already ended.");
            if(state.Rules.Occlusion!=Core.Rules.Occlusion)throw new InvalidOperationException("Rules changed inside run.");
            CheckBefore(state);
            return state.Turn==Side.A?a.Decide(state,Core,Seed,NodeBudget):b.Decide(ObservationFactory.ForB(state),Seed,NodeBudget);
        }
        public PlyRecord RecordCommitted(GameState before,GameState after,CpuDecision decision)
        {
            if(!Core.IsLegal(before,decision.Move)||Core.Apply(before,decision.Move).Hash!=after.Hash)throw new InvalidOperationException("ScenarioMismatch: committed transition mismatch.");
            var guards=CheckAfter(after);
            return new PlyRecord(before,after,decision,SceneForPly(after.Ply),guards);
        }
        private void CheckBefore(GameState state)
        {
            var hero=state.Find(PawnFirst4D.HeroId)??throw new InvalidOperationException("ScenarioMismatch: same hero must survive.");
            if(state.Ply>=First3DPly-1&&state.Ply<First4DPly&&(state.Ply&1)==0)
            {
                if(Core.Attackers(state,hero.Position,Side.B).Count==0)throw new InvalidOperationException("ScenarioMismatch: required real pressure absent.");
                var escape=hero.Position+new QN(0,0,state.Ply<First4DPly-1?1:0,state.Ply==First4DPly-1?1:0);
                if(!Core.IsLegal(state,new Move(hero.Id,hero.Position,escape)))throw new InvalidOperationException("ScenarioMismatch: escape unavailable.");
            }
            if(state.Ply>=ExpectedPlies&&state.Outcome==GameOutcome.Ongoing)throw new InvalidOperationException("ScenarioLimit: expected ending exceeded.");
        }
        private IReadOnlyList<string> CheckAfter(GameState state)
        {
            var guards=new List<string>{"fixed-rules","alternating-turn","same-hero"};
            var hero=state.Find(PawnFirst4D.HeroId)??throw new InvalidOperationException("ScenarioMismatch: hero captured.");
            if(state.Ply<=4&&state.Pieces.Any(p=>p.Position.V!=0||p.Position.W!=0))throw new InvalidOperationException("ScenarioMismatch: opening must be planar.");
            if(state.Ply>=First3DPly-1&&state.Ply<First4DPly&&(state.Ply&1)==0)
            {
                if(Core.Attackers(state,hero.Position,Side.B).Count==0)throw new InvalidOperationException("ScenarioMismatch: no real attack.");
                guards.Add("hero-under-authoritative-attack");
            }
            if(IsHeightEscapePly(state.Ply))
            {
                if(hero.Position.V!=(state.Ply-First3DPly)/2+1||hero.Position.W!=0)throw new InvalidOperationException("ScenarioMismatch: three ordinary 3D exchanges required before W escape.");
                guards.Add("three-dimensional-escape-"+hero.Position.V);
            }
            if(IsHeightReplyPly(state.Ply))
            {
                if(!state.Pieces.Any(p=>p.Side==Side.B&&p.Position.V==hero.Position.V))throw new InvalidOperationException("ScenarioMismatch: B did not respond at observed height.");
                guards.Add("B-observed-height-response");
            }
            if(state.Ply>=First4DPly&&state.Ply<ExpectedPlies)
            {
                if(hero.Position.W==0||ObservationFactory.ForB(state).Pieces.Any(p=>p.Id==hero.Id))throw new InvalidOperationException("ScenarioMismatch: hero not logically hidden.");
                guards.Add("hero-retained-in-core-and-absent-from-B-observation");
            }
            if(state.Ply==ExpectedPlies)
            {
                if(state.Outcome!=GameOutcome.KingCapture||state.Winner!=Side.A||hero.Position!=new QN(4,0,0,0))throw new InvalidOperationException("ScenarioMismatch: required same-pawn king capture absent.");
                guards.Add("same-pawn-captured-king");
            }
            return guards;
        }
        public ScenarioResult RunToEnd(int maximumPlies=64)
        {
            var initial=Core.InitialState();var tx=new MoveTransaction(Core,initial);var records=new List<PlyRecord>();
            while(tx.State.Outcome==GameOutcome.Ongoing)
            {
                if(tx.State.Ply>=maximumPlies)throw new InvalidOperationException("ScenarioLimit.");
                var before=tx.State;var decision=DecideNext(before);tx.Reserve(decision.Move);var after=tx.Commit();records.Add(RecordCommitted(before,after,decision));
            }
            if(tx.State.Ply!=ExpectedPlies||tx.State.Winner!=Side.A||tx.State.Pieces.Count!=ExpectedRemainingPieceCount)throw new InvalidOperationException("ScenarioMismatch: unexpected terminal state.");
            var result=new ScenarioResult(initial,tx.State,records,ExecutionMode.DirectedLive,Seed);IndependentRuleVerifier.Verify(result);return result;
        }
        public ScenarioResult Replay(ScenarioResult source)
        {
            IndependentRuleVerifier.Verify(source);
            var state=Core.InitialState();var initial=state;var records=new List<PlyRecord>();
            foreach(var expected in source.Records)
            {
                if(expected.BeforeHash!=state.Hash)throw new InvalidOperationException("Replay before hash mismatch.");CheckBefore(state);
                var decision=new CpuDecision("VerifiedReplay/1",expected.Move,state.Turn==Side.A?state.Hash:ObservationFactory.ForB(state).Hash,Array.Empty<CandidateScore>(),Seed,0,0,"Validated stored move; no live CPU search in this mode.");
                var after=Core.Apply(state,expected.Move);if(after.Hash!=expected.AfterHash)throw new InvalidOperationException("Replay after hash mismatch.");
                records.Add(RecordCommitted(state,after,decision));state=after;
            }
            var result=new ScenarioResult(initial,state,records,ExecutionMode.VerifiedReplay,Seed);IndependentRuleVerifier.Verify(result);return result;
        }
        public static string ExportReplay(ScenarioResult source)
        {
            IndependentRuleVerifier.Verify(source);
            var text=new StringBuilder();text.AppendLine("ND-SHOGI-REPLAY/1");text.AppendLine(Ruleset.Version);text.AppendLine(source.InitialState.Rules.OcclusionId);text.AppendLine(source.Seed.ToString(CultureInfo.InvariantCulture));text.AppendLine(source.TranscriptHash);
            foreach(var r in source.Records)text.AppendLine(r.Ply+"|"+r.Move.PieceId+"|"+r.Move.From+"|"+r.Move.To+"|"+r.BeforeHash+"|"+r.AfterHash);
            return text.ToString();
        }
        public ScenarioResult LoadReplay(string text)
        {
            var lines=text.Replace("\r","").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
            if(lines.Length<6||lines[0]!="ND-SHOGI-REPLAY/1"||lines[1]!=Ruleset.Version||lines[2]!=Core.Rules.OcclusionId||int.Parse(lines[3],CultureInfo.InvariantCulture)!=Seed)throw new InvalidOperationException("Replay header/config mismatch.");
            var state=Core.InitialState();var initial=state;var records=new List<PlyRecord>();
            foreach(var line in lines.Skip(5))
            {
                var fields=line.Split('|');
                if(fields.Length!=6||int.Parse(fields[0],CultureInfo.InvariantCulture)!=state.Ply+1||fields[4]!=state.Hash)throw new InvalidOperationException("Replay ordering/before hash mismatch.");
                var move=new Move(fields[1],ParseQ(fields[2]),ParseQ(fields[3]));CheckBefore(state);
                var decision=new CpuDecision("VerifiedReplay/1",move,state.Turn==Side.A?state.Hash:ObservationFactory.ForB(state).Hash,Array.Empty<CandidateScore>(),Seed,0,0,"Stored fixture; each move and state hash are revalidated.");
                var after=Core.Apply(state,move);if(after.Hash!=fields[5])throw new InvalidOperationException("Replay after hash mismatch.");records.Add(RecordCommitted(state,after,decision));state=after;
            }
            var result=new ScenarioResult(initial,state,records,ExecutionMode.VerifiedReplay,Seed);IndependentRuleVerifier.Verify(result);
            if(result.TranscriptHash!=lines[4])throw new InvalidOperationException("Replay transcript hash mismatch.");return result;
        }
        public CpuDecision DecideReplayNext(GameState state,ScenarioResult verifiedReplay)
        {
            if(verifiedReplay.Mode!=ExecutionMode.VerifiedReplay||state.Ply>=verifiedReplay.Records.Count)throw new InvalidOperationException("Expected verified replay source.");
            var record=verifiedReplay.Records[state.Ply];
            if(state.Hash!=record.BeforeHash||!Core.IsLegal(state,record.Move))throw new InvalidOperationException("Replay current state mismatch.");
            CheckBefore(state);return record.Decision;
        }
        private static QN ParseQ(string text)
        {
            var fields=text.Split(',');if(fields.Length!=4)throw new FormatException("Replay coordinate requires four integers.");
            return new QN(int.Parse(fields[0],CultureInfo.InvariantCulture),int.Parse(fields[1],CultureInfo.InvariantCulture),int.Parse(fields[2],CultureInfo.InvariantCulture),int.Parse(fields[3],CultureInfo.InvariantCulture));
        }
    }
    public static class Bootstrap
    {
        public static ScenarioRunner CreateScenario(OcclusionKind occlusion=OcclusionKind.LatticeLine,int seed=ScenarioRunner.DefaultSeed)=>new ScenarioRunner(new Ruleset(occlusion),seed);
    }
    // Independent transition checker. Does not call RuleCore, Movement, Occlusion or Apply.
    public static class IndependentRuleVerifier
    {
        public static void Verify(ScenarioResult result)
        {
            var initial=result.InitialState;
            if(initial.Pieces.Count!=40||initial.Ply!=0||initial.Turn!=Side.A||initial.Outcome!=GameOutcome.Ongoing||initial.Captured.Count!=0)throw new InvalidOperationException("Independent initial fixture rejected.");
            var back=new[]{PieceKind.Lance,PieceKind.Knight,PieceKind.Silver,PieceKind.Gold,PieceKind.King,PieceKind.Gold,PieceKind.Silver,PieceKind.Knight,PieceKind.Lance};
            foreach(Side side in Enum.GetValues(typeof(Side)))
            {
                int front=side==Side.A?1:-1;
                for(int u=-4;u<=4;u++)
                {
                    RequirePiece(initial,new QN(-4*front,u,0,0),side,back[u+4]);RequirePiece(initial,new QN(-2*front,u,0,0),side,PieceKind.Pawn);
                }
                RequirePiece(initial,new QN(-3*front,-3*front,0,0),side,PieceKind.Rook);RequirePiece(initial,new QN(-3*front,3*front,0,0),side,PieceKind.Bishop);
            }
            var positions=initial.Pieces.ToDictionary(p=>p.Id,p=>p);var captured=new HashSet<string>();Side turn=Side.A;Side? winner=null;int ply=0;var currentHash=initial.Hash;
            foreach(var r in result.Records)
            {
                if(winner.HasValue||r.Ply!=++ply||r.BeforeHash!=currentHash)throw new InvalidOperationException("Independent order/hash rejected.");
                if(r.BeforeState.Rules.Occlusion!=initial.Rules.Occlusion||r.AfterState.Rules.Occlusion!=initial.Rules.Occlusion)throw new InvalidOperationException("Independent rules change rejected.");
                if(!positions.TryGetValue(r.Move.PieceId,out var p)||p.Side!=turn||p.Position!=r.Move.From)throw new InvalidOperationException("Independent mover rejected.");
                CheckMove(positions.Values,p,r.Move.To,initial.Rules.Occlusion);
                var victim=positions.Values.FirstOrDefault(x=>x.Position==r.Move.To);
                if(victim!=null){positions.Remove(victim.Id);captured.Add(victim.Id);if(victim.Kind==PieceKind.King)winner=turn;}
                if(r.CapturedId!=victim?.Id)throw new InvalidOperationException("Independent capture record rejected.");
                positions[p.Id]=p.At(r.Move.To);turn=turn==Side.A?Side.B:Side.A;
                if(r.AfterState.Turn!=turn||r.AfterState.Winner!=winner||r.AfterState.Pieces.Count!=positions.Count||r.AfterState.Outcome!=(winner.HasValue?GameOutcome.KingCapture:GameOutcome.Ongoing))throw new InvalidOperationException("Independent transition rejected.");
                foreach(var actual in r.AfterState.Pieces)if(!positions.TryGetValue(actual.Id,out var expected)||actual.Position!=expected.Position||actual.Kind!=expected.Kind||actual.Side!=expected.Side)throw new InvalidOperationException("Independent piece mutation rejected.");
                if(!captured.SetEquals(r.AfterState.Captured.Select(x=>x.Id)))throw new InvalidOperationException("Independent captured set rejected.");
                if(r.Move.PieceId.StartsWith("A-",StringComparison.Ordinal)&&r.Move.PieceId!=PawnFirst4D.HeroId)throw new InvalidOperationException("Independent hero continuity rejected.");
                currentHash=r.AfterHash;
            }
            if(result.FinalState.Hash!=currentHash||winner!=Side.A||result.FinalState.Outcome!=GameOutcome.KingCapture)throw new InvalidOperationException("Independent ending rejected.");
        }
        private static void RequirePiece(GameState state,QN q,Side side,PieceKind kind)
        {if(!state.Pieces.Any(p=>p.Position==q&&p.Side==side&&p.Kind==kind))throw new InvalidOperationException("Independent initial arrangement rejected.");}
        public static void CheckMove(IEnumerable<Piece> source,Piece p,QN to,OcclusionKind occlusion)
        {
            var pieces=source.ToArray();var d=to-p.Position;int sign=p.Side==Side.A?1:-1;
            int[] a={Math.Abs(d.F),Math.Abs(d.U),Math.Abs(d.V),Math.Abs(d.W)};int count=a.Count(x=>x>0),t=a.Skip(1).Count(x=>x>0);bool allowed=false;
            if(!to.InBounds||p.Side==Side.B&&to.W!=0||pieces.Any(x=>x.Position==to&&x.Side==p.Side))throw new InvalidOperationException("Independent destination rejected.");
            switch(p.Kind)
            {
                case PieceKind.Pawn: allowed=count==1&&(d.F==sign||d.F==0)&&a.Max()==1;break;
                case PieceKind.King: allowed=a.Max()==1;break;
                case PieceKind.Lance: allowed=count==1&&d.F*sign>0;break;
                case PieceKind.Knight: allowed=d.F==2*sign&&t==1&&a.Skip(1).Sum()==1;break;
                case PieceKind.Gold: allowed=a.Max()==1&&(d.F==sign&&t<=1||d.F==0&&t==1||d.F==-sign&&t==0);break;
                case PieceKind.Silver: allowed=a.Max()==1&&(d.F==sign&&t<=1||d.F==-sign&&t==1);break;
                case PieceKind.Rook: allowed=count==1;break;
                case PieceKind.Bishop: allowed=count==2&&a.Where(x=>x>0).Distinct().Count()==1;break;
            }
            if(!allowed)throw new InvalidOperationException("Independent movement rejected.");
            bool slider=p.Kind==PieceKind.Lance||p.Kind==PieceKind.Rook||p.Kind==PieceKind.Bishop;
            if(!slider)return;
            foreach(var other in pieces.Where(x=>x.Position!=p.Position&&x.Position!=to))
            {
                bool intersects;
                if(occlusion==OcclusionKind.LatticeLine)
                {
                    // A point lies strictly between the endpoints and all integer cross-products agree.
                    int axis=Array.FindIndex(a,x=>x>0),delta=d[axis],offset=other.Position[axis]-p.Position[axis];
                    intersects=delta>0?offset>0&&offset<delta:offset<0&&offset>delta;
                    for(int i=0;i<4&&intersects;i++)if((other.Position[i]-p.Position[i])*delta!=offset*d[i])intersects=false;
                }
                else if(occlusion==OcclusionKind.SupercoverClosed)
                {
                    // Independent bounded decimal interval computation; input integers are <= 8.
                    decimal lo=0,hi=1;intersects=true;
                    for(int i=0;i<4&&intersects;i++)
                    {
                        if(d[i]==0){if(Math.Abs(other.Position[i]-p.Position[i])>0)intersects=false;continue;}
                        decimal x=(other.Position[i]-0.5m-p.Position[i])/d[i],y=(other.Position[i]+0.5m-p.Position[i])/d[i];
                        lo=Math.Max(lo,Math.Min(x,y));hi=Math.Min(hi,Math.Max(x,y));if(lo>hi)intersects=false;
                    }
                    intersects=intersects&&hi>0&&lo<1;
                }
                else throw new InvalidOperationException("Independent unsupported occlusion.");
                if(intersects)throw new InvalidOperationException("Independent path blocked.");
            }
        }
    }
}
