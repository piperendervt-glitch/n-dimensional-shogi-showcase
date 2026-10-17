using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;

string evidence = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/cfg2-match-tests");
Directory.CreateDirectory(evidence);
string data = Path.Combine(evidence, "data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")); Directory.CreateDirectory(data);
var results = new List<object>(); int passed = 0, failed = 0, assertions = 0; var watch = Stopwatch.StartNew();
void Assert(bool ok, string reason = "assertion failed") { assertions++; if (!ok) throw new Exception(reason); }
void Near(double a, double b) => Assert(Math.Abs(a - b) < 1e-7, a + " != " + b);
void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, "expected rejection"); }
void Test(string name, Action body) { try { body(); passed++; results.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; results.Add(new { name, status = "FAIL", error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
MatchConfig Compile(SettingsDraft? draft = null, MatchOptionsRaw? options = null)
{
    Assert(MatchConfig.TryCreate(draft ?? SettingsDraft.CreateMatchDemo(), options ?? new MatchOptionsRaw(), out var config, out var report), string.Join(";", report.Errors)); return config!;
}
Piece P(string id, Side side, PieceKind kind, int f, int u = 0, int v = 0, int w = 0) => new Piece(id, side, kind, new QN(f, u, v, w));
Piece[] Kings() => new[] { P("AK", Side.A, PieceKind.King, -4, -4, -4, -4), P("BK", Side.B, PieceKind.King, 4, 4, 4, 4) };
MatchSession Fixture(IEnumerable<Piece> pieces, MatchMode mode = MatchMode.PvP, ManualClock? clock = null, MatchConfig? config = null, Side turn = Side.A) => MatchSession.CreateFixture(config ?? Compile(), mode, clock ?? new ManualClock(), pieces, "test/core-fixture/1", turn);
bool ApplyCpu(MatchSession s, out string reason) => s.TryApplyCpu(MatchCpu.Compute(s.BeginCpuRequest()), out reason);
void One(MatchSession s)
{
    if (s.CanCpuAct) Assert(ApplyCpu(s, out _)); else Assert(s.TryMove(s.QueryLegal().Moves.First(), out _));
    s.CompletePresentation();
}

Test("C2-02-normal40-explicit-conversion-snapshot-all-modes", () =>
{
    var config = Compile(); var legacy = new RuleCore().InitialState();
    foreach (MatchMode mode in Enum.GetValues(typeof(MatchMode)))
    {
        var s = MatchSession.Create(config, mode, new ManualClock()); Assert(s.State.Pieces.Count == 40 && s.State.Turn == Side.A && !s.IsFinished);
        Assert(s.State.Pieces.All(p => p.Position.V == 0 && p.Position.W == 0));
        Assert(s.State.Pieces.Select(p => p.Id + p.Side + p.Kind + p.Position).SequenceEqual(legacy.Pieces.Select(p => p.Id + p.Side + p.Kind + p.Position)));
        Assert(s.Snapshot.Config.RuleHash == config.RuleHash && s.Snapshot.InitialHash == s.State.Hash);
        Assert(s.Snapshot.SideA == (mode == MatchMode.CvC ? MatchController.Cpu : MatchController.Human));
        Assert(s.Snapshot.SideB == (mode == MatchMode.PvP ? MatchController.Human : MatchController.Cpu));
        One(s); One(s); Assert(s.State.Ply == 2); MatchRecordStore.Save(Path.Combine(data, "normal-" + mode + ".xml"), s);
    }
});
Test("C2-03-eight-kinds-both-sides-preview-match-same-independent-coordinate-sets", () =>
{
    var config = Compile();
    foreach (Side side in Enum.GetValues(typeof(Side))) foreach (PieceKind kind in Enum.GetValues(typeof(PieceKind)))
    {
        var pieces = Kings().Where(p => kind != PieceKind.King || p.Side != side).Concat(new[] { P("subject", side, kind, 0) }).ToArray();
        var s = Fixture(pieces, config: config, turn: side); var q = s.Query("subject"); var preview = PreviewQuery.Execute(config.Preset, s.ToPreview(), "subject");
        Assert(q.Status == QueryStatus.Complete && q.G.SequenceEqual(preview.G) && q.P.SequenceEqual(preview.P));
        var expected = new HashSet<QN>(); int sign = side == Side.A ? 1 : -1;
        for (int f = -4; f <= 4; f++) for (int u = -4; u <= 4; u++) for (int v = -4; v <= 4; v++) for (int w = -4; w <= 4; w++)
        {
            int[] d = { f, u, v, w }; int nz = d.Count(x => x != 0), transverse = d.Skip(1).Count(x => x != 0), max = d.Max(Math.Abs); bool allow = false;
            if (nz == 0) continue;
            if (kind == PieceKind.King) allow = max == 1;
            if (kind == PieceKind.Pawn) allow = max == 1 && nz == 1 && f * sign >= 0;
            if (kind == PieceKind.Rook) allow = nz == 1;
            if (kind == PieceKind.Bishop) allow = nz == 2 && d.Count(x => Math.Abs(x) == max) == 2;
            if (kind == PieceKind.Lance) allow = nz == 1 && f * sign > 0;
            if (kind == PieceKind.Knight) allow = f == 2 * sign && transverse == 1 && d.Skip(1).All(x => Math.Abs(x) <= 1);
            if (kind == PieceKind.Gold) allow = max == 1 && (f == sign && transverse <= 1 || f == 0 && transverse == 1 || f == -sign && transverse == 0);
            if (kind == PieceKind.Silver) allow = max == 1 && (f == sign && transverse <= 1 || f == -sign && transverse == 1);
            if (allow) expected.Add(new QN(f, u, v, w));
        }
        Assert(expected.SetEquals(q.G), kind + " " + side + " coordinate mismatch");
        Assert(s.QueryLegal().Moves.Where(m => m.PieceId == "subject").Select(m => m.To).ToHashSet().SetEquals(q.P));
        int count = kind == PieceKind.King ? 80 : kind == PieceKind.Pawn ? 7 : kind == PieceKind.Rook ? 32 : kind == PieceKind.Bishop ? 96 : kind == PieceKind.Gold ? 14 : kind == PieceKind.Silver ? 13 : kind == PieceKind.Knight ? 6 : 4;
        Assert(q.G.Count == count, kind + " count");
    }
});
Test("C2-03-B-W-axis-moves-and-knight-jumps-with-blocker", () =>
{
    var s = Fixture(Kings().Concat(new[] { P("B", Side.B, PieceKind.Knight, 0), P("block", Side.A, PieceKind.Pawn, -1) }), turn: Side.B);
    var target = new QN(-2, 0, 0, 1); Assert(s.Query("B").P.Contains(target)); Assert(s.TryMove(new Move("B", new QN(), target), out _));
    Assert(s.State.Find("B")!.Position.W == 1 && s.State.At(new QN(-1, 0, 0, 0))!.Id == "block");
});
Test("C2-03-lance-both-sides-blockers-two-occlusions", () =>
{
    foreach (string occlusion in new[] { "lattice-line/1", "supercover-closed/1" }) foreach (Side side in new[] { Side.A, Side.B })
    {
        var draft = SettingsDraft.CreateMatchDemo(); draft.Edit(d => d.Occlusion = occlusion); int sign = side == Side.A ? 1 : -1;
        var s = Fixture(Kings().Concat(new[] { P("lance", side, PieceKind.Lance, 0), P("block", RuleCore.Other(side), PieceKind.Pawn, 2 * sign) }), config: Compile(draft), turn: side);
        Assert(s.Query("lance").P.ToHashSet().SetEquals(new[] { new QN(sign, 0, 0, 0), new QN(2 * sign, 0, 0, 0) }));
        Assert(s.TryMove(new Move("lance", new QN(), new QN(2 * sign, 0, 0, 0)), out _)); Assert(s.State.CapturedBy(side).Single().Victim.Id == "block");
    }
});
Test("C2-03-edit-axis-k-pattern-distance-forward-capture-reflects-real-move", () =>
{
    var draft = SettingsDraft.CreateMatchDemo(); var pos = Kings().Where(p => p.Side != Side.A).Concat(new[] { P("subject", Side.A, PieceKind.King, 0) }).ToArray();
    Assert(Fixture(pos).TryMove(new Move("subject", new QN(), new QN(0, 0, 0, 1)), out _));
    draft.Edit(d => d.Definitions.Single(x => x.Id == "king-step").AxesMask = "7"); var noW = Fixture(pos, config: Compile(draft)); string before = noW.State.Hash;
    Assert(!noW.TryMove(new Move("subject", new QN(), new QN(0, 0, 0, 1)), out _) && noW.State.Hash == before);
    draft.Edit(d => { var x = d.Definitions.Single(v => v.Id == "king-step"); x.AxesMask = "15"; x.MaxAxes = "1"; }); Assert(Fixture(pos, config: Compile(draft)).Query("subject").G.Count == 8);
    draft.Edit(d => { var x = d.Definitions.Single(v => v.Id == "king-step"); x.Kind = "h11/1"; x.MaxDistance = "2"; x.Backward = false; x.Sideways = false; });
    var runner = Fixture(pos, config: Compile(draft)); Assert(runner.Query("subject").P.Count == 2); Assert(runner.TryMove(new Move("subject", new QN(), new QN(2, 0, 0, 0)), out _));
    draft.Edit(d => d.Definitions.Single(v => v.Id == "king-step").MaxDistance = "1"); Assert(!Fixture(pos, config: Compile(draft)).TryMove(new Move("subject", new QN(), new QN(2, 0, 0, 0)), out _));
    draft = SettingsDraft.CreateMatchDemo(); draft.Edit(d => d.Definitions.Single(v => v.Id == "king-step").AllowCapture = false);
    var adjacent = new[] { P("AK", Side.A, PieceKind.King, 0), P("BK", Side.B, PieceKind.King, 1) };
    Assert(!Fixture(adjacent, config: Compile(draft)).TryMove(new Move("AK", new QN(), new QN(1, 0, 0, 0)), out _));
    Assert(Fixture(adjacent).TryMove(new Move("AK", new QN(), new QN(1, 0, 0, 0)), out _));
});
Test("C2-03-H22-corner-occlusion-changes-actual-capture", () =>
{
    var pieces = Kings().Concat(new[] { P("bishop", Side.A, PieceKind.Bishop, 0), P("corner", Side.A, PieceKind.Pawn, 1), P("victim", Side.B, PieceKind.Pawn, 1, 1) }).ToArray();
    var lattice = Fixture(pieces); Assert(lattice.TryMove(new Move("bishop", new QN(), new QN(1, 1, 0, 0)), out _));
    var d = SettingsDraft.CreateMatchDemo(); d.Edit(x => x.Occlusion = "supercover-closed/1"); var closed = Fixture(pieces, config: Compile(d)); Assert(!closed.TryMove(new Move("bishop", new QN(), new QN(1, 1, 0, 0)), out _));
});
Test("C2-04-invalid-draft-unknown-missing-fixed-components-no-apply", () =>
{
    var old = SettingsDraft.CreateExperiment(); Assert(!MatchConfig.TryCreate(old, new MatchOptionsRaw(), out _, out _));
    foreach (Action<DraftData> mutate in new Action<DraftData>[] { d => d.Definitions[0].MaxAxes = "bad", d => d.Definitions.RemoveAll(x => x.Id == "pawn-step"), d => d.Definitions[0].Kind = "unknown/1", d => d.Definitions.Single(x => x.Id == "gold-legacy").Kind = "step/1", d => d.Definitions.Single(x => x.Id == "knight-legacy").AxesMask = "7", d => d.Min = "-3" })
    { var draft = SettingsDraft.CreateMatchDemo(); draft.Edit(mutate); string hash = draft.ContentHash; Assert(!MatchConfig.TryCreate(draft, new MatchOptionsRaw(), out _, out var report) && report.Errors.Count > 0 && draft.ContentHash == hash); }
    foreach (string invalid in new[] { "", "0", "-1", "NaN", "Infinity", "no" }) Assert(!MatchConfig.TryCreate(SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw { TimeSeconds = invalid }, out _, out _));
});
Test("C2-04-initial-kings-IDs-cells-side-kind-bounds-validation", () =>
{
    Reject(() => Fixture(Kings().Take(1))); Reject(() => Fixture(Kings().Concat(new[] { P("AK2", Side.A, PieceKind.King, 0) })));
    Reject(() => Fixture(Kings().Concat(new[] { P("AK", Side.A, PieceKind.Pawn, 0) })));
    Reject(() => Fixture(Kings().Concat(new[] { P("dup", Side.A, PieceKind.Pawn, -4, -4, -4, -4) })));
    Reject(() => Fixture(Kings().Concat(new[] { P("bad", (Side)8, PieceKind.Pawn, 0) })));
    Reject(() => Fixture(Kings().Concat(new[] { P("bad", Side.A, (PieceKind)99, 0) })));
    Reject(() => Fixture(Kings().Concat(new[] { P("bad", Side.A, PieceKind.Pawn, 5) })));
});
Test("C2-02-snapshot-clones-draft-options-and-freezes-current-match", () =>
{
    var draft = SettingsDraft.CreateMatchDemo(); var options = new MatchOptionsRaw(); var config = Compile(draft, options); var s = MatchSession.Create(config, MatchMode.PvP, new ManualClock());
    string snapshot = s.Snapshot.Hash, rule = config.RuleHash, state = s.State.Hash;
    draft.Edit(d => d.Definitions[0].MaxAxes = "1"); options.TimeSeconds = "1"; var leaked = config.Preset.ToDraftData(); leaked.Definitions[0].MaxAxes = "2";
    Assert(config.RuleHash == rule && s.State.Hash == state && s.Snapshot.Hash == snapshot && config.TimeSeconds == 600);
    Assert(Compile(draft, options).RuleHash != rule);
});
Test("C2-05-06-07-three-modes-normal-king-capture-handler-and-record", () =>
{
    foreach (MatchMode mode in Enum.GetValues(typeof(MatchMode)))
    {
        var s = Fixture(new[] { P("AK", Side.A, PieceKind.King, 0), P("BK", Side.B, PieceKind.King, 1) }, mode);
        if (s.CanCpuAct) Assert(ApplyCpu(s, out _)); else Assert(s.TryMove(new Move("AK", new QN(), new QN(1, 0, 0, 0)), out _));
        Assert(s.State.Outcome == MatchOutcome.KingCapture && s.State.Winner == Side.A && s.State.Ply == 1 && s.Phase == MatchPhase.Finished);
        Assert(s.State.CapturedBy(Side.A).Count == 0); Assert(s.Events.Last().Kind == "end"); MatchRecordStore.Save(Path.Combine(data, "terminal-" + mode + ".xml"), s);
        var next = MatchSession.Create(s.Config, mode, new ManualClock()); Assert(next.Snapshot.MatchId != s.Snapshot.MatchId && next.State.Ply == 0 && next.State.Pieces.Count == 40);
    }
});
Test("C2-08-full4D-hidden-attacks-obstruction-independent-preview", () =>
{
    var s = Fixture(Kings().Concat(new[] { P("rook", Side.B, PieceKind.Rook, 0, 0, 0, 4), P("target", Side.A, PieceKind.Pawn, 0, 0, 0, 2) }), turn: Side.B);
    string hash = s.State.Hash; var preview = s.ToPreview(); var target = new QN(0, 0, 0, 2);
    Assert(PreviewQuery.Attackers(s.Config.Preset, preview, target, Side.B).Attackers.Any(x => x.Id == "rook"));
    Assert(PreviewQuery.AttackCells(s.Config.Preset, preview, "rook").Cells.Contains(target));
    preview.Place(new TrialPiece("block", "pawn-step", Side.A, new QN(0, 0, 0, 3)));
    Assert(!PreviewQuery.AttackCells(s.Config.Preset, preview, "rook").Cells.Contains(target)); Assert(s.State.Hash == hash && s.State.Pieces.Count == 4);
    Assert(s.TryMove(new Move("rook", new QN(0, 0, 0, 4), target), out _));
});
Test("C2-09-no-moves-initial-and-after-commit-complete-only", () =>
{
    var draft = SettingsDraft.CreateMatchDemo(); draft.Edit(d => { var k = d.Definitions[0]; k.AllowEmpty = false; k.AllowCapture = false; });
    var none = Fixture(Kings(), config: Compile(draft)); Assert(none.State.Outcome == MatchOutcome.NoMovesDraw && none.State.Winner == null && none.State.Ply == 0);
    var next = Fixture(Kings().Concat(new[] { P("mover", Side.A, PieceKind.Pawn, 0) }), config: Compile(draft)); Assert(next.TryMove(new Move("mover", new QN(), new QN(1, 0, 0, 0)), out _)); Assert(next.State.Outcome == MatchOutcome.NoMovesDraw && next.State.Ply == 1);
});
Test("C2-09-partial-cancel-fault-never-zero-or-draw", () =>
{
    var s = MatchSession.Create(Compile(options: new MatchOptionsRaw { CpuBudget = "1" }), MatchMode.CvC, new ManualClock()); var result = MatchCpu.Compute(s.BeginCpuRequest());
    Assert(result.Status == MatchQueryStatus.Partial && !result.Move.HasValue); Assert(!s.TryApplyCpu(result, out _)); Assert(s.Phase == MatchPhase.ProcessingPaused && s.State.Outcome == MatchOutcome.Ongoing && s.State.Ply == 0);
    Assert(s.RetryProcessing()); using var cancel = new CancellationTokenSource(); cancel.Cancel(); var cancelled = MatchCpu.Compute(s.BeginCpuRequest(), cancel.Token);
    Assert(cancelled.Status == MatchQueryStatus.Cancelled && !s.TryApplyCpu(cancelled, out _)); Assert(s.State.Outcome == MatchOutcome.Ongoing);
    Assert(s.RetryProcessing()); s.SuspendProcessing("injected worker exception"); Assert(s.Phase == MatchPhase.ProcessingPaused && s.State.Outcome == MatchOutcome.Ongoing);
});
Test("C2-09-partial-reason-names-budget-and-evaluated-count", () =>
{
    var s = MatchSession.Create(Compile(options: new MatchOptionsRaw { CpuBudget = "1" }), MatchMode.CvC, new ManualClock()); var result = MatchCpu.Compute(s.BeginCpuRequest());
    Assert(!s.TryApplyCpu(result, out var reason)); Assert(reason.Contains("照会予算1件") && reason.Contains("照会済み" + result.EvaluatedCandidates + "件"), reason); Assert(s.FaultReason == reason);
});
Test("C2-09-retry-after-handover-fault-resumes-presentation-not-input", () =>
{
    var s = MatchSession.Create(Compile(), MatchMode.CvC, new ManualClock()); Assert(ApplyCpu(s, out _)); Assert(s.PresentationPaused);
    s.SuspendProcessing("injected next-turn query fault"); Assert(s.Phase == MatchPhase.ProcessingPaused);
    Assert(!s.PauseCvc() && s.Phase == MatchPhase.ProcessingPaused); Assert(!s.ResumeCvc());
    Assert(s.RetryProcessing()); Assert(s.Phase == MatchPhase.Presentation && !s.CanCpuAct && !s.Clock.Running && s.FaultReason == "");
    s.CompletePresentation(); Assert(s.Phase == MatchPhase.Active && s.CanCpuAct && s.State.Ply == 1);
    var idle = MatchSession.Create(Compile(), MatchMode.CvC, new ManualClock()); idle.SuspendProcessing("injected"); Assert(idle.RetryProcessing() && idle.Phase == MatchPhase.Active);
});
Test("C2-12-cpu-clock-hold-released-when-cpu-request-is-invalidated", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.CvC, clock); s.BeginCpuRequest(); s.CpuClockHeld = true; Assert(!s.Clock.Running);
    Assert(s.PauseCvc()); Assert(!s.CpuClockHeld); Assert(s.ResumeCvc()); clock.Advance(2); s.Tick(); Near(s.Clock.ASeconds, 598);
    s.CpuClockHeld = true; s.SuspendProcessing("injected"); Assert(!s.CpuClockHeld);
});
Test("C2-10-pick-revision-generation-double-commit-rejected", () =>
{
    var s = MatchSession.Create(Compile(), MatchMode.PvP, new ManualClock()); var move = s.QueryLegal().Moves.First(); long revision = s.Revision, generation = s.Generation;
    Assert(!s.TryMove(move, revision + 1, generation, out _) && s.State.Ply == 0); Assert(s.TryMove(move, revision, generation, out _));
    Assert(!s.TryMove(move, revision, generation, out _) && s.State.Ply == 1); s.CompletePresentation(); Assert(!s.TryMove(move, revision, generation, out _) && s.State.Ply == 1);
});
Test("C2-10-cpu-stale-pause-resume-new-match-end-duplicate", () =>
{
    var s = MatchSession.Create(Compile(), MatchMode.CvC, new ManualClock()); var old = MatchCpu.Compute(s.BeginCpuRequest()); string before = s.State.Hash;
    Assert(s.PauseCvc() && s.ResumeCvc()); Assert(s.State.Hash == before); Assert(!s.TryApplyCpu(old, out _) && s.State.Ply == 0);
    var fresh = MatchCpu.Compute(s.BeginCpuRequest()); Assert(s.TryApplyCpu(fresh, out _) && !s.TryApplyCpu(fresh, out _) && s.State.Ply == 1);
    s.CompletePresentation(); var late = MatchCpu.Compute(s.BeginCpuRequest()); s.Interrupt(); Assert(!s.TryApplyCpu(late, out _) && s.State.Outcome == MatchOutcome.Interrupted);
    var other = MatchSession.Create(s.Config, MatchMode.CvC, new ManualClock()); other.BeginCpuRequest(); Assert(!other.TryApplyCpu(old, out _) && other.State.Ply == 0);
});
Test("C2-11-both-capture-trays-owner-and-no-drop", () =>
{
    foreach (Side side in new[] { Side.A, Side.B })
    {
        var s = Fixture(Kings().Concat(new[] { P("rook", side, PieceKind.Rook, 0), P("victim", RuleCore.Other(side), PieceKind.Silver, 1) }), turn: side);
        Assert(s.TryMove(new Move("rook", new QN(), new QN(1, 0, 0, 0)), out _)); var captured = s.State.CapturedBy(side).Single(); Assert(captured.Captor == side && captured.Victim.Side == RuleCore.Other(side) && captured.Victim.Kind == PieceKind.Silver);
        Assert(s.State.CapturedBy(RuleCore.Other(side)).Count == 0 && s.State.Find("victim") == null); s.CompletePresentation(); Assert(!s.TryMove(new Move("victim", new QN(1, 0, 0, 0), new QN(2, 0, 0, 0)), out _));
    }
});
Test("C2-12-clock-switch-presentation-focus-no-frame-dependence", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.PvP, clock); clock.Advance(2); s.Tick(); Near(s.Clock.ASeconds, 598); Near(s.Clock.BSeconds, 600);
    Assert(s.TryMove(s.QueryLegal().Moves.First(), out _)); Assert(s.PresentationPaused && !s.Clock.Running && !s.CanHumanAct);
    clock.Advance(100); s.Tick(); Near(s.Clock.ASeconds, 598); Near(s.Clock.BSeconds, 600); s.CompletePresentation();
    clock.Advance(3); s.Tick(); Near(s.Clock.BSeconds, 597); Near(s.Clock.ASeconds, 598);
    // There is deliberately no focus-loss clock pause API: elapsed real time continues.
    clock.Advance(7); s.Tick(); Near(s.Clock.BSeconds, 590); for (int i = 0; i < 100; i++) s.Tick(); Near(s.Clock.BSeconds, 590);
});
Test("C2-12-cpu-clock-hold-charges-only-thinking-never-human", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.CvC, clock); clock.Advance(2); s.Tick(); Near(s.Clock.ASeconds, 598);
    s.CpuClockHeld = true; Assert(!s.Clock.Running); clock.Advance(40); s.Tick(); Near(s.Clock.ASeconds, 598); Assert(ApplyCpu(s, out _)); Near(s.Clock.ASeconds, 598);
    s.CompletePresentation(); s.CpuClockHeld = false; clock.Advance(1); s.Tick(); Near(s.Clock.BSeconds, 599); Near(s.Clock.ASeconds, 598);
    var h = MatchSession.Create(Compile(), MatchMode.PvP, clock); h.CpuClockHeld = true; clock.Advance(5); h.Tick(); Near(h.Clock.ASeconds, 595); Assert(h.Clock.Running);
});
Test("C2-12-zero-boundary-human-and-late-CPU-cannot-overturn", () =>
{
    foreach (MatchMode mode in new[] { MatchMode.PvP, MatchMode.CvC })
    {
        var clock = new ManualClock(); var s = MatchSession.Create(Compile(options: new MatchOptionsRaw { TimeSeconds = "0.1" }), mode, clock); var move = s.QueryLegal().Moves.First(); CpuResult? cpu = mode == MatchMode.CvC ? MatchCpu.Compute(s.BeginCpuRequest()) : null;
        clock.Advance(0.1); if (cpu != null) Assert(!s.TryApplyCpu(cpu, out _)); else Assert(!s.TryMove(move, out _));
        Assert(s.State.Outcome == MatchOutcome.TimeLoss && s.State.Winner == Side.B && s.State.Ply == 0); Near(s.Clock.ASeconds, 0); string hash = s.State.Hash;
        clock.Advance(200); s.Tick(); Assert(s.State.Hash == hash); Near(s.Clock.BSeconds, 0.1); Reject(() => s.BeginCpuRequest());
    }
});
Test("C2-12-CvC-pause-stops-both-step-exactly-one-and-repause", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.CvC, clock); clock.Advance(2); Assert(s.PauseCvc()); clock.Advance(50); s.Tick(); Near(s.Clock.ASeconds, 598); Near(s.Clock.BSeconds, 600);
    Assert(s.StepCvc()); clock.Advance(1); Assert(ApplyCpu(s, out _)); Assert(s.State.Ply == 1 && s.PresentationPaused); clock.Advance(20); s.CompletePresentation(); Assert(s.Phase == MatchPhase.CvcPaused);
    Near(s.Clock.ASeconds, 597); Near(s.Clock.BSeconds, 600); clock.Advance(30); s.Tick(); Near(s.Clock.BSeconds, 600); Assert(!s.CanCpuAct); Assert(s.ResumeCvc()); One(s); Assert(s.State.Ply == 2);
});
Test("C2-12-nonmonotonic-source-rejected-no-clock-rewind", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.PvP, clock); clock.Advance(3); s.Tick(); clock.Value = 1; Reject(s.Tick); Near(s.Clock.ASeconds, 597); clock.Value = double.NaN; Reject(s.Tick); Near(s.Clock.ASeconds, 597);
});
Test("C2-12-clock-deadline-rechecked-after-move-validation", () =>
{
    var clock = new AdvancingClock(); var s = MatchSession.CreateFixture(Compile(options: new MatchOptionsRaw { TimeSeconds = "1" }), MatchMode.PvP, clock,
        new[] { P("AK", Side.A, PieceKind.King, 0), P("BK", Side.B, PieceKind.King, 1) }, "test/commit-deadline/1");
    clock.Step = 0.6;
    Assert(!s.TryMove(new Move("AK", new QN(), new QN(1, 0, 0, 0)), out _));
    Assert(s.State.Outcome == MatchOutcome.TimeLoss && s.State.Winner == Side.B && s.State.Ply == 0 && s.State.Find("BK") != null);
});
Test("C2-12-pause-resume-during-presentation-never-reopens-input-early", () =>
{
    var s = MatchSession.Create(Compile(), MatchMode.CvC, new ManualClock()); Assert(ApplyCpu(s, out _)); Assert(s.PresentationPaused);
    Assert(s.PauseCvc() && s.ResumeCvc()); Assert(s.PresentationPaused && !s.CanCpuAct && !s.Clock.Running);
    s.CompletePresentation(); Assert(s.CanCpuAct); Assert(ApplyCpu(s, out _)); Assert(s.PauseCvc());
    s.CompletePresentation(); Assert(s.IsPaused && !s.CanCpuAct); Assert(s.ResumeCvc() && s.CanCpuAct);
});
Test("C2-09-interrupt-is-neither-win-nor-draw-and-finished-frozen", () =>
{
    var clock = new ManualClock(); var s = MatchSession.Create(Compile(), MatchMode.PvP, clock); clock.Advance(4); s.Interrupt("test safe stop; not natural terminal");
    Assert(s.State.Outcome == MatchOutcome.Interrupted && s.State.Winner == null); string hash = s.State.Hash; clock.Advance(100); s.Tick(); s.CompletePresentation(); s.Interrupt(); Assert(hash == s.State.Hash && s.State.Ply == 0); Near(s.Clock.ASeconds, 596);
});
Test("C2-06-07-cpu-stable-seed-capture-score-and-common-validation", () =>
{
    var pieces = Kings().Concat(new[] { P("rook", Side.A, PieceKind.Rook, 0), P("gold", Side.B, PieceKind.Gold, 1), P("pawn", Side.B, PieceKind.Pawn, 0, 1) });
    var a = Fixture(pieces, MatchMode.CvC); var b = Fixture(pieces, MatchMode.CvC); var ra = MatchCpu.Compute(a.BeginCpuRequest()); var rb = MatchCpu.Compute(b.BeginCpuRequest());
    Assert(ra.Move.HasValue && rb.Move.HasValue && ra.Move.Value.Equals(rb.Move.Value)); Assert(ra.Move.GetValueOrDefault().To == new QN(1, 0, 0, 0)); Assert(a.TryApplyCpu(ra, out _));
    Assert(a.State.CapturedBy(Side.A).Single().Victim.Kind == PieceKind.Gold && a.State.Ply == 1);
});
Test("C2-16-valid-settings-roundtrip-start-all-modes-without-CFG1-mutation", () =>
{
    var draft = SettingsDraft.CreateMatchDemo(); draft.Edit(d => { d.Occlusion = "supercover-closed/1"; d.Definitions[1].MaxDistance = "2"; }); var options = new MatchOptionsRaw { TimeSeconds = "125.5", CpuSeed = "-71" }; string path = Path.Combine(data, "next-match.xml");
    var config = Compile(draft, options); MatchSettingsStore.SaveValid(path, draft, options); Assert(!draft.IsDirty); var loaded = MatchSettingsStore.Load(path);
    Assert(loaded.IsValidSettings && loaded.Config!.SettingsHash == config.SettingsHash && loaded.Options.TimeSeconds == "125.5");
    foreach (MatchMode mode in Enum.GetValues(typeof(MatchMode))) Assert(MatchSession.Create(loaded.Config!, mode, new ManualClock()).Snapshot.Config.SettingsHash == config.SettingsHash);
});
Test("C2-04-16-invalid-raw-separate-from-last-valid-and-cross-kind-rejected", () =>
{
    var draft = SettingsDraft.CreateMatchDemo(); var options = new MatchOptionsRaw(); string good = Path.Combine(data, "good.xml"), raw = Path.Combine(data, "raw.xml"); MatchSettingsStore.SaveValid(good, draft, options); string bytes = File.ReadAllText(good);
    draft.Edit(d => d.Definitions[0].MaxAxes = "12x"); options.TimeSeconds = "生入力"; Reject(() => MatchSettingsStore.SaveValid(good, draft, options)); MatchSettingsStore.SaveDraft(raw, draft, options);
    var loaded = MatchSettingsStore.Load(raw); Assert(!loaded.IsValidSettings && loaded.Config == null && loaded.Draft.Current.Definitions[0].MaxAxes == "12x" && loaded.Options.TimeSeconds == "生入力"); Assert(File.ReadAllText(good) == bytes);
    Reject(() => MatchSettingsStore.SaveDraft(good, draft, options)); Reject(() => MatchSettingsStore.SaveValid(raw, SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw())); Assert(File.ReadAllText(good) == bytes);
});
Test("C2-04-16-atomic-faults-preserve-old-normal-and-raw-dirty", () =>
{
    string path = Path.Combine(data, "fault.xml"); var draft = SettingsDraft.CreateMatchDemo(); var options = new MatchOptionsRaw(); MatchSettingsStore.SaveValid(path, draft, options); string before = File.ReadAllText(path); draft.Edit(d => d.Name = "changed");
    foreach (SaveStage stage in Enum.GetValues(typeof(SaveStage))) { Reject(() => MatchSettingsStore.SaveValid(path, draft, options, actual => { if (actual == stage) throw new IOException("injected " + stage); })); Assert(File.ReadAllText(path) == before && draft.IsDirty); }
    string fresh = Path.Combine(data, "never-written.xml"); Reject(() => MatchSettingsStore.SaveValid(fresh, draft, options, _ => throw new IOException("first save"))); Assert(!File.Exists(fresh)); Assert(!Directory.EnumerateFiles(data, ".cfg2-*.tmp").Any());
});
Test("C2-04-unknown-schema-fields-definitions-DTD-rejected-no-byte-change", () =>
{
    string source = Path.Combine(data, "strict-source.xml"); MatchSettingsStore.SaveValid(source, SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw()); string text = File.ReadAllText(source);
    var changes = new[] { text.Replace("schema=\"1\"", "schema=\"9\""), text.Replace("schema=\"1\"", "schema=\"1\" future=\"x\""), text.Replace("h11/1", "unknown/1"), text.Replace("</settings-bundle>", "<future /></settings-bundle>"), "<!DOCTYPE settings-bundle [<!ENTITY test SYSTEM 'file:///not-used'>]>" + text };
    for (int i = 0; i < changes.Length; i++) { string bad = Path.Combine(data, "bad-" + i + ".xml"); File.WriteAllText(bad, changes[i]); Reject(() => MatchSettingsStore.Load(bad)); Assert(File.ReadAllText(bad) == changes[i]); }
});
Test("C2-09-record-full-snapshot-move-captor-clock-hashes-and-atomic-failure", () =>
{
    var clock = new ManualClock(); var s = Fixture(new[] { P("AK", Side.A, PieceKind.King, 0), P("BK", Side.B, PieceKind.King, 1) }, clock: clock); clock.Advance(2.5); string before = s.State.Hash;
    Assert(s.TryMove(new Move("AK", new QN(), new QN(1, 0, 0, 0)), out _)); string file = Path.Combine(data, "journal.xml"); MatchRecordStore.Save(file, s); var xml = XDocument.Load(file); var n = xml.Root!.Name.Namespace;
    Assert((string?)xml.Root.Attribute("initialHash") == before && (string?)xml.Root.Attribute("currentHash") == s.State.Hash && (string?)xml.Root.Attribute("ruleHash") == s.Config.RuleHash);
    var move = xml.Descendants(n + "event").Single(e => (string?)e.Attribute("kind") == "move"); Assert((string?)move.Attribute("before") == before && (string?)move.Attribute("after") == s.State.Hash); Near(double.Parse((string)move.Attribute("aSeconds")!, CultureInfo.InvariantCulture), 597.5);
    Assert((string?)move.Element(n + "capture")!.Attribute("captor") == "A" && (string?)move.Element(n + "capture")!.Attribute("tray") == "false");
    Assert(xml.Descendants(MatchSettingsNamespace() + "definition").Count() == s.Config.Preset.Definitions.Count);
    string bytes = File.ReadAllText(file); Reject(() => MatchRecordStore.Save(file, s, stage => { if (stage == SaveStage.BeforeReplace) throw new IOException("record fault"); })); Assert(File.ReadAllText(file) == bytes);
});
XNamespace MatchSettingsNamespace() => "urn:ndshogi:cfg2:settings/1";
Test("C2-17-CFG1-legacy-unsupported-fixture-and-original-game-unchanged", () =>
{
    var old = SettingsDraft.CreateExperiment(); Assert(old.TryCompile(out var oldPreset, out _)); Assert(!oldPreset!.Find("gold-legacy")!.IsSupported && !oldPreset.CanApply && !old.CanApply);
    var legacy = new RuleCore().InitialState(); string before = legacy.Hash; var current = MatchSession.Create(Compile(), MatchMode.CvC, new ManualClock()); One(current); One(current); Assert(legacy.Hash == before && legacy.Pieces.All(p => p.Side != Side.B || p.Position.W == 0));
});

