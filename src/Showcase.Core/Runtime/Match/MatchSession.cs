using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace NDShogi.Showcase.Core.Match
{
    public sealed class MatchSnapshot
    {
        public string MatchId { get; } = Guid.NewGuid().ToString("N");
        public MatchConfig Config { get; }
        public MatchMode Mode { get; }
        public MatchController SideA { get; }
        public MatchController SideB { get; }
        public string FixtureId { get; }
        public MatchState InitialState { get; }
        public string InitialHash => InitialState.Hash;
        public string Hash { get; }
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        internal MatchSnapshot(MatchConfig config, MatchMode mode, MatchState initial, string fixtureId)
        {
            if (!Enum.IsDefined(typeof(MatchMode), mode)) throw new ArgumentException("Unknown match mode.");
            Config = config; Mode = mode; InitialState = initial; FixtureId = fixtureId;
            SideA = mode == MatchMode.CvC ? MatchController.Cpu : MatchController.Human;
            SideB = mode == MatchMode.PvP ? MatchController.Human : MatchController.Cpu;
            Hash = StableHash.Of(config.SettingsHash + "|" + mode + "|" + SideA + "|" + SideB + "|" + initial.Hash + "|" + fixtureId);
        }
        public MatchController Controller(Side side) => side == Side.A ? SideA : SideB;
    }
    public sealed class MatchClockView
    {
        public double ASeconds { get; }
        public double BSeconds { get; }
        public bool Running { get; }
        public Side ActiveSide { get; }
        internal MatchClockView(double a, double b, bool running, Side side) { ASeconds = a; BSeconds = b; Running = running; ActiveSide = side; }
    }
    public sealed class MatchEvent
    {
        public string Kind { get; }
        public int Ply { get; }
        public Move? Move { get; }
        public CapturedPiece? Capture { get; }
        public string BeforeHash { get; }
        public string AfterHash { get; }
        public double MonotonicSeconds { get; }
        public DateTime Utc { get; } = DateTime.UtcNow;
        public double ASeconds { get; }
        public double BSeconds { get; }
        public MatchOutcome Outcome { get; }
        public Side? Winner { get; }
        public long Generation { get; }
        public string Detail { get; }
        internal MatchEvent(string kind, MatchSession session, string before, Move? move = null, CapturedPiece? capture = null, string detail = "")
        {
            Kind = kind; Ply = session.State.Ply; Move = move; Capture = capture; BeforeHash = before; AfterHash = session.State.Hash;
            MonotonicSeconds = session.LastTickSeconds; ASeconds = session.Clock.ASeconds; BSeconds = session.Clock.BSeconds;
            Outcome = session.State.Outcome; Winner = session.State.Winner; Generation = session.Generation; Detail = detail;
        }
    }
    public readonly struct CpuStamp : IEquatable<CpuStamp>
    {
        public string MatchId { get; }
        public long Revision { get; }
        public Side Turn { get; }
        public string RuleHash { get; }
        public long Generation { get; }
        public CpuStamp(string matchId, long revision, Side turn, string ruleHash, long generation) { MatchId = matchId; Revision = revision; Turn = turn; RuleHash = ruleHash; Generation = generation; }
        public bool Equals(CpuStamp other) => MatchId == other.MatchId && Revision == other.Revision && Turn == other.Turn && RuleHash == other.RuleHash && Generation == other.Generation;
        public override bool Equals(object? obj) => obj is CpuStamp s && Equals(s);
        public override int GetHashCode() => (MatchId ?? "").GetHashCode() ^ Revision.GetHashCode() ^ Generation.GetHashCode();
    }
    public sealed class CpuRequest
    {
        public CpuStamp Stamp { get; }
        public MatchConfig Config { get; }
        public MatchState State { get; }
        internal CpuRequest(CpuStamp stamp, MatchConfig config, MatchState state) { Stamp = stamp; Config = config; State = state; }
    }
    public sealed class CpuResult
    {
        public CpuStamp Stamp { get; }
        public MatchQueryStatus Status { get; }
        public Move? Move { get; }
        public int CandidateCount { get; }
        public int EvaluatedCandidates { get; }
        public string Reason { get; }
        internal CpuResult(CpuStamp stamp, MatchQueryStatus status, Move? move, int candidates, int evaluated, string reason) { Stamp = stamp; Status = status; Move = move; CandidateCount = candidates; EvaluatedCandidates = evaluated; Reason = reason; }
    }
    public static class MatchCpu
    {
        private static int Value(PieceKind kind) => kind == PieceKind.King ? 100000 : kind == PieceKind.Rook ? 1000 : kind == PieceKind.Bishop ? 900 : kind == PieceKind.Gold ? 600 : kind == PieceKind.Silver ? 500 : kind == PieceKind.Knight ? 400 : kind == PieceKind.Lance ? 300 : 100;
        public static CpuResult Compute(CpuRequest request, CancellationToken cancellation = default)
        {
            try
            {
                var result = MatchRules.QueryLegal(request.Config, request.State, cancellation, request.Config.CpuBudget);
                if (result.Status != MatchQueryStatus.Complete) return new CpuResult(request.Stamp, result.Status, null, result.Moves.Count, result.EvaluatedCandidates, result.Reason);
                if (cancellation.IsCancellationRequested) return new CpuResult(request.Stamp, MatchQueryStatus.Cancelled, null, result.Moves.Count, result.EvaluatedCandidates, "CPU取消");
                var moves = result.Moves.OrderByDescending(m => request.State.At(m.To) is Piece victim ? Value(victim.Kind) : 0)
                    .ThenBy(m => StableHash.Tie(m.Key, request.Config.CpuSeed)).ThenBy(m => m.Key, StringComparer.Ordinal).ToArray();
                return new CpuResult(request.Stamp, MatchQueryStatus.Complete, moves.Length == 0 ? (Move?)null : moves[0], moves.Length, result.EvaluatedCandidates, "共通全4D候補・捕獲評価・seed付き安定同点処理");
            }
            catch (Exception e) { return new CpuResult(request.Stamp, MatchQueryStatus.Faulted, null, 0, 0, e.Message); }
        }
    }
    // Mutated on the UI thread only. Workers receive immutable CpuRequest snapshots.
    public sealed class MatchSession
    {
        private readonly IMonotonicClock time;
        private double aSeconds, bSeconds;
        private readonly List<MatchEvent> events = new List<MatchEvent>();
        private CpuStamp? pending;
        private bool stopAfterOne;
        private bool pauseAfterPresentation;
        private bool presentationPending;
        public MatchSnapshot Snapshot { get; }
        public MatchConfig Config => Snapshot.Config;
        public MatchState State { get; private set; }
        public MatchPhase Phase { get; private set; }
        public long Revision => State.Revision;
        public long Generation { get; private set; }
        public double LastTickSeconds { get; private set; }
        public bool PresentationPaused => Phase == MatchPhase.Presentation;
        public bool IsPaused => Phase == MatchPhase.CvcPaused || Phase == MatchPhase.ProcessingPaused;
        public bool HasCpuRequest => pending.HasValue;
        public bool IsFinished => State.Outcome != MatchOutcome.Ongoing;
        public bool CanHumanAct => Phase == MatchPhase.Active && !IsFinished && Snapshot.Controller(State.Turn) == MatchController.Human;
        public bool CanCpuAct => Phase == MatchPhase.Active && !IsFinished && Snapshot.Controller(State.Turn) == MatchController.Cpu;
        public string FaultReason { get; private set; } = "";
        // The UI holds a CPU's clock while it shows a computed move or waits out the display delay;
        // only real thinking time is charged. A human's clock is never held.
        public bool CpuClockHeld { get; set; }
        private bool ClockHeldNow => CpuClockHeld && Snapshot.Controller(State.Turn) == MatchController.Cpu;
        public MatchClockView Clock => new MatchClockView(aSeconds, bSeconds, Phase == MatchPhase.Active && !IsFinished && !ClockHeldNow, State.Turn);
        public IReadOnlyList<MatchEvent> Events => events.AsReadOnly();
        private MatchSession(MatchConfig config, MatchMode mode, IMonotonicClock clock, IEnumerable<Piece> pieces, string fixture, Side turn)
        {
            time = clock ?? throw new ArgumentNullException(nameof(clock));
            State = new MatchState(pieces, turn, 0, 0, config);
            if (State.Pieces.Count(p => p.Side == Side.A && p.Kind == PieceKind.King) != 1 || State.Pieces.Count(p => p.Side == Side.B && p.Kind == PieceKind.King) != 1) throw new ArgumentException("開始配置には両陣営の王が各1枚必要です。");
            Snapshot = new MatchSnapshot(config, mode, State, fixture); aSeconds = bSeconds = config.TimeSeconds; LastTickSeconds = ReadTime(); Phase = MatchPhase.Active;
            events.Add(new MatchEvent("start", this, State.Hash, detail: config.SettingsHash));
            var first = QueryLegal();
            if (first.Status != MatchQueryStatus.Complete) SuspendProcessing("開始照会が未完了：" + first.Reason);
            else if (first.Moves.Count == 0) End(MatchOutcome.NoMovesDraw, null, "開始手番の完了済み着手候補が0件");
        }
        public static MatchSession Create(MatchConfig config, MatchMode mode, IMonotonicClock clock) => new MatchSession(config, mode, clock, MatchRules.InitialPieces(), "normal-initial-40/1", Side.A);
        public static MatchSession CreateFixture(MatchConfig config, MatchMode mode, IMonotonicClock clock, IEnumerable<Piece> pieces, string fixtureId, Side turn = Side.A)
        {
            if (string.IsNullOrWhiteSpace(fixtureId) || fixtureId == "normal-initial-40/1" || fixtureId.Any(char.IsControl)) throw new ArgumentException("検証配置には通常初期配置と異なる明示fixture IDが必要です。");
            return new MatchSession(config, mode, clock, pieces, fixtureId, turn);
        }
        private double ReadTime()
        {
            double now = time.Seconds;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < LastTickSeconds) throw new InvalidOperationException("時刻源が有限・単調ではありません。時計を巻き戻しません。");
            return now;
        }
        public void Tick()
        {
            if (IsFinished) return;
            double now = ReadTime(), elapsed = now - LastTickSeconds; LastTickSeconds = now;
            if (Phase != MatchPhase.Active || ClockHeldNow) return;
            if (State.Turn == Side.A) aSeconds = Math.Max(0, aSeconds - elapsed); else bSeconds = Math.Max(0, bSeconds - elapsed);
            if ((State.Turn == Side.A ? aSeconds : bSeconds) <= 0) End(MatchOutcome.TimeLoss, RuleCore.Other(State.Turn), "持ち時間0：単調時計");
        }
        // A held CPU clock belongs to the request being presented; any invalidation (commit, presentation end,
        // pause, retry) releases it so the hold can never carry over to the next CPU turn.
        private void InvalidateCpu() { Generation++; pending = null; CpuClockHeld = false; }
        private void End(MatchOutcome outcome, Side? winner, string detail)
        {
            if (IsFinished) return; string before = State.Hash;
            State = new MatchState(State.Pieces, State.Turn, State.Ply, State.Revision + 1, Config, State.Captured, outcome, winner);
            Phase = MatchPhase.Finished; InvalidateCpu(); stopAfterOne = false; pauseAfterPresentation = false;
            presentationPending = false;
            events.Add(new MatchEvent("end", this, before, detail: detail));
        }
        public Settings.PreviewSession ToPreview() => State.ToPreview(Config);
        public Settings.QueryResult Query(string pieceId, CancellationToken cancellation = default, int budget = int.MaxValue) => MatchRules.Query(Config, State, pieceId, cancellation, budget);
        public LegalMoveResult QueryLegal(CancellationToken cancellation = default, int budget = int.MaxValue) => MatchRules.QueryLegal(Config, State, cancellation, budget);
        public bool TryMove(Move move, out string reason) => TryMove(move, Revision, Generation, out reason);
        public bool TryMove(Move move, long expectedRevision, long expectedGeneration, out string reason)
        {
            Tick();
            if (!CanHumanAct || expectedRevision != Revision || expectedGeneration != Generation) { reason = "手番・入力状態・版が変わりました。再選択してください。"; return false; }
            return CommitMove(move, "Human", out reason);
        }
        private bool CommitMove(Move move, string actor, out string reason)
        {
            var mover = State.Find(move.PieceId);
            if (mover == null || mover.Side != State.Turn || mover.Position != move.From) { reason = "移動元/手番が一致しません。"; return false; }
            var q = Query(move.PieceId);
            if (q.Status != Settings.QueryStatus.Complete || !q.P.Contains(move.To)) { reason = "共通Pの着手候補ではありません。"; return false; }
            // Validation itself consumes real thinking time. Recheck the deadline at commit,
            // before a king capture could otherwise overtake an expired clock.
            Tick();
            if (Phase != MatchPhase.Active || IsFinished) { reason = "着手確定前に時計または入力状態が変わりました。"; return false; }
            string before = State.Hash; var victim = State.At(move.To); var capture = victim == null ? null : new CapturedPiece(mover.Side, victim, State.Ply + 1);
            var pieces = State.Pieces.Where(p => p.Id != mover.Id && p.Id != victim?.Id).Concat(new[] { mover.At(move.To) });
            var captured = capture == null ? State.Captured : State.Captured.Concat(new[] { capture });
            bool king = victim?.Kind == PieceKind.King;
            State = new MatchState(pieces, RuleCore.Other(State.Turn), State.Ply + 1, State.Revision + 1, Config, captured, king ? MatchOutcome.KingCapture : MatchOutcome.Ongoing, king ? mover.Side : (Side?)null);
            InvalidateCpu();
            Phase = king ? MatchPhase.Finished : MatchPhase.Presentation;
            presentationPending = !king;
            pauseAfterPresentation = stopAfterOne; stopAfterOne = false;
            events.Add(new MatchEvent("move", this, before, move, capture, actor));
            if (king) events.Add(new MatchEvent("end", this, State.Hash, detail: "王捕獲"));
            else
            {
                var legal = QueryLegal();
                if (legal.Status == MatchQueryStatus.Complete && legal.Moves.Count == 0) End(MatchOutcome.NoMovesDraw, null, "次手番の完了済み着手候補が0件");
                else if (legal.Status != MatchQueryStatus.Complete) SuspendProcessing("次手番照会未完了：" + legal.Reason);
            }
            reason = "着手確定：王捕獲デモ（完全Lではありません）。"; return true;
        }
        public void CompletePresentation()
        {
            if (!presentationPending || IsFinished || (Phase != MatchPhase.Presentation && Phase != MatchPhase.CvcPaused)) return; Tick();
            bool stayPaused = Phase == MatchPhase.CvcPaused || pauseAfterPresentation;
            presentationPending = false;
            Phase = stayPaused ? MatchPhase.CvcPaused : MatchPhase.Active; pauseAfterPresentation = false; InvalidateCpu();
            events.Add(new MatchEvent("presentation-complete", this, State.Hash, detail: Phase.ToString()));
        }
        public bool PauseCvc()
        {
            // A processing fault is left only through RetryProcessing, which re-runs the failed query.
            Tick(); if (Snapshot.Mode != MatchMode.CvC || IsFinished || Phase == MatchPhase.ProcessingPaused) return false;
            Phase = MatchPhase.CvcPaused; stopAfterOne = false; pauseAfterPresentation = false; InvalidateCpu(); events.Add(new MatchEvent("cvc-pause", this, State.Hash)); return true;
        }
        public bool ResumeCvc()
        {
            Tick(); if (Snapshot.Mode != MatchMode.CvC || Phase != MatchPhase.CvcPaused || IsFinished) return false;
            Phase = presentationPending ? MatchPhase.Presentation : MatchPhase.Active; pauseAfterPresentation = false; stopAfterOne = false; InvalidateCpu(); events.Add(new MatchEvent("cvc-resume", this, State.Hash)); return true;
        }
        public bool StepCvc()
        {
            if (!ResumeCvc()) return false; stopAfterOne = true; events.Add(new MatchEvent("cvc-step", this, State.Hash, detail: "one commit then pause; thinking clock runs until commit")); return true;
        }
        public void SuspendProcessing(string reason)
        {
            Tick(); if (IsFinished) return; Phase = MatchPhase.ProcessingPaused; FaultReason = reason; InvalidateCpu(); events.Add(new MatchEvent("processing-paused", this, State.Hash, detail: reason));
        }
        public bool RetryProcessing()
        {
            Tick(); if (Phase != MatchPhase.ProcessingPaused || IsFinished) return false; FaultReason = ""; InvalidateCpu(); events.Add(new MatchEvent("processing-retry", this, State.Hash));
            if (!presentationPending) { Phase = MatchPhase.Active; return true; }
            // The fault came from the next-turn query after a commit: run it again and resume the hand-over it interrupted.
            Phase = MatchPhase.Presentation;
            var legal = QueryLegal();
            if (legal.Status == MatchQueryStatus.Complete && legal.Moves.Count == 0) End(MatchOutcome.NoMovesDraw, null, "次手番の完了済み着手候補が0件");
            else if (legal.Status != MatchQueryStatus.Complete) SuspendProcessing("次手番照会未完了：" + legal.Reason);
            return true;
        }
        public void Interrupt(string reason = "メニュー復帰") { Tick(); if (!IsFinished) End(MatchOutcome.Interrupted, null, reason); }
        public CpuRequest BeginCpuRequest()
        {
            Tick(); if (!CanCpuAct || pending.HasValue) throw new InvalidOperationException("CPU要求を開始できない状態です。");
            InvalidateCpu(); var stamp = new CpuStamp(Snapshot.MatchId, Revision, State.Turn, Config.RuleHash, Generation); pending = stamp;
            events.Add(new MatchEvent("cpu-request", this, State.Hash, detail: MatchConfig.CpuId + ";seed=" + Config.CpuSeed + ";budget=" + Config.CpuBudget));
            return new CpuRequest(stamp, Config, State);
        }
        public bool TryApplyCpu(CpuResult result, out string reason)
        {
            Tick();
            // pending was stamped from this session's current match, revision, turn, rules and generation, so equality covers them all.
            if (!CanCpuAct || !pending.HasValue || !pending.Value.Equals(result.Stamp))
            { reason = "古い/終了後/取消後のCPU応答を拒否しました。"; return false; }
            if (result.Status == MatchQueryStatus.Partial)
            {
                // Retrying recomputes the same partial result, so say what has to change.
                reason = "CPU未完了：照会予算" + Config.CpuBudget.ToString(CultureInfo.InvariantCulture) + "件では全候補を照会できません（照会済み" + result.EvaluatedCandidates.ToString(CultureInfo.InvariantCulture) + "件）。オプションのCPU設定で照会予算を増やし、次の対局を始めてください。";
                SuspendProcessing(reason); return false;
            }
            if (result.Status != MatchQueryStatus.Complete) { reason = "CPU未完了：" + result.Status + " " + result.Reason; SuspendProcessing(reason); return false; }
            if (!result.Move.HasValue)
            {
                var check = QueryLegal();
                if (check.Status == MatchQueryStatus.Complete && check.Moves.Count == 0) { End(MatchOutcome.NoMovesDraw, null, "全件完了した着手候補0件"); reason = "手なし引分"; return true; }
                reason = "CPU結果に手がありません。引分にはしません。"; SuspendProcessing(reason); return false;
            }
            if (!CommitMove(result.Move.Value, "Cpu;" + MatchConfig.CpuId + ";seed=" + Config.CpuSeed, out reason)) { SuspendProcessing("CPU不正結果：" + reason); return false; }
            return true;
        }
    }
}
