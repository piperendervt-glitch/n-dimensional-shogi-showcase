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

    /// <summary>Cell of an N-dimensional board (axis 0 = F forward, 1 = U, 2 = V height, 3 = W, 4.. = further axes).
    /// Axes a cell does not carry are zero: default(QN) is the origin of every board, and (1,0,0,0) equals (1,0,0,0,0).
    /// A board normalizes the cells it stores to its own N (see BoundsN.Normalize), so persisted text always carries N coordinates.</summary>
    public readonly struct QN : IEquatable<QN>
    {
        public const int MinDimensions = 1, MaxDimensions = 7;
        private readonly int[]? c;
        public QN(params int[] coordinates)
        {
            if (coordinates == null || coordinates.Length < MinDimensions || coordinates.Length > MaxDimensions) throw new ArgumentException("A cell has 1 to " + MaxDimensions + " coordinates.");
            c = (int[])coordinates.Clone();
        }
        private QN(int[] owned, bool _) { c = owned; }
        public static QN Zero(int dimensions) => new QN(new int[Checked(dimensions)], true);
        /// <summary>The first coordinates of <paramref name="values"/>, padded with zeros to <paramref name="dimensions"/>.</summary>
        public static QN Padded(IReadOnlyList<int> values, int dimensions)
        {
            var v = new int[Checked(dimensions)];
            for (int i = 0; i < values.Count; i++) { if (i < dimensions) v[i] = values[i]; else if (values[i] != 0) throw new ArgumentException("Too many coordinates for the board."); }
            return new QN(v, true);
        }
        static int Checked(int dimensions) => dimensions >= MinDimensions && dimensions <= MaxDimensions ? dimensions : throw new ArgumentOutOfRangeException(nameof(dimensions));
        public int Dimensions => c?.Length ?? 0;
        /// <summary>Number of leading axes up to the last nonzero coordinate: the smallest board this cell lies on.</summary>
        public int Rank { get { int n = Dimensions; while (n > 0 && c![n - 1] == 0) n--; return n; } }
        public int this[int i] => c != null && i >= 0 && i < c.Length ? c[i] : throw new IndexOutOfRangeException();
        /// <summary>Coordinate on <paramref name="axis"/>, or 0 when the board has fewer axes.</summary>
        public int Axis(int axis) => c != null && axis >= 0 && axis < c.Length ? c[axis] : 0;
        public int F => Axis(0);
        public int U => Axis(1);
        public int V => Axis(2);
        public int W => Axis(3);
        public int[] ToArray() => c == null ? Array.Empty<int>() : (int[])c.Clone();
        public QN WithAxis(int axis, int value) { var v = ToArray(); v[axis] = value; return new QN(v, true); }
        public bool InBounds => c == null || c.All(x => x >= -4 && x <= 4);
        public bool Equals(QN x)
        {
            int n = Math.Max(Dimensions, x.Dimensions);
            for (int i = 0; i < n; i++) if (Axis(i) != x.Axis(i)) return false;
            return true;
        }
        public override bool Equals(object? o) => o is QN q && Equals(q);
        // Over the leading Rank axes, so equal cells of different declared N hash alike; the origin hashes to 0.
        public override int GetHashCode() { int n = Rank; if (n == 0) return 0; int h = c![0]; for (int i = 1; i < n; i++) h = (h * 397) ^ c[i]; return h; }
        public static bool operator ==(QN a, QN b) => a.Equals(b);
        public static bool operator !=(QN a, QN b) => !a.Equals(b);
        public static QN operator +(QN a, QN b) { int n = Math.Max(a.Dimensions, b.Dimensions); var v = new int[n]; for (int i = 0; i < n; i++) v[i] = a.Axis(i) + b.Axis(i); return n == 0 ? default : new QN(v, true); }
        public static QN operator -(QN a, QN b) { int n = Math.Max(a.Dimensions, b.Dimensions); var v = new int[n]; for (int i = 0; i < n; i++) v[i] = a.Axis(i) - b.Axis(i); return n == 0 ? default : new QN(v, true); }
        public int Manhattan(QN x) { int n = Math.Max(Dimensions, x.Dimensions), d = 0; for (int i = 0; i < n; i++) d += Math.Abs(Axis(i) - x.Axis(i)); return d; }
        // Invariant on purpose: cells feed hashes, move keys (CPU tie-break) and saved files, so a locale whose
        // minus sign is U+2212 must not change them. For N=4 the text is identical to the former Q4 ("f,u,v,w").
        public override string ToString() => c == null ? "" : string.Join(",", c.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        /// <summary>Parses "a,b,c,..." (invariant integers); <paramref name="dimensions"/> 0 accepts any supported count.</summary>
        public static bool TryParse(string? text, int dimensions, out QN cell)
        {
            cell = default; if (string.IsNullOrEmpty(text)) return false;
            var parts = text!.Split(','); if (parts.Length < MinDimensions || parts.Length > MaxDimensions || dimensions != 0 && parts.Length != dimensions) return false;
            var v = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v[i])) return false;
            cell = new QN(v, true); return true;
        }
    }
    public sealed class Piece
    {
        public string Id {get;}
        public Side Side {get;}
        public PieceKind Kind {get;}
        public QN Position {get;}
        public Piece(string id,Side side,PieceKind kind,QN position) {Id=id;Side=side;Kind=kind;Position=position;}
        public Piece At(QN position)=>new Piece(Id,Side,Kind,position);
    }
    public readonly struct Move : IEquatable<Move>
    {
        public string PieceId {get;}
        public QN From {get;}
        public QN To {get;}
        public Move(string id,QN from,QN to) {PieceId=id;From=from;To=to;}
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
        private readonly Dictionary<QN,Piece> occupied;
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
            var copy=pieces.Select(p=>p.Position.Dimensions==4?p:p.At(QN.Padded(p.Position.ToArray(),4))).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            if(copy.Any(p=>!p.Position.InBounds||p.Side==Side.B&&p.Position.W!=0))throw new ArgumentException("Invalid position or capability.");
            occupied=copy.ToDictionary(p=>p.Position);byId=copy.ToDictionary(p=>p.Id);
            Pieces=Array.AsReadOnly(copy);Captured=Array.AsReadOnly((captured??Array.Empty<Piece>()).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray());
            Hash=StableHash.Of(Ruleset.Version+"|"+Rules.OcclusionId+"|"+Ruleset.Capabilities+"|"+Turn+"|"+Ply+"|"+Outcome+"|"+Winner+"|"+
                string.Join(";",Pieces.Select(p=>p.Id+":"+p.Side+":"+p.Kind+":"+p.Position))+"|captured:"+string.Join(";",Captured.Select(p=>p.Id)));
        }
        public Piece? At(QN q)=>occupied.TryGetValue(q,out var p)?p:null;
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
                    pieces.Add(new Piece(Id(side,back[u+4],u),side,back[u+4],new QN(-4*s,u,0,0)));
                    pieces.Add(new Piece(Id(side,PieceKind.Pawn,u),side,PieceKind.Pawn,new QN(-2*s,u,0,0)));
                }
                pieces.Add(new Piece(side+"-Rook",side,PieceKind.Rook,new QN(-3*s,-3*s,0,0)));
                pieces.Add(new Piece(side+"-Bishop",side,PieceKind.Bishop,new QN(-3*s,3*s,0,0)));
            }
            return new GameState(pieces,rules:Rules);
        }
        public static string Id(Side side,PieceKind kind,int u)=>side+"-"+kind+"-"+u.ToString(CultureInfo.InvariantCulture);
        private bool PathClear(GameState state,Piece p,QN to)
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
                var to=p.Position+QN.Padded(d,4);var m=new Move(p.Id,p.Position,to);if(IsLegal(state,m))moves.Add(m);
            }
            return moves.OrderBy(m=>m.Key,StringComparer.Ordinal).ToArray();
        }
        public IReadOnlyList<Piece> Attackers(GameState state,QN target,Side by)
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
