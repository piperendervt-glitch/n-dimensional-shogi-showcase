using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using NDShogi.Showcase.Core.Settings;

namespace NDShogi.Showcase.Core.Match
{
    public enum MatchMode { PvP, PvC, CvC }
    public enum MatchController { Human, Cpu }
    public enum MatchOutcome { Ongoing, KingCapture, NoMovesDraw, TimeLoss, Interrupted }
    public enum MatchPhase { Active, Presentation, CvcPaused, ProcessingPaused, Finished }
    public enum MatchQueryStatus { Complete, Partial, Cancelled, Unsupported, Invalid, Faulted }
    public interface IMonotonicClock { double Seconds { get; } }
    public sealed class StopwatchClock : IMonotonicClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public double Seconds => watch.Elapsed.TotalSeconds;
    }
    public sealed class MatchOptionsRaw
    {
        public string TimeSeconds = "600";
        public string CpuSeed = "20261017";
        public string CpuBudget = "65536";
        public string CpuDelayMilliseconds = "500";
        public MatchOptionsRaw Clone() => (MatchOptionsRaw)MemberwiseClone();
    }
    public sealed class MatchConfig
    {
        public const string RuleId = "CFG2_DEMO_4D";
        /// <summary>Rule id of an N≠4 match. N=4 keeps <see cref="RuleId"/> so existing 4D hashes and records are unchanged.</summary>
        public const string RuleIdND = "CFG2_DEMO_ND";
        public int Dimensions => Preset.Bounds.Dimensions;
        public string EffectiveRuleId => Dimensions == 4 ? RuleId : RuleIdND;
        public const string RuleVersion = "1";
        public const string CpuId = "capture-stable-seeded/1";
        public const string ClockPolicy = "monotonic; no-increment; presentation-pauses-both; explicit-cvc-pause; focus-does-not-pause/1";
        public PreviewPreset Preset { get; }
        public double TimeSeconds { get; }
        public int CpuSeed { get; }
        public int CpuBudget { get; }
        public int CpuDelayMilliseconds { get; }
        public string RuleHash { get; }
        public string SettingsHash { get; }
        private readonly MatchOptionsRaw raw;
        private MatchConfig(PreviewPreset preset, MatchOptionsRaw options, double time, int seed, int budget, int delay)
        {
            Preset = preset; raw = options.Clone(); TimeSeconds = time; CpuSeed = seed; CpuBudget = budget; CpuDelayMilliseconds = delay;
            // N=4 hashes exactly as before; other N carry their rule id and dimension count in the hash.
            string ruleId = preset.Bounds.Dimensions == 4 ? RuleId : RuleIdND + "/N" + preset.Bounds.Dimensions.ToString(CultureInfo.InvariantCulture);
            RuleHash = StableHash.Of(ruleId + "/" + RuleVersion + "|" + preset.RuleHash + "|king-capture;no-moves-draw;time-loss;no-self-check;no-promotion;no-drops;no-repetition|" + time.ToString("R", CultureInfo.InvariantCulture));
            SettingsHash = StableHash.Of(RuleHash + "|" + CpuId + "|" + seed.ToString(CultureInfo.InvariantCulture) + "|" + budget.ToString(CultureInfo.InvariantCulture) + "|" + delay.ToString(CultureInfo.InvariantCulture) + "|" + ClockPolicy);
        }
        public MatchOptionsRaw ToRaw() => raw.Clone();
        public static string DefinitionId(PieceKind kind) => kind == PieceKind.King ? "king-step" : kind == PieceKind.Pawn ? "pawn-step" : kind == PieceKind.Rook ? "rook-h11" : kind == PieceKind.Bishop ? "bishop-h22" : kind.ToString().ToLowerInvariant() + "-legacy";
        public static bool TryCreate(SettingsDraft draft, MatchOptionsRaw options, out MatchConfig? config, out ValidationReport report)
        {
            config = null; draft.TryCompile(out var preset, out var previewReport); var errors = previewReport.Errors.ToList();
            if (!double.TryParse(options.TimeSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double time) || double.IsNaN(time) || double.IsInfinity(time) || time <= 0 || time > TimeSpan.MaxValue.TotalSeconds) errors.Add("持ち時間は有限の正の秒数です。生入力を保持しています。");
            if (!int.TryParse(options.CpuSeed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) errors.Add("CPU seedは32bit整数です。");
            if (!int.TryParse(options.CpuBudget, NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget) || budget < 1 || budget > 1000000) errors.Add("CPU候補予算は1〜1000000です。予算終了を引分にはしません。");
            if (!int.TryParse(options.CpuDelayMilliseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay) || delay < 0 || delay > 60000) errors.Add("CPU表示待ちは0〜60000msです。");
            if (preset != null)
            {
                if (preset.Bounds.Min != -4 || preset.Bounds.Max != 4 || preset.ForwardAxis != 0) errors.Add("対局は各軸−4..4・前方Z(F)です。");
                foreach (PieceKind kind in Enum.GetValues(typeof(PieceKind)))
                {
                    var definition = preset.Find(DefinitionId(kind));
                    if (definition == null || !definition.IsSupported) { errors.Add("初期配置の駒定義が未対応または欠損：" + kind); continue; }
                    if (kind == PieceKind.Gold && definition.Kind != PreviewMovementKind.Gold || kind == PieceKind.Silver && definition.Kind != PreviewMovementKind.Silver || kind == PieceKind.Knight && definition.Kind != PreviewMovementKind.Knight || kind == PieceKind.Lance && definition.Kind != PreviewMovementKind.Lance)
                        errors.Add("固定の金・銀・桂・香定義を別ルールへ置換できません：" + kind);
                    if ((kind == PieceKind.King || kind == PieceKind.Pawn || kind == PieceKind.Rook || kind == PieceKind.Bishop) && !definition.IsEditable) errors.Add("編集可能駒の移動方式が未対応：" + kind);
                }
            }
            report = new ValidationReport(errors, previewReport.Warnings);
            if (!report.IsValid || preset == null) return false;
            config = new MatchConfig(preset, options, time, seed, budget, delay); return true;
        }
        public static MatchConfig CreateDefault()
        {
            if (!TryCreate(SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw(), out var config, out var report)) throw new InvalidOperationException(string.Join(" / ", report.Errors));
            return config!;
        }
    }
    public sealed class CapturedPiece
    {
        public Side Captor { get; }
        public Piece Victim { get; }
        public int Ply { get; }
        internal CapturedPiece(Side captor, Piece victim, int ply) { Captor = captor; Victim = victim; Ply = ply; }
    }
    public sealed class MatchState
    {
        public IReadOnlyList<Piece> Pieces { get; }
        public IReadOnlyList<CapturedPiece> Captured { get; }
        public Side Turn { get; }
        public int Ply { get; }
        public long Revision { get; }
        public MatchOutcome Outcome { get; }
        public Side? Winner { get; }
        public string Hash { get; }
        private readonly Dictionary<QN, Piece> occupied;
        private readonly Dictionary<string, Piece> byId;
        internal MatchState(IEnumerable<Piece> pieces, Side turn, int ply, long revision, MatchConfig config, IEnumerable<CapturedPiece>? captured = null, MatchOutcome outcome = MatchOutcome.Ongoing, Side? winner = null)
        {
            var bounds = config.Preset.Bounds;
            var list = pieces.Select(p => bounds.Contains(p.Position) ? p.At(bounds.Normalize(p.Position)) : p).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (list.Length > 256 || !Enum.IsDefined(typeof(Side), turn) || list.Any(p => string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 128 || p.Id.Any(char.IsControl) || !Enum.IsDefined(typeof(PieceKind), p.Kind) || !Enum.IsDefined(typeof(Side), p.Side) || !config.Preset.Bounds.Contains(p.Position))) throw new ArgumentException("Invalid match piece/side/bounds.");
            occupied = list.ToDictionary(p => p.Position); byId = list.ToDictionary(p => p.Id, StringComparer.Ordinal);
            Pieces = Array.AsReadOnly(list); Captured = Array.AsReadOnly((captured ?? Array.Empty<CapturedPiece>()).ToArray());
            Turn = turn; Ply = ply; Revision = revision; Outcome = outcome; Winner = winner;
            Hash = StableHash.Of(config.RuleHash + "|" + turn + "|" + ply + "|" + outcome + "|" + winner + "|" + string.Join(";", list.Select(p => p.Id.Length + ":" + p.Id + ":" + p.Kind + ":" + p.Side + ":" + p.Position)) + "|captured:" + string.Join(";", Captured.Select(c => c.Captor + ":" + c.Victim.Id.Length + ":" + c.Victim.Id + ":" + c.Victim.Kind + ":" + c.Victim.Side)));
        }
        public Piece? Find(string id) => byId.TryGetValue(id, out var p) ? p : null;
        public Piece? At(QN cell) => occupied.TryGetValue(cell, out var p) ? p : null;
        public IReadOnlyList<CapturedPiece> CapturedBy(Side side) => Array.AsReadOnly(Captured.Where(c => c.Captor == side && c.Victim.Kind != PieceKind.King).ToArray());
        public PreviewSession ToPreview(MatchConfig config) => new PreviewSession(config.Preset.Bounds, Pieces.Select(p => new TrialPiece(p.Id, MatchConfig.DefinitionId(p.Kind), p.Side, p.Position)));
    }
    public sealed class LegalMoveResult
    {
        public MatchQueryStatus Status { get; }
        public IReadOnlyList<Move> Moves { get; }
        public int EvaluatedCandidates { get; }
        public string Reason { get; }
        internal LegalMoveResult(MatchQueryStatus status, IEnumerable<Move> moves, int evaluated, string reason = "") { Status = status; Moves = Array.AsReadOnly(moves.OrderBy(m => m.Key, StringComparer.Ordinal).ToArray()); EvaluatedCandidates = evaluated; Reason = reason; }
    }
    public static class MatchRules
    {
        public static IReadOnlyList<Piece> InitialPieces() => InitialPieces(4);
        /// <summary>The flat shogi layout on the F×U plane, every further axis at 0, for a board of <paramref name="dimensions"/> axes.</summary>
        public static IReadOnlyList<Piece> InitialPieces(int dimensions)
        {
            // Explicit data conversion only; old GameState/CPU/asymmetric rules never enter a match.
            return Array.AsReadOnly(new RuleCore().InitialState().Pieces.Select(p => new Piece(p.Id, p.Side, p.Kind, QN.Padded(p.Position.ToArray(), dimensions))).ToArray());
        }
        public static QueryResult Query(MatchConfig config, MatchState state, string pieceId, CancellationToken cancellation = default, int budget = int.MaxValue)
            => PreviewQuery.Execute(config.Preset, state.ToPreview(config), pieceId, cancellation, budget);
        // Explicit mapping instead of matching enum names as strings: an unmapped value fails here, by name.
        private static MatchQueryStatus ToMatchStatus(QueryStatus status)
        {
            switch (status)
            {
                case QueryStatus.Complete: return MatchQueryStatus.Complete;
                case QueryStatus.Partial: return MatchQueryStatus.Partial;
                case QueryStatus.Cancelled: return MatchQueryStatus.Cancelled;
                case QueryStatus.Unsupported: return MatchQueryStatus.Unsupported;
                case QueryStatus.Invalid: return MatchQueryStatus.Invalid;
                default: throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
        public static LegalMoveResult QueryLegal(MatchConfig config, MatchState state, CancellationToken cancellation = default, int budget = int.MaxValue)
        {
            var moves = new List<Move>(); int evaluated = 0;
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (state.Outcome != MatchOutcome.Ongoing) return new LegalMoveResult(MatchQueryStatus.Invalid, moves, 0, "対局は終了しています。");
            try
            {
                var preview = state.ToPreview(config);
                foreach (var p in state.Pieces.Where(x => x.Side == state.Turn))
                {
                    if (cancellation.IsCancellationRequested) return new LegalMoveResult(MatchQueryStatus.Cancelled, moves, evaluated);
                    var q = PreviewQuery.Execute(config.Preset, preview, p.Id, cancellation, Math.Max(0, budget - evaluated)); evaluated += q.EvaluatedCandidates;
                    moves.AddRange(q.P.Select(to => new Move(p.Id, p.Position, to)));
                    if (q.Status != QueryStatus.Complete) return new LegalMoveResult(ToMatchStatus(q.Status), moves, evaluated, q.Reason);
                }
                return new LegalMoveResult(MatchQueryStatus.Complete, moves, evaluated, "王捕獲デモの着手可能手。自玉安全を制約する完全Lではありません。");
            }
            catch (Exception e) { return new LegalMoveResult(MatchQueryStatus.Faulted, moves, evaluated, e.Message); }
        }
    }
}
