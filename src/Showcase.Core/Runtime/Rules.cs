using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NDShogi.Showcase.Core
{
    public enum Side { A, B }
    public enum PieceKind { Pawn, Lance, Knight, Silver, Gold, Bishop, Rook, King }
    public enum OcclusionKind { LatticeLine, SupercoverClosed }
    public enum GameOutcome { Ongoing, KingCapture, NoLegalMovesDraw }

    public readonly struct Q4 : IEquatable<Q4>
    {
        public int F { get; }
        public int U { get; }
        public int V { get; }
        public int W { get; }
        public Q4(int f, int u, int v, int w) { F=f; U=u; V=v; W=w; }
        public int this[int i] => i==0?F:i==1?U:i==2?V:i==3?W:throw new IndexOutOfRangeException();
        public int[] ToArray() => new[] {F,U,V,W};
        public bool InBounds => F>=-4&&F<=4&&U>=-4&&U<=4&&V>=-4&&V<=4&&W>=-4&&W<=4;
        public bool Equals(Q4 x) => F==x.F&&U==x.U&&V==x.V&&W==x.W;
        public override bool Equals(object? o) => o is Q4 q&&Equals(q);
        public override int GetHashCode() => (((F*397)^U)*397^V)*397^W;
        public static bool operator ==(Q4 a,Q4 b)=>a.Equals(b);
        public static bool operator !=(Q4 a,Q4 b)=>!a.Equals(b);
        public static Q4 operator +(Q4 a,Q4 b)=>new Q4(a.F+b.F,a.U+b.U,a.V+b.V,a.W+b.W);
        public static Q4 operator -(Q4 a,Q4 b)=>new Q4(a.F-b.F,a.U-b.U,a.V-b.V,a.W-b.W);
        public int Manhattan(Q4 x)=>Math.Abs(F-x.F)+Math.Abs(U-x.U)+Math.Abs(V-x.V)+Math.Abs(W-x.W);
        public override string ToString()=>string.Join(",",F,U,V,W);
    }
    public sealed class Piece
    {
        public string Id {get;}
        public Side Side {get;}
        public PieceKind Kind {get;}
        public Q4 Position {get;}
        public Piece(string id,Side side,PieceKind kind,Q4 position) {Id=id;Side=side;Kind=kind;Position=position;}
        public Piece At(Q4 position)=>new Piece(Id,Side,Kind,position);
    }
    public readonly struct Move : IEquatable<Move>
    {
        public string PieceId {get;}
        public Q4 From {get;}
        public Q4 To {get;}
        public Move(string id,Q4 from,Q4 to) {PieceId=id;From=from;To=to;}
        public bool Equals(Move x)=>PieceId==x.PieceId&&From==x.From&&To==x.To;
        public override bool Equals(object? o)=>o is Move m&&Equals(m);
        public override int GetHashCode()=>PieceId.GetHashCode()^From.GetHashCode()^To.GetHashCode();
        public string Key=>PieceId+":"+From+">"+To;
        public override string ToString()=>Key;
    }
    public sealed class Ruleset
    {
        public const string Version="showcase-asymmetric-4d-king-capture/0.1";
        public const string Capabilities="A:4D-pawn-first;B:3D-w0/1";
        public const string ObservationVersion="logical-w-zero/1";
        public int Dimensions=>4;
        public OcclusionKind Occlusion {get;}
        public string OcclusionId=>Occlusion==OcclusionKind.LatticeLine?"lattice-line/1":"supercover-closed/1";
        public Ruleset(OcclusionKind occlusion=OcclusionKind.LatticeLine,int dimensions=4)
        {
            if(dimensions!=4)throw new ArgumentException("This preset requires N=4.");
            if(!Enum.IsDefined(typeof(OcclusionKind),occlusion))throw new ArgumentException("Unsupported occlusion.");
            Occlusion=occlusion;
        }
        public static OcclusionKind ParseOcclusion(string id)
        {
            if(id=="lattice-line/1"||id=="lattice-line")return OcclusionKind.LatticeLine;
            if(id=="supercover-closed/1"||id=="supercover-closed")return OcclusionKind.SupercoverClosed;
            throw new ArgumentException("Unsupported occlusion: "+id);
        }
    }
    public sealed class GameState
    {
        private readonly Dictionary<Q4,Piece> occupied;
        private readonly Dictionary<string,Piece> byId;
        public IReadOnlyList<Piece> Pieces {get;}
        public IReadOnlyList<Piece> Captured {get;}
        public Ruleset Rules {get;}
        public Side Turn {get;}
        public int Ply {get;}
        public Side? Winner {get;}
        public GameOutcome Outcome {get;}
        public string Hash {get;}
        public GameState(IEnumerable<Piece> pieces,Side turn=Side.A,int ply=0,Ruleset? rules=null,IEnumerable<Piece>? captured=null,Side? winner=null,GameOutcome outcome=GameOutcome.Ongoing)
        {
            Rules=rules??new Ruleset(); Turn=turn;Ply=ply;Winner=winner;Outcome=outcome;
            var copy=pieces.OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            if(copy.Any(p=>!p.Position.InBounds||p.Side==Side.B&&p.Position.W!=0))throw new ArgumentException("Invalid position or capability.");
            occupied=copy.ToDictionary(p=>p.Position);byId=copy.ToDictionary(p=>p.Id);
            Pieces=Array.AsReadOnly(copy);Captured=Array.AsReadOnly((captured??Array.Empty<Piece>()).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray());
            Hash=StableHash.Of(Ruleset.Version+"|"+Rules.OcclusionId+"|"+Ruleset.Capabilities+"|"+Turn+"|"+Ply+"|"+Outcome+"|"+Winner+"|"+
                string.Join(";",Pieces.Select(p=>p.Id+":"+p.Side+":"+p.Kind+":"+p.Position))+"|captured:"+string.Join(";",Captured.Select(p=>p.Id)));
        }
        public Piece? At(Q4 q)=>occupied.TryGetValue(q,out var p)?p:null;
        public Piece? Find(string id)=>byId.TryGetValue(id,out var p)?p:null;
    }
    public static class StableHash
    {
        public static string Of(string value)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
        }
        public static int Tie(string key,int seed)
        {
            unchecked {uint h=(uint)seed+2166136261;foreach(char c in key)h=(h^c)*16777619;return (int)(h%997);}
        }
    }
    public static class Movement
    {
        public static bool IsSlider(PieceKind kind)=>kind==PieceKind.Lance||kind==PieceKind.Rook||kind==PieceKind.Bishop;
        public static bool Allows(PieceKind kind,Side side,IReadOnlyList<int> d)
        {
            int sign=side==Side.A?1:-1,max=0,nonzero=0,transverse=0;
            for(int i=0;i<d.Count;i++){max=Math.Max(max,Math.Abs(d[i]));if(d[i]!=0){nonzero++;if(i>0)transverse++;}}
            if(nonzero==0)return false;
            if(kind==PieceKind.Rook)return nonzero==1;
            if(kind==PieceKind.Bishop){if(nonzero!=2)return false;return d.Count(x=>Math.Abs(x)==max)==2;}
            if(kind==PieceKind.Lance)return nonzero==1&&d[0]*sign>0;
            if(kind==PieceKind.Knight)return d[0]==2*sign&&transverse==1&&d.Skip(1).All(x=>Math.Abs(x)<=1);
            if(max!=1)return false;
            if(kind==PieceKind.King)return true;
            if(kind==PieceKind.Pawn)return nonzero==1&&(d[0]==0||d[0]==sign);
            if(kind==PieceKind.Gold)return d[0]==sign&&transverse<=1||d[0]==0&&transverse==1||d[0]==-sign&&transverse==0;
            if(kind==PieceKind.Silver)return d[0]==sign&&transverse<=1||d[0]==-sign&&transverse==1;
            return false;
        }
        private static readonly Dictionary<string,int[][]> cache=new Dictionary<string,int[][]>();
        public static IReadOnlyList<int[]> Deltas(PieceKind kind,Side side,int dimensions)
        {
            string key=kind+"/"+side+"/"+dimensions;
            lock(cache)
            {
                if(cache.TryGetValue(key,out var found))return found;
                var list=new List<int[]>();
                if(IsSlider(kind))
                {
                    var unit=new int[dimensions]; Enumerate(unit,0,-1,1,d=>{if(!Allows(kind,side,d))return;for(int k=1;k<=8;k++)list.Add(d.Select(x=>x*k).ToArray());});
                }
                else
                {
                    var unit=new int[dimensions]; int r=kind==PieceKind.Knight?2:1;
                    Enumerate(unit,0,-r,r,d=>{if(Allows(kind,side,d))list.Add((int[])d.Clone());});
                }
                found=list.ToArray();cache.Add(key,found);return found;
            }
        }
        private static void Enumerate(int[] d,int axis,int min,int max,Action<int[]> visit)
        {if(axis==d.Length){visit(d);return;}for(int x=min;x<=max;x++){d[axis]=x;Enumerate(d,axis+1,min,max,visit);}}
    }
    public static class Occlusion
    {
        private readonly struct Fraction : IComparable<Fraction>
        {
            public long N {get;} public long D {get;}
            public Fraction(long n,long d){if(d<0){n=-n;d=-d;}N=n;D=d;}
            public int CompareTo(Fraction x)=>(N*x.D).CompareTo(x.N*D);
        }
        public static bool CellBlocks(IReadOnlyList<int> from,IReadOnlyList<int> to,IReadOnlyList<int> cell,OcclusionKind kind)
        {
            if(from.SequenceEqual(cell)||to.SequenceEqual(cell))return false;
            if(kind==OcclusionKind.LatticeLine)
            {
                int g=0;for(int i=0;i<from.Count;i++)g=Gcd(g,Math.Abs(to[i]-from[i]));
                for(int k=1;k<g;k++){bool same=true;for(int i=0;i<from.Count;i++)if(from[i]+k*(to[i]-from[i])/g!=cell[i]){same=false;break;}if(same)return true;}
                return false;
            }
            if(kind!=OcclusionKind.SupercoverClosed)throw new ArgumentException("Unsupported occlusion.");
            var low=new Fraction(0,1);var high=new Fraction(1,1);
            for(int i=0;i<from.Count;i++)
            {
                int d=to[i]-from[i];
                if(d==0){if(2*from[i]<2*cell[i]-1||2*from[i]>2*cell[i]+1)return false;continue;}
                var a=new Fraction(2*cell[i]-1-2*from[i],2*d);var b=new Fraction(2*cell[i]+1-2*from[i],2*d);
                if(a.CompareTo(b)>0){var tmp=a;a=b;b=tmp;}
                if(a.CompareTo(low)>0)low=a;if(b.CompareTo(high)<0)high=b;if(low.CompareTo(high)>0)return false;
            }
            return low.CompareTo(high)<=0&&high.N>0&&low.CompareTo(new Fraction(1,1))<0;
        }
        private static int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}
    }
    public sealed class RuleCore
    {
        public Ruleset Rules {get;}
        public RuleCore(Ruleset? rules=null){Rules=rules??new Ruleset();}
        public GameState InitialState()
        {
            var pieces=new List<Piece>();var back=new[]{PieceKind.Lance,PieceKind.Knight,PieceKind.Silver,PieceKind.Gold,PieceKind.King,PieceKind.Gold,PieceKind.Silver,PieceKind.Knight,PieceKind.Lance};
            foreach(Side side in Enum.GetValues(typeof(Side)))
            {
                int s=side==Side.A?1:-1;
                for(int u=-4;u<=4;u++)
                {
                    pieces.Add(new Piece(Id(side,back[u+4],u),side,back[u+4],new Q4(-4*s,u,0,0)));
                    pieces.Add(new Piece(Id(side,PieceKind.Pawn,u),side,PieceKind.Pawn,new Q4(-2*s,u,0,0)));
                }
                pieces.Add(new Piece(side+"-Rook",side,PieceKind.Rook,new Q4(-3*s,-3*s,0,0)));
                pieces.Add(new Piece(side+"-Bishop",side,PieceKind.Bishop,new Q4(-3*s,3*s,0,0)));
            }
            return new GameState(pieces,rules:Rules);
        }
        public static string Id(Side side,PieceKind kind,int u)=>side+"-"+kind+"-"+u.ToString(CultureInfo.InvariantCulture);
        private bool PathClear(GameState state,Piece p,Q4 to)
        {
            if(!Movement.IsSlider(p.Kind))return true;
            var start=p.Position.ToArray();var end=to.ToArray();
            return !state.Pieces.Any(x=>Occlusion.CellBlocks(start,end,x.Position.ToArray(),Rules.Occlusion));
        }
        public bool IsLegal(GameState state,Move move)
        {
            if(state.Rules.Occlusion!=Rules.Occlusion||state.Outcome!=GameOutcome.Ongoing)return false;
            var p=state.Find(move.PieceId);if(p==null||p.Side!=state.Turn||p.Position!=move.From||!move.To.InBounds)return false;
            if(p.Side==Side.B&&move.To.W!=0)return false;
            if(state.At(move.To)?.Side==p.Side)return false;
            return Movement.Allows(p.Kind,p.Side,(move.To-p.Position).ToArray())&&PathClear(state,p,move.To);
        }
        public IReadOnlyList<Move> LegalMoves(GameState state)
        {
            var moves=new List<Move>();if(state.Outcome!=GameOutcome.Ongoing)return moves;
            foreach(var p in state.Pieces.Where(x=>x.Side==state.Turn))foreach(var d in Movement.Deltas(p.Kind,p.Side,p.Side==Side.B?3:4))
            {
                var to=p.Position+new Q4(d[0],d[1],d[2],d.Length==4?d[3]:0);var m=new Move(p.Id,p.Position,to);if(IsLegal(state,m))moves.Add(m);
            }
            return moves.OrderBy(m=>m.Key,StringComparer.Ordinal).ToArray();
        }
        public IReadOnlyList<Piece> Attackers(GameState state,Q4 target,Side by)
        {
            return state.Pieces.Where(p=>p.Side==by&&(by!=Side.B||target.W==0)&&Movement.Allows(p.Kind,p.Side,(target-p.Position).ToArray())&&PathClear(state,p,target)).ToArray();
        }
        public GameState Apply(GameState state,Move move)
        {
            if(!IsLegal(state,move))throw new InvalidOperationException("Illegal move: "+move);
            var moving=state.Find(move.PieceId)!;var victim=state.At(move.To);
            var pieces=state.Pieces.Where(p=>p.Id!=moving.Id&&p.Id!=victim?.Id).Concat(new[]{moving.At(move.To)});
            var captured=victim==null?state.Captured:state.Captured.Concat(new[]{victim});
            var terminal=victim?.Kind==PieceKind.King;
            var next=new GameState(pieces,Other(state.Turn),state.Ply+1,Rules,captured,terminal?state.Turn:(Side?)null,terminal?GameOutcome.KingCapture:GameOutcome.Ongoing);
            if(!terminal&&LegalMoves(next).Count==0)return new GameState(next.Pieces,next.Turn,next.Ply,Rules,next.Captured,outcome:GameOutcome.NoLegalMovesDraw);
            return next;
        }
        public static Side Other(Side side)=>side==Side.A?Side.B:Side.A;
    }
    public sealed class MoveTransaction
    {
        private readonly RuleCore core;
        private Move? pending;
        private string? before;
        public GameState State {get;private set;}
        public bool HasReservation=>pending.HasValue;
        public int CommitCount {get;private set;}
        public MoveTransaction(RuleCore core,GameState state){this.core=core;State=state;}
        public void Reserve(Move move)
        {
            if(pending.HasValue)throw new InvalidOperationException("A move is already reserved.");
            if(!core.IsLegal(State,move))throw new InvalidOperationException("Illegal reservation.");pending=move;before=State.Hash;
        }
        public GameState Commit()
        {
            if(!pending.HasValue||before!=State.Hash)throw new InvalidOperationException("No valid reservation.");
            State=core.Apply(State,pending.Value);pending=null;before=null;CommitCount++;return State;
        }
        public void Cancel(){pending=null;before=null;}
        public void Restart(GameState initial){Cancel();State=initial;CommitCount=0;}
    }
}
