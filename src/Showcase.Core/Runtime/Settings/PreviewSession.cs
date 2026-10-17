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
        public IReadOnlyList<TrialPiece> Pieces { get; }
        public string Hash { get; }
        public PreviewSnapshot(IEnumerable<TrialPiece> pieces)
        {
            var copy = pieces.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            // 1024 so that a layer-copied match position (360 pieces) can be queried through the same snapshot type.
            if (copy.Length > 1024 || copy.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length || copy.Select(p => p.Position).Distinct().Count() != copy.Length) throw new ArgumentException("Trial pieces must have unique IDs and cells; limit 1024.");
            Pieces = Array.AsReadOnly(copy);
            Hash = StableHash.Of(string.Join(";", copy.Select(p => p.Id.Length + ":" + p.Id + "|" + p.DefinitionId.Length + ":" + p.DefinitionId + "|" + p.Side + "|" + p.Position)));
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
        public PreviewSession(BoundsN? bounds = null, IEnumerable<TrialPiece>? pieces = null)
        {
            Bounds = bounds ?? new BoundsN(); snapshot = Normalized(new PreviewSnapshot(pieces ?? Array.Empty<TrialPiece>())); Validate(snapshot); Reindex();
        }
        private void Validate(PreviewSnapshot next)
        {
            if (next.Pieces.Any(p => !Bounds.Contains(p.Position))) throw new ArgumentException("Trial piece outside preview bounds.");
        }
        // Stored (and hashed) cells always carry this board's N coordinates, whatever N the caller's cell declared.
        private PreviewSnapshot Normalized(PreviewSnapshot next) => next.Pieces.All(p => p.Position.Dimensions == Bounds.Dimensions) ? next : new PreviewSnapshot(next.Pieces.Select(p => Bounds.Contains(p.Position) ? p.At(Bounds.Normalize(p.Position)) : p));
        private void Reindex() { occupied = Pieces.ToDictionary(p => p.Position); byId = Pieces.ToDictionary(p => p.Id, StringComparer.Ordinal); }
        private void Change(PreviewSnapshot next)
        {
            next = Normalized(next); Validate(next); if (next.Hash == snapshot.Hash) return;
            undo.Push(snapshot); snapshot = next; redo.Clear(); Reindex(); Revision++;
        }
        public PreviewSnapshot Snapshot() => new PreviewSnapshot(Pieces);
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
        public string Meaning => "捕獲定義と経路による全4Dの幾何攻撃。王捕獲許可・王手回避・勝敗は未評価。";
        internal AttackQueryResult(QueryStatus status, IEnumerable<TrialPiece> attackers) { Status = status; Attackers = Array.AsReadOnly(attackers.ToArray()); }
    }

    public static class PreviewQuery
    {
        private static IEnumerable<QN> Deltas(PieceDefinition definition, Side side, int forwardAxis, int dimensions)
        {
            if (definition.IsSupported && !definition.IsEditable)
            {
                foreach (var d in Movement.Deltas(definition.FixedKind, side, dimensions)) yield return new QN(d);
                yield break;
            }
            int distance = definition.Kind == PreviewMovementKind.Step ? 1 : definition.MaxDistance;
            // Every unit direction of {-1,0,1}^N in the same (lexicographic) order the former four nested loops produced for N=4.
            var unit = new int[dimensions];
            foreach (var u in UnitDirections(unit, 0))
            {
                var step = new QN(u); if (!definition.Allows(step, side, forwardAxis)) continue;
                for (int scale = 1; scale <= distance; scale++) yield return new QN(u.Select(x => x * scale).ToArray());
            }
        }
        private static IEnumerable<int[]> UnitDirections(int[] unit, int axis)
        {
            if (axis == unit.Length) { yield return unit; yield break; }
            for (int x = -1; x <= 1; x++) { unit[axis] = x; foreach (var u in UnitDirections(unit, axis + 1)) yield return u; }
        }
        private static bool PathClear(PreviewPreset preset, IReadOnlyList<TrialPiece> pieces, TrialPiece mover, QN target, PieceDefinition definition)
        {
            if (!definition.IsSlider) return true;
            return !pieces.Any(p => Occlusion.CellBlocks(mover.Position.ToArray(), target.ToArray(), p.Position.ToArray(), preset.Occlusion));
        }
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
            if (session.Bounds.Min != preset.Bounds.Min || session.Bounds.Max != preset.Bounds.Max || snapshot.Pieces.Any(x => !preset.Bounds.Contains(x.Position))) return Result(QueryStatus.Invalid, "試し盤と設定の盤域が一致しません。");
            int total = 0; var occupied = snapshot.Pieces.ToDictionary(x => x.Position);
            foreach (var delta in Deltas(definition, mover.Side, preset.ForwardAxis, preset.Bounds.Dimensions))
            {
                if (cancellation.IsCancellationRequested) return Result(QueryStatus.Cancelled, "取消：確定全件数・合法手0件ではありません。", evaluated: total);
                if (total >= candidateBudget) return Result(QueryStatus.Partial, "一部計算：未評価の候補があります。", evaluated: total);
                total++;
                var target = mover.Position + delta;
                if (!preset.Bounds.Contains(target)) continue;
                g.Add(target);
                occupied.TryGetValue(target, out var occupant);
                if (occupant != null && occupant.Side == mover.Side) continue;
                if (occupant == null ? !definition.AllowEmpty : !definition.AllowCapture) continue;
                if (PathClear(preset, snapshot.Pieces, mover, target, definition)) p.Add(target);
            }
            return Result(QueryStatus.Complete, "G/P計算完了。セル数です。L・成り別着手数・終局は未評価。", total, total);
        }
        public static AttackQueryResult Attackers(PreviewPreset preset, PreviewSession session, QN target, Side by, CancellationToken cancellation = default)
        {
            if (!preset.Bounds.Contains(target) || session.Bounds.Min != preset.Bounds.Min || session.Bounds.Max != preset.Bounds.Max) return new AttackQueryResult(QueryStatus.Invalid, Array.Empty<TrialPiece>());
            var pieces = session.Snapshot().Pieces; var attackers = new List<TrialPiece>(); bool unsupported = false;
            foreach (var piece in pieces.Where(p => p.Side == by))
            {
                if (cancellation.IsCancellationRequested) return new AttackQueryResult(QueryStatus.Cancelled, attackers);
                var definition = preset.Find(piece.DefinitionId);
                if (definition == null || !definition.IsSupported) { unsupported = true; continue; }
                if (definition.AllowCapture && definition.Allows(target - piece.Position, piece.Side, preset.ForwardAxis) && PathClear(preset, pieces, piece, target, definition)) attackers.Add(piece);
            }
            return new AttackQueryResult(unsupported ? QueryStatus.Partial : QueryStatus.Complete, attackers);
        }
        public static AttackRangeResult AttackCells(PreviewPreset preset, PreviewSession session, string pieceId, CancellationToken cancellation = default)
        {
            var pieces = session.Snapshot().Pieces; var piece = pieces.FirstOrDefault(x => x.Id == pieceId);
            var definition = piece == null ? null : preset.Find(piece.DefinitionId);
            if (piece == null || definition == null || session.Bounds.Min != preset.Bounds.Min || session.Bounds.Max != preset.Bounds.Max)
                return new AttackRangeResult(QueryStatus.Invalid, Array.Empty<QN>());
            if (!definition.IsSupported) return new AttackRangeResult(QueryStatus.Unsupported, Array.Empty<QN>());
            var cells = new List<QN>();
            if (!definition.AllowCapture) return new AttackRangeResult(QueryStatus.Complete, cells);
            foreach (var delta in Deltas(definition, piece.Side, preset.ForwardAxis, preset.Bounds.Dimensions))
            {
                if (cancellation.IsCancellationRequested) return new AttackRangeResult(QueryStatus.Cancelled, cells);
                var target = piece.Position + delta;
                if (preset.Bounds.Contains(target) && PathClear(preset, pieces, piece, target, definition)) cells.Add(target);
            }
            return new AttackRangeResult(QueryStatus.Complete, cells);
        }
    }
    public sealed class AttackRangeResult
    {
        public QueryStatus Status { get; }
        public IReadOnlyList<QN> Cells { get; }
        public string Meaning => "全4Dの幾何攻撃（味方を守るセルを含む）。自玉安全による完全合法性ではありません。";
        internal AttackRangeResult(QueryStatus status, IEnumerable<QN> cells) { Status = status; Cells = Array.AsReadOnly(cells.ToArray()); }
    }
}