SettingsDraft DemoN(int n)
{
    var draft = SettingsDraft.CreateMatchDemo();
    draft.Edit(d => { d.Dimensions = n.ToString(CultureInfo.InvariantCulture); foreach (var x in d.Definitions) { x.AxesMask = BoundsN.FullAxesMask(n).ToString(CultureInfo.InvariantCulture); if (x.MaxAxes == "4") x.MaxAxes = n.ToString(CultureInfo.InvariantCulture); } });
    return draft;
}
string StableRecord(MatchSession s) => StableHash.Of(System.Text.RegularExpressions.Regex.Replace(MatchRecordStore.Serialize(s), "id=\"[^\"]*\"|startedUtc=\"[^\"]*\"|utc=\"[^\"]*\"|monotonic=\"[^\"]*\"|snapshotHash=\"[^\"]*\"", ""));

Test("ND-01-four-dimensional-hashes-text-and-legal-moves-are-unchanged", () =>
{
    // Values recorded from the pure 4D code (main d34759b) before the N-dimensional generalization.
    var config = Compile();
    Assert(config.RuleHash == "1747423614e8828afa1a174bf42574d4f479ca2fecccc91b00149b7b89fa8b9f", "rule hash");
    Assert(config.SettingsHash == "82cfba190dea3990131cac669c0b316ccea0a9d5cc3882fd96b8a2328e4ae86f", "settings hash");
    Assert(config.Preset.RuleHash == "5ec3680da0f5d5b038caadb537ba5ff5c91ef68f2a9c1a83624cfdb412d21d09", "preset hash");
    Assert(config.Dimensions == 4 && config.EffectiveRuleId == MatchConfig.RuleId);
    var s = MatchSession.Create(config, MatchMode.PvP, new ManualClock());
    Assert(s.State.Hash == "30f4b78b72b97677ac93e8b7b8d2c49a9cc5b6758a5edb6f2336118bcecb11de", "initial state hash");
    var legal = MatchRules.QueryLegal(config, s.State); Assert(legal.Moves.Count == 217 && legal.Moves[0].Key == "A-Bishop:-3,3,0,0>-1,3,-2,0");
    Assert(new RuleCore().InitialState().Hash == "68b1ef1c7742d934f392aeeea45b669fed3a3fd9e361e43c00d9815e5d14786a", "legacy game hash");
    Assert(StableRecord(s) == "91b6c6f49f45c7ed94d2b434c6eb6f0ded739d3b00a44f886f72b034fd597d5f", "record text");
    Assert(new QN(1, -2, 3, -4).ToString() == "1,-2,3,-4" && new QN() == new QN(0, 0, 0, 0) && new QN(1, 0, 0, 0) == new QN(1, 0, 0, 0, 0) && new QN(1, 0, 0, 0).GetHashCode() == new QN(1, 0, 0, 0, 0).GetHashCode());
    Assert(new QN(0, 0, 0, 1) != new QN(0, 0, 0, 0, 1) && new QN(0, 0, 0, 1).Rank == 4 && new QN(0, 0, 0, 0, 1).Rank == 5);
    Assert(QN.TryParse("1,2,3,4", 4, out var parsed) && parsed == new QN(1, 2, 3, 4) && !QN.TryParse("1,2,3", 4, out _) && !QN.TryParse("1,2,3,4,5,6,7,8", 0, out _) && !QN.TryParse("1,2,３,4", 4, out _));
});

