using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NDShogi.Showcase.Core.Settings
{
    public sealed class TrialPiece
    {
        public string Id { get; }
        public string DefinitionId { get; }
        public Side Side { get; }
        public QN Position { get; }
        public TrialPiece(string id, string definitionId, Side side, QN position)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl) || string.IsNullOrWhiteSpace(definitionId) || definitionId.Length > 128 || definitionId.Any(char.IsControl)) throw new ArgumentException("Invalid trial piece ID.");
            if (!Enum.IsDefined(typeof(Side), side)) throw new ArgumentException("Invalid side.");
            Id = id; DefinitionId = definitionId; Side = side; Position = position;
        }
        public TrialPiece At(QN position) => new TrialPiece(Id, DefinitionId, Side, position);
        public TrialPiece WithDefinition(string id) => new TrialPiece(Id, id, Side, Position);
    }

    public sealed class PreviewSnapshot
    {
        /// <summary>Most pieces a trial board, a match position (MatchState) or a saved bundle may hold: a layer-copied
        /// layout (40 × 9 = 360) with room for its edits. One limit, so whatever a match holds can also be queried and saved.</summary>
        public const int MaxPieces = 1024;
        public IReadOnlyList<TrialPiece> Pieces { get; }
        public string Hash { get; }
        private Dictionary<QN, TrialPiece>? occupancy;
        private int[][]? cells;
        public PreviewSnapshot(IEnumerable<TrialPiece> pieces)
        {
            var copy = pieces.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (copy.Length > MaxPieces || copy.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length || copy.Select(p => p.Position).Distinct().Count() != copy.Length) throw new ArgumentException("Trial pieces must have unique IDs and cells; limit " + MaxPieces + ".");
            Pieces = Array.AsReadOnly(copy);
            Hash = StableHash.Of(string.Join(";", copy.Select(p => p.Id.Length + ":" + p.Id + "|" + p.DefinitionId.Length + ":" + p.DefinitionId + "|" + p.Side + "|" + p.Position)));
        }
        // Built once per (immutable) snapshot and only read afterwards, so queries on worker threads can share them.
        internal Dictionary<QN, TrialPiece> Occupancy
        {
            get { var built = occupancy; if (built != null) return built; built = Pieces.ToDictionary(p => p.Position); return Interlocked.CompareExchange(ref occupancy, built, null) ?? built; }
        }
        /// <summary>Piece cells as arrays, in <see cref="Pieces"/> order: the occlusion test's input for every slider target.</summary>
        internal int[][] Cells
        {
            get { var built = cells; if (built != null) return built; built = Pieces.Select(p => p.Position.ToArray()).ToArray(); return Interlocked.CompareExchange(ref cells, built, null) ?? built; }
        }
    }

    public sealed class PreviewSession
    {
        private PreviewSnapshot snapshot;
        private Dictionary<QN, TrialPiece> occupied = new Dictionary<QN, TrialPiece>();
        private Dictionary<string, TrialPiece> byId = new Dictionary<string, TrialPiece>(StringComparer.Ordinal);
        private readonly Stack<PreviewSnapshot> undo = new Stack<PreviewSnapshot>();
        private readonly Stack<PreviewSnapshot> redo = new Stack<PreviewSnapshot>();
        public BoundsN Bounds { get; }
        public long Revision { get; private set; }
        public IReadOnlyList<TrialPiece> Pieces => snapshot.Pieces;
        public string Hash => snapshot.Hash;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public PreviewSession(BoundsN? bounds = null, IEnumerable<TrialPiece>? pieces = null) : this(bounds, new PreviewSnapshot(pieces ?? Array.Empty<TrialPiece>())) { }
        // Shares an existing immutable snapshot (a match position's), so its hash and lookups are not rebuilt.
        internal PreviewSession(BoundsN? bounds, PreviewSnapshot pieces)
        {
            Bounds = bounds ?? new BoundsN(); snapshot = Normalized(pieces); Validate(snapshot); Reindex();
        }
        private void Validate(PreviewSnapshot next)
        {
            if (next.Pieces.Any(p => !Bounds.Contains(p.Position))) throw new ArgumentException("Trial piece outside preview bounds.");
        }
        // Stored (and hashed) cells always carry this board's N coordinates, whatever N the caller's cell declared.
        private PreviewSnapshot Normalized(PreviewSnapshot next) => next.Pieces.All(p => p.Position.Dimensions == Bounds.Dimensions) ? next : new PreviewSnapshot(next.Pieces.Select(p => Bounds.Contains(p.Position) ? p.At(Bounds.Normalize(p.Position)) : p));
        private void Reindex() { occupied = snapshot.Occupancy; byId = Pieces.ToDictionary(p => p.Id, StringComparer.Ordinal); }
        private void Change(PreviewSnapshot next)
        {
            next = Normalized(next); Validate(next); if (next.Hash == snapshot.Hash) return;
            undo.Push(snapshot); snapshot = next; redo.Clear(); Reindex(); Revision++;
        }
        // Snapshots are immutable, so the current one is returned as is: copying it re-sorted and re-hashed every piece on each query.
        public PreviewSnapshot Snapshot() => snapshot;
        public TrialPiece? At(QN q) => occupied.TryGetValue(q, out var piece) ? piece : null;
        public TrialPiece? Find(string id) => byId.TryGetValue(id, out var piece) ? piece : null;
        public void Place(TrialPiece piece)
        {
            var occupant = At(piece.Position);
            if (occupant != null && occupant.Id != piece.Id) throw new InvalidOperationException("試し配置：同じセルに重ねられません。先に消去してください。");
            Change(new PreviewSnapshot(Pieces.Where(p => p.Id != piece.Id).Concat(new[] { piece })));
        }
        public bool Erase(QN q)
        {
            var piece = At(q); if (piece == null) return false;
            Change(new PreviewSnapshot(Pieces.Where(p => p.Id != piece.Id))); return true;
        }
        public void Clear() => Change(new PreviewSnapshot(Array.Empty<TrialPiece>()));
        public void ReplaceDefinition(string pieceId, string definitionId)
        {
            var piece = Find(pieceId) ?? throw new ArgumentException("Unknown trial piece."); Place(piece.WithDefinition(definitionId));
        }
        public bool Undo()
        {
            if (!CanUndo) return false; redo.Push(snapshot); snapshot = undo.Pop(); Reindex(); Revision++; return true;
        }
        public bool Redo()
        {
            if (!CanRedo) return false; undo.Push(snapshot); snapshot = redo.Pop(); Reindex(); Revision++; return true;
        }
        public bool TryMove(PreviewPreset preset, string pieceId, QN target, out string reason)
        {
            var result = PreviewQuery.Execute(preset, this, pieceId);
            if (result.Status != QueryStatus.Complete || !result.P.Contains(target)) { reason = "P（遮蔽・占有込み）の試し移動候補ではありません。Lは未評価です。"; return false; }
            var piece = Find(pieceId)!;
            Change(new PreviewSnapshot(Pieces.Where(p => p.Id != pieceId && p.Position != target).Concat(new[] { piece.At(target) })));
            reason = "試し盤だけを移動しました（本番合法手ではありません）。"; return true;
        }
        public static PreviewSession CreateExperiment(int dimensions = 4) => new PreviewSession(new BoundsN(dimensions: dimensions), new[] { new TrialPiece("subject", "king-step", Side.A, QN.Zero(dimensions)) });
    }

    public enum QueryStatus { Complete, Partial, Cancelled, Unsupported, Invalid }
    public readonly struct QueryStamp : IEquatable<QueryStamp>
    {
        public string SessionId { get; }
        public long DraftRevision { get; }
        public long TrialRevision { get; }
        public string PieceId { get; }
        public long RequestId { get; }
        public QueryStamp(string sessionId, long draftRevision, long trialRevision, string pieceId, long requestId = 0)
        { SessionId = sessionId; DraftRevision = draftRevision; TrialRevision = trialRevision; PieceId = pieceId; RequestId = requestId; }
        public bool Equals(QueryStamp other) => SessionId == other.SessionId && DraftRevision == other.DraftRevision && TrialRevision == other.TrialRevision && PieceId == other.PieceId && RequestId == other.RequestId;
        public override bool Equals(object? value) => value is QueryStamp stamp && Equals(stamp);
        public override int GetHashCode() => (SessionId ?? "").GetHashCode() ^ DraftRevision.GetHashCode() ^ TrialRevision.GetHashCode() ^ (PieceId ?? "").GetHashCode() ^ RequestId.GetHashCode();
    }
    public sealed class QueryResult
    {
        public QueryStamp Stamp { get; }
        public QueryStatus Status { get; }
        public string Reason { get; }
        public IReadOnlyList<QN> G { get; }
        public IReadOnlyList<QN> P { get; }
        public int? TheoreticalCount { get; }
        public int EvaluatedCandidates { get; }
        public string LStatus => "未評価：完全合法性・王手回避・成り打ち・終局はCFG-2以降。";
        internal QueryResult(QueryStamp stamp, QueryStatus status, string reason, IEnumerable<QN> g, IEnumerable<QN> p, int? theoretical, int evaluated)
        { Stamp = stamp; Status = status; Reason = reason; G = Array.AsReadOnly(g.ToArray()); P = Array.AsReadOnly(p.ToArray()); TheoreticalCount = theoretical; EvaluatedCandidates = evaluated; }
    }
    public sealed class LatestQueryGate
    {
        private QueryStamp current;
        private bool hasCurrent;
        public QueryResult? Latest { get; private set; }
        public void Begin(QueryStamp stamp) { current = stamp; hasCurrent = true; Latest = null; }
        public void Invalidate() { hasCurrent = false; Latest = null; }
        public bool TryAccept(QueryResult result)
        { if (!hasCurrent || !current.Equals(result.Stamp)) return false; Latest = result; return true; }
    }
    public sealed class AttackQueryResult
    {
        public QueryStatus Status { get; }
        public IReadOnlyList<TrialPiece> Attackers { get; }
        public string Meaning => "捕獲定義と経路による全次元の幾何攻撃。王捕獲許可・王手回避・勝敗は未評価。";
        internal AttackQueryResult(QueryStatus status, IEnumerable<TrialPiece> attackers) { Status = status; Attackers = Array.AsReadOnly(attackers.ToArray()); }
    }

    public static class PreviewQuery
    {
        // Delta lists keyed by everything they depend on, as flat rows of N coordinates. An editable piece walks all 3^N unit
        // directions to build its list (177,147 at N=11), so the walk is done once per key instead of on every query.
        private static readonly Dictionary<string, int[]> deltaCache = new Dictionary<string, int[]>();
        private static int cachedCoordinates;
        // About 32 MB of coordinates: every list a match uses at N=11 (≈2M) fits many times; editing a definition in the
        // settings adds a key per variant, so the cache starts over rather than grow without bound.
        private const int MaxCachedCoordinates = 1 << 23;
        private static int[] Deltas(PieceDefinition definition, Side side, int forwardAxis, int dimensions)
        {
            bool fixedKind = definition.IsSupported && !definition.IsEditable;
            string key = fixedKind ? "fixed/" + definition.FixedKind + "/" + side + "/" + dimensions
                : definition.Kind + "/" + definition.AxesMask + "/" + definition.MaxAxes + "/" + definition.MaxDistance + "/" + definition.Forward + "/" + definition.Sideways + "/" + definition.Backward + "/" + side + "/" + forwardAxis + "/" + dimensions;
            lock (deltaCache)
            {
                if (deltaCache.TryGetValue(key, out var found)) return found;
                var list = new List<int>();
                if (fixedKind) foreach (var d in Movement.Deltas(definition.FixedKind, side, dimensions)) list.AddRange(d);
                else
                {
                    int distance = definition.Kind == PreviewMovementKind.Step ? 1 : definition.MaxDistance;
                    // Every unit direction of {-1,0,1}^N in the same (lexicographic) order the former four nested loops produced for N=4:
                    // an odometer whose last axis turns fastest.
                    var u = new int[dimensions]; for (int i = 0; i < dimensions; i++) u[i] = -1;
                    for (int axis = 0; axis >= 0;)
                    {
                        if (definition.Allows(new QN(u), side, forwardAxis)) for (int scale = 1; scale <= distance; scale++) foreach (int x in u) list.Add(x * scale);
                        for (axis = dimensions - 1; axis >= 0 && u[axis] == 1; axis--) u[axis] = -1;
                        if (axis >= 0) u[axis]++;
                    }
                }
                found = list.ToArray();
                if (cachedCoordinates + found.Length > MaxCachedCoordinates) { deltaCache.Clear(); cachedCoordinates = 0; }
                deltaCache.Add(key, found); cachedCoordinates += found.Length; return found;
            }
        }
        /// <summary>from + the delta in row <paramref name="row"/>, written to <paramref name="to"/>; false when it leaves Min..Max on some axis.</summary>
        private static bool Target(int[] from, int[] deltas, int row, BoundsN bounds, int[] to)
        {
            for (int i = 0, o = row * to.Length; i < to.Length; i++) { int x = from[i] + deltas[o + i]; if (x < bounds.Min || x > bounds.Max) return false; to[i] = x; }
            return true;
        }
        private static bool PathClear(PreviewPreset preset, PreviewSnapshot snapshot, int[] from, int[] to, PieceDefinition definition)
        {
            if (!definition.IsSlider) return true;
            foreach (var cell in snapshot.Cells) if (InBox(from, to, cell) && Occlusion.CellBlocks(from, to, cell, preset.Occlusion)) return false;
            return true;
        }
        // Under both occlusion models a blocking cell lies, on every axis, between the move's two ends (a lattice point of
        // the segment, or a unit cube it meets); CellBlocks is false for any other cell, so those are skipped unasked.
        private static bool InBox(int[] from, int[] to, int[] cell)
        {
            for (int i = 0; i < cell.Length; i++) { int a = from[i], b = to[i], x = cell[i]; if (a <= b ? x < a || x > b : x < b || x > a) return false; }
            return true;
        }
        // Min..Max alone is not enough: a board of another N would index axes the other one lacks.
        private static string? DomainMismatch(PreviewSession session, PreviewPreset preset)
            => session.Bounds.Dimensions != preset.Bounds.Dimensions ? "試し盤のN=" + session.Bounds.Dimensions + "と設定のN=" + preset.Bounds.Dimensions + "が一致しません。"
                : !session.Bounds.SameDomain(preset.Bounds) ? "試し盤と設定の盤域が一致しません。" : null;
        public static QueryResult Execute(PreviewPreset preset, PreviewSession session, string pieceId, CancellationToken cancellation = default, int candidateBudget = int.MaxValue, QueryStamp stamp = default)
        {
            var snapshot = session.Snapshot(); var mover = snapshot.Pieces.FirstOrDefault(p => p.Id == pieceId);
            var g = new List<QN>(); var p = new List<QN>();
            QueryResult Result(QueryStatus status, string reason, int? theoretical = null, int evaluated = 0) => new QueryResult(stamp, status, reason, g, p, theoretical, evaluated);
            if (candidateBudget < 0) throw new ArgumentOutOfRangeException(nameof(candidateBudget));
            if (mover == null) return Result(QueryStatus.Invalid, "検証対象の駒がありません。Lは未評価です。");
            var definition = preset.Find(mover.DefinitionId);
            if (definition == null) return Result(QueryStatus.Invalid, "試し駒の定義IDが見つかりません。");
            if (!definition.IsSupported) return Result(QueryStatus.Unsupported, "この既存移動成分は初回のG/P照会に未対応です。置換していません。");
            var mismatch = DomainMismatch(session, preset);
            if (mismatch != null || snapshot.Pieces.Any(x => !preset.Bounds.Contains(x.Position))) return Result(QueryStatus.Invalid, mismatch ?? "試し盤と設定の盤域が一致しません。");
            int total = 0, n = preset.Bounds.Dimensions; var occupied = snapshot.Occupancy;
            var deltas = Deltas(definition, mover.Side, preset.ForwardAxis, n); var from = mover.Position.ToArray(); var to = new int[n];
            for (int row = 0, rows = deltas.Length / n; row < rows; row++)
            {
                if (cancellation.IsCancellationRequested) return Result(QueryStatus.Cancelled, "取消：確定全件数・合法手0件ではありません。", evaluated: total);
                if (total >= candidateBudget) return Result(QueryStatus.Partial, "一部計算：未評価の候補があります。", evaluated: total);
                total++;
                if (!Target(from, deltas, row, preset.Bounds, to)) continue;
                var target = new QN(to);
                g.Add(target);
                occupied.TryGetValue(target, out var occupant);
                if (occupant != null && occupant.Side == mover.Side) continue;
                if (occupant == null ? !definition.AllowEmpty : !definition.AllowCapture) continue;
                if (PathClear(preset, snapshot, from, to, definition)) p.Add(target);
            }
            return Result(QueryStatus.Complete, "G/P計算完了。セル数です。L・成り別着手数・終局は未評価。", total, total);
        }
        public static AttackQueryResult Attackers(PreviewPreset preset, PreviewSession session, QN target, Side by, CancellationToken cancellation = default)
        {
            if (!preset.Bounds.Contains(target) || DomainMismatch(session, preset) != null) return new AttackQueryResult(QueryStatus.Invalid, Array.Empty<TrialPiece>());
            target = preset.Bounds.Normalize(target);
            var snapshot = session.Snapshot(); var pieces = snapshot.Pieces; var attackers = new List<TrialPiece>(); bool unsupported = false; var to = target.ToArray();
            foreach (var piece in pieces.Where(p => p.Side == by))
            {
                if (cancellation.IsCancellationRequested) return new AttackQueryResult(QueryStatus.Cancelled, attackers);
                var definition = preset.Find(piece.DefinitionId);
                if (definition == null || !definition.IsSupported) { unsupported = true; continue; }
                if (definition.AllowCapture && definition.Allows(target - piece.Position, piece.Side, preset.ForwardAxis) && PathClear(preset, snapshot, piece.Position.ToArray(), to, definition)) attackers.Add(piece);
            }
            return new AttackQueryResult(unsupported ? QueryStatus.Partial : QueryStatus.Complete, attackers);
        }
        public static AttackRangeResult AttackCells(PreviewPreset preset, PreviewSession session, string pieceId, CancellationToken cancellation = default)
        {
            var snapshot = session.Snapshot(); var piece = snapshot.Pieces.FirstOrDefault(x => x.Id == pieceId);
            var definition = piece == null ? null : preset.Find(piece.DefinitionId);
            if (piece == null || definition == null || DomainMismatch(session, preset) != null)
                return new AttackRangeResult(QueryStatus.Invalid, Array.Empty<QN>());
            if (!definition.IsSupported) return new AttackRangeResult(QueryStatus.Unsupported, Array.Empty<QN>());
            var cells = new List<QN>();
            if (!definition.AllowCapture) return new AttackRangeResult(QueryStatus.Complete, cells);
            int n = preset.Bounds.Dimensions; var deltas = Deltas(definition, piece.Side, preset.ForwardAxis, n); var from = piece.Position.ToArray(); var to = new int[n];
            for (int row = 0, rows = deltas.Length / n; row < rows; row++)
            {
                if (cancellation.IsCancellationRequested) return new AttackRangeResult(QueryStatus.Cancelled, cells);
                if (Target(from, deltas, row, preset.Bounds, to) && PathClear(preset, snapshot, from, to, definition)) cells.Add(new QN(to));
            }
            return new AttackRangeResult(QueryStatus.Complete, cells);
        }
    }
    public sealed class AttackRangeResult
    {
        public QueryStatus Status { get; }
        public IReadOnlyList<QN> Cells { get; }
        public string Meaning => "全次元の幾何攻撃（味方を守るセルを含む）。自玉安全による完全合法性ではありません。";
        internal AttackRangeResult(QueryStatus status, IEnumerable<QN> cells) { Status = status; Cells = Array.AsReadOnly(cells.ToArray()); }
    }
}
