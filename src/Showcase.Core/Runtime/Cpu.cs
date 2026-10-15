using System;
using System.Collections.Generic;
using System.Linq;

namespace NDShogi.Showcase.Core
{
    public readonly struct Position3 : IEquatable<Position3>
    {
        public int F {get;} public int U {get;} public int V {get;}
        public Position3(int f,int u,int v){F=f;U=u;V=v;}
        public int[] ToArray()=>new[]{F,U,V};
        public bool InBounds=>F>=-4&&F<=4&&U>=-4&&U<=4&&V>=-4&&V<=4;
        public int Distance(Position3 p)=>Math.Abs(F-p.F)+Math.Abs(U-p.U)+Math.Abs(V-p.V);
        public bool Equals(Position3 p)=>F==p.F&&U==p.U&&V==p.V;
        public override bool Equals(object? o)=>o is Position3 p&&Equals(p);
        public override int GetHashCode()=>((F*397)^U)*397^V;
        public override string ToString()=>F+","+U+","+V;
    }
    public sealed class ObservedPiece3
    {
        public string Id {get;} public Side Side {get;} public PieceKind Kind {get;} public Position3 Position {get;}
        public ObservedPiece3(string id,Side side,PieceKind kind,Position3 position){Id=id;Side=side;Kind=kind;Position=position;}
        public ObservedPiece3 At(Position3 p)=>new ObservedPiece3(Id,Side,Kind,p);
    }
    // Deliberately separate types: this observation and the Tactical3D search tree have no W coordinate.
    public sealed class Observation3D
    {
        public IReadOnlyList<ObservedPiece3> Pieces {get;}
        public Side Turn {get;}
        public int PublicPly {get;}
        public string Hash {get;}
        public Observation3D(IEnumerable<ObservedPiece3> pieces,Side turn,int publicPly)
        {
            Pieces=Array.AsReadOnly(pieces.OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray());Turn=turn;PublicPly=publicPly;
            Hash=StableHash.Of(Ruleset.ObservationVersion+"|"+Turn+"|"+PublicPly+"|"+string.Join(";",Pieces.Select(p=>p.Id+":"+p.Side+":"+p.Kind+":"+p.Position)));
        }
    }
    public static class ObservationFactory
    {
        public static Observation3D ForB(GameState state)=>new Observation3D(state.Pieces.Where(p=>p.Position.W==0).Select(p=>new ObservedPiece3(p.Id,p.Side,p.Kind,new Position3(p.Position.F,p.Position.U,p.Position.V))),state.Turn,state.Ply);
    }
    public static class ObservationBoundaryAudit
    {
        public static void RequireAllowedInput(object input)
        {
            if(input==null||input.GetType()!=typeof(Observation3D))throw new InvalidOperationException("Observation boundary rejected unapproved envelope/full state.");
            var props=typeof(Observation3D).GetProperties().Select(p=>p.Name).OrderBy(x=>x).ToArray();
            if(!props.SequenceEqual(new[]{"Hash","Pieces","PublicPly","Turn"}))throw new InvalidOperationException("Observation schema changed: audit required.");
        }
        public static void RequireNoninterference(CpuDecision a,CpuDecision b)
        {
            if(a.ObservationHash!=b.ObservationHash||a.Fingerprint!=b.Fingerprint)throw new InvalidOperationException("Hidden-state noninterference violation.");
        }
    }
    public sealed class CandidateScore
    {
        public Move Move {get;}
        public int TacticalScore {get;}
        public int PreferenceScore {get;}
        public int Total=>TacticalScore+PreferenceScore;
        public int ReplyNodes {get;}
        public CandidateScore(Move move,int tactical,int preference,int replyNodes){Move=move;TacticalScore=tactical;PreferenceScore=preference;ReplyNodes=replyNodes;}
    }
    public sealed class CpuDecision
    {
        public string Cpu {get;} public Move Move {get;} public string ObservationHash {get;}
        public IReadOnlyList<CandidateScore> Candidates {get;}
        public int Seed {get;} public int NodeBudget {get;} public int NodesVisited {get;}
        public string PreferenceExplanation {get;}
        public bool NonPawnRescue {get;}
        public string Fingerprint {get;}
        public CpuDecision(string cpu,Move move,string observationHash,IEnumerable<CandidateScore> candidates,int seed,int budget,int nodes,string explanation,bool rescue=false)
        {
            Cpu=cpu;Move=move;ObservationHash=observationHash;Candidates=Array.AsReadOnly(candidates.ToArray());Seed=seed;NodeBudget=budget;NodesVisited=nodes;PreferenceExplanation=explanation;NonPawnRescue=rescue;
            Fingerprint=StableHash.Of(cpu+"|"+move.Key+"|"+observationHash+"|"+seed+"|"+budget+"|"+nodes+"|"+string.Join(";",Candidates.Select(c=>c.Move.Key+":"+c.TacticalScore+":"+c.PreferenceScore+":"+c.ReplyNodes)));
        }
    }
    public static class PieceValue
    {
        public static int Of(PieceKind k)=>k==PieceKind.King?10000:k==PieceKind.Rook?900:k==PieceKind.Bishop?800:k==PieceKind.Gold?600:k==PieceKind.Silver?500:k==PieceKind.Knight?350:k==PieceKind.Lance?300:100;
    }
    public sealed class PawnFirst4D
    {
        public const string HeroId="A-Pawn-0";
        public CpuDecision Decide(GameState state,RuleCore core,int seed,int nodeBudget=5000)
        {
            if(state.Turn!=Side.A)throw new InvalidOperationException("A turn required.");
            var all=core.LegalMoves(state);var pawn=all.Where(m=>state.Find(m.PieceId)!.Kind==PieceKind.Pawn).ToArray();
            var moves=pawn.Length>0?pawn:all.ToArray();if(moves.Length==0)throw new InvalidOperationException("No legal moves.");
            if(moves.Length>nodeBudget)throw new InvalidOperationException("Node budget too small to evaluate every root.");
            var hero=state.Find(HeroId)??throw new InvalidOperationException("ScenarioMismatch: hero missing.");var q=hero.Position;
            Q4 goal;
            if(q.W==0&&q.V==0&&q.F<0)goal=new Q4(0,0,0,0);
            else if(q.W==0&&q.V<ScenarioRunner.ThreeDExchangeCount)goal=new Q4(q.F,0,q.V+1,0);
            else if(q.W==0)goal=new Q4(q.F,0,q.V,1);
            else if(q.F<4)goal=new Q4(4,0,q.V,q.W);
            else if(q.V>0)goal=new Q4(4,0,0,q.W);
            else goal=new Q4(4,0,0,0);
            var scores=new List<CandidateScore>();
            foreach(var m in moves)
            {
                int tactical=state.At(m.To) is Piece victim?PieceValue.Of(victim.Kind):0;
                if(core.Attackers(state,m.To,Side.B).Count>0)tactical-=50;
                int preference=m.PieceId==HeroId?100000-10000*m.To.Manhattan(goal):0;
                scores.Add(new CandidateScore(m,tactical,preference,0));
            }
            var choice=scores.OrderByDescending(c=>c.Total).ThenBy(c=>StableHash.Tie(c.Move.Key,seed)).ThenBy(c=>c.Move.Key,StringComparer.Ordinal).First();
            return new CpuDecision("PawnFirst4D/2",choice.Move,state.Hash,scores,seed,nodeBudget,scores.Count,"Disclosed directed preference: central hero +100000; Manhattan goal distance -10000/cell. Goal progresses opening, three height escapes with an intervening B reply each time, W escape, hidden advance, descent, return capture.",pawn.Length==0);
        }
    }
    public sealed class Tactical3D
    {
        private readonly OcclusionKind occlusion;
        public Tactical3D(OcclusionKind occlusion){this.occlusion=occlusion;}
        private readonly struct Move3
        {
            public readonly string Id;public readonly Position3 From;public readonly Position3 To;
            public Move3(string id,Position3 from,Position3 to){Id=id;From=from;To=to;}
            public string Key=>Id+":"+From+">"+To;
            public Move ToProposal()=>new Move(Id,new Q4(From.F,From.U,From.V,0),new Q4(To.F,To.U,To.V,0));
        }
        private sealed class Board3
        {
            public readonly ObservedPiece3[] Pieces;
            private readonly Dictionary<Position3,ObservedPiece3> occupancy;
            public Board3(IEnumerable<ObservedPiece3> pieces){Pieces=pieces.ToArray();occupancy=Pieces.ToDictionary(p=>p.Position);}
            public ObservedPiece3? At(Position3 p)=>occupancy.TryGetValue(p,out var piece)?piece:null;
            public Board3 Apply(Move3 move)
            {
                var p=Pieces.First(x=>x.Id==move.Id);var victim=At(move.To);
                return new Board3(Pieces.Where(x=>x.Id!=p.Id&&x.Id!=victim?.Id).Concat(new[]{p.At(move.To)}));
            }
        }
        private List<Move3> Moves(Board3 board,Side side)
        {
            var list=new List<Move3>();
            foreach(var p in board.Pieces.Where(x=>x.Side==side))foreach(var d in Movement.Deltas(p.Kind,side,3))
            {
                var to=new Position3(p.Position.F+d[0],p.Position.U+d[1],p.Position.V+d[2]);
                if(!to.InBounds||board.At(to)?.Side==side)continue;
                if(Movement.IsSlider(p.Kind)&&board.Pieces.Any(x=>Occlusion.CellBlocks(p.Position.ToArray(),to.ToArray(),x.Position.ToArray(),occlusion)))continue;
                list.Add(new Move3(p.Id,p.Position,to));
            }
            return list.OrderBy(m=>m.Key,StringComparer.Ordinal).ToList();
        }
        public CpuDecision Decide(Observation3D observation,int seed,int nodeBudget=5000)
        {
            ObservationBoundaryAudit.RequireAllowedInput(observation);
            if(observation.Turn!=Side.B)throw new InvalidOperationException("B turn required.");
            var board=new Board3(observation.Pieces);var moves=Moves(board,Side.B);
            if(moves.Count==0)throw new InvalidOperationException("No legal observed moves.");
            if(moves.Count>nodeBudget)throw new InvalidOperationException("Node budget too small to evaluate every root.");
            int repliesPerRoot=(nodeBudget-moves.Count)/moves.Count;
            var hero=board.Pieces.FirstOrDefault(p=>p.Id==PawnFirst4D.HeroId);
            var central=board.Pieces.FirstOrDefault(p=>p.Id=="B-Pawn-0");
            string preferredId;Position3 goal;string explanation;
            if(hero!=null&&hero.Position.F>=0&&central!=null)
            {
                preferredId=central.Id;goal=new Position3(hero.Position.F+1,hero.Position.U,hero.Position.V);
                explanation="Observed central intruder: approach its forward neighbor and match observed height.";
            }
            else
            {
                // An ordinary opening-development plan, evaluated only against observed 3D occupancy.
                // The plan is also used on move 2, before any dimensional escape has happened.
                // There is no test of W, hidden state, hidden piece count, or the escape's ply.
                var development=new[]{
                    (id:"B-Silver--2",at:new Position3(3,-1,0)),
                    (id:"B-Gold-1",at:new Position3(3,0,0)),
                    (id:"B-Silver-2",at:new Position3(3,1,0)),
                    (id:"B-Gold--1",at:new Position3(3,-2,0)),
                    (id:"B-Pawn-3",at:new Position3(0,3,0)),
                    (id:"B-Rook",at:new Position3(1,3,0)),
                    (id:"B-Pawn--3",at:new Position3(0,-3,0))
                };
                preferredId="";goal=new Position3(0,0,0);
                foreach(var target in development)
                {
                    var piece=board.Pieces.FirstOrDefault(p=>p.Id==target.id);
                    if(piece==null||piece.Position.Equals(target.at)||!moves.Any(m=>m.Id==target.id&&m.To.Distance(target.at)<piece.Position.Distance(target.at)))continue;
                    preferredId=target.id;goal=target.at;break;
                }
                explanation="Observation-only ordinary development: develop silver/gold, open a rook file, then advance the rook and opposite pawn; completed or blocked goals are skipped.";
            }
            int beforeMaterial=Material(board),nodes=moves.Count;var scores=new List<CandidateScore>();
            foreach(var m in moves)
            {
                var after=board.Apply(m);int tactical=Material(after)-beforeMaterial;int n=0;
                // Every root is evaluated; a fixed equal reply allocation avoids wall-clock dependence.
                if(after.Pieces.Any(p=>p.Kind==PieceKind.King&&p.Side==Side.A))
                {
                    foreach(var reply in Moves(after,Side.A).OrderByDescending(r=>after.At(r.To) is ObservedPiece3 victim?PieceValue.Of(victim.Kind):0).ThenBy(r=>r.Key,StringComparer.Ordinal).Take(repliesPerRoot))
                    {
                        tactical=Math.Min(tactical,Material(after.Apply(reply))-beforeMaterial);n++;
                    }
                }
                int preference=m.Id==preferredId?100000-10000*m.To.Distance(goal):0;
                scores.Add(new CandidateScore(m.ToProposal(),tactical,preference,n));nodes+=n;
            }
            var choice=scores.OrderByDescending(c=>c.Total).ThenBy(c=>StableHash.Tie(c.Move.Key,seed)).ThenBy(c=>c.Move.Key,StringComparer.Ordinal).First();
            return new CpuDecision("Tactical3D/2",choice.Move,observation.Hash,scores,seed,nodeBudget,nodes,explanation+" Disclosed preference: selected observed piece +100000, goal distance -10000/cell; bounded 2-ply material score over every legal root. Preferences may override tactical caution; no hidden-state input is available.");
        }
        private static int Material(Board3 board)=>board.Pieces.Sum(p=>(p.Side==Side.B?1:-1)*PieceValue.Of(p.Kind));
    }
}