Test("ND-02-five-dimensional-match-compiles-starts-moves-along-the-fifth-axis-and-saves", () =>
{
    var config = Compile(DemoN(5)); Assert(config.Dimensions == 5 && config.EffectiveRuleId == MatchConfig.RuleIdND && config.RuleHash != Compile().RuleHash);
    var s = MatchSession.Create(config, MatchMode.PvP, new ManualClock());
    Assert(s.State.Pieces.Count == 40 && s.State.Pieces.All(p => p.Position.Dimensions == 5 && p.Position.ToString().Count(c => c == ',') == 4));
    var legal = s.QueryLegal(); Assert(legal.Status == MatchQueryStatus.Complete && legal.Moves.Count > 217, "more room: " + legal.Moves.Count);
    var pawn = s.State.Find("A-Pawn-0")!; var to = pawn.Position + new QN(0, 0, 0, 0, 1);
    Assert(legal.Moves.Any(m => m.PieceId == pawn.Id && m.To == to)); Assert(s.TryMove(new Move(pawn.Id, pawn.Position, to), out var why), why);
    Assert(s.State.Find("A-Pawn-0")!.Position.Axis(4) == 1 && s.State.Turn == Side.B);
    string xml = MatchRecordStore.Serialize(s); Assert(xml.Contains("dimensions=\"5\"") && xml.Contains("ruleId=\"CFG2_DEMO_ND\"") && xml.Contains("to=\"-2,0,0,0,1\"") && xml.Contains("cell=\"-2,0,0,0,0\""));
    string file = Path.Combine(data, "nd-settings.xml"); MatchSettingsStore.SaveValid(file, DemoN(5), new MatchOptionsRaw());
    var loaded = MatchSettingsStore.Load(file); Assert(loaded.IsValidSettings && loaded.Config!.Dimensions == 5 && loaded.Config.RuleHash == config.RuleHash);
    foreach (int n in new[] { 3, 6, 7 }) { var c = Compile(DemoN(n)); var m = MatchSession.Create(c, MatchMode.CvC, new ManualClock()); Assert(m.State.Pieces.All(p => p.Position.Dimensions == n)); One(m); One(m); Assert(m.State.Ply == 2); }
    foreach (int n in new[] { 2, 8 }) { var bad = DemoN(n); Assert(!MatchConfig.TryCreate(bad, new MatchOptionsRaw(), out _, out var report) && report.Errors.Any(e => e.Contains("N")), "N=" + n + " must be rejected"); }
    var wrongMask = DemoN(5); wrongMask.Edit(d => d.Definitions.Single(x => x.Id == "gold-legacy").AxesMask = "15"); Assert(!MatchConfig.TryCreate(wrongMask, new MatchOptionsRaw(), out _, out _), "fixed pieces must use every axis");
});

Test("ND-03-lower-dimensional-cells-are-accepted-and-normalized-to-the-board", () =>
{
    var config = Compile(DemoN(5)); var s = Fixture(Kings(), config: config);
    Assert(s.State.Pieces.All(p => p.Position.Dimensions == 5) && s.State.At(new QN(-4, -4, -4, -4)) != null && s.State.At(new QN(-4, -4, -4, -4, 0))!.Id == "AK");
    Assert(s.TryMove(new Move("AK", new QN(-4, -4, -4, -4), new QN(-3, -4, -4, -4)), out var why), why); Assert(s.State.Find("AK")!.Position == new QN(-3, -4, -4, -4, 0));
    Reject(() => Fixture(Kings().Concat(new[] { new Piece("far", Side.A, PieceKind.Pawn, new QN(0, 0, 0, 0, 0, 1)) }), config: config));
    var three = Compile(DemoN(3)); Reject(() => Fixture(Kings(), config: three)); Assert(Fixture(new[] { P("AK", Side.A, PieceKind.King, -4, -4, -4), P("BK", Side.B, PieceKind.King, 4, 4, 4) }, config: three).State.Pieces.All(p => p.Position.Dimensions == 3));
});

watch.Stop(); var report = new { status = failed == 0 ? "PASS" : "FAIL", passed, failed, assertions, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, tests = results, evidenceType = "Pure C#; no Unity/OS/human input", dataRoot = data };
File.WriteAllText(Path.Combine(evidence, "tests.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} PASS, {failed} FAIL, {assertions} assertions"); Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class ManualClock : IMonotonicClock { public double Value; public double Seconds => Value; public void Advance(double seconds) => Value += seconds; }
sealed class AdvancingClock : IMonotonicClock { private double value; public double Step; public double Seconds { get { value += Step; return value; } } }
