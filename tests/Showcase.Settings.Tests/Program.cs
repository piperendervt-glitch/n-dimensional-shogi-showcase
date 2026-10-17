using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;

var pivotChildExit = PivotStorageTests.RunChild(args);
if (pivotChildExit.HasValue) { Environment.ExitCode = pivotChildExit.Value; return; }

string evidence = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/cfg1-settings-tests");
Directory.CreateDirectory(evidence);
string dataRoot = Path.Combine(evidence, "data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff"));
Directory.CreateDirectory(dataRoot);
var results = new List<object>(); int passed = 0, failed = 0;
var duration = Stopwatch.StartNew();
void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, "fault not rejected"); }
void Test(string name, Action body) { var watch = Stopwatch.StartNew(); try { body(); passed++; results.Add(new { name, status = "PASS", milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; results.Add(new { name, status = "FAIL", error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e.Message); } }
SettingsDraft Fixture(int k = 4, string kind = "step/1", int distance = 1)
{
    var data = new DraftData { Name = "F1-F4 original: N4 0..4", SourceId = "SW2-F1-F4-original/0.2", Min = "0", Max = "4" };
    data.Definitions.Add(new RawPieceDefinition { Id = "m", Name = "数学fixture（製品ルールではない）", Kind = kind, MaxAxes = k.ToString(), MaxDistance = distance.ToString() });
    data.Definitions.Add(new RawPieceDefinition { Id = "rook", Name = "F4飛", Kind = "h11/1", MaxDistance = "4" });
    return new SettingsDraft(data);
}
PreviewPreset Compile(SettingsDraft draft) { Assert(draft.TryCompile(out var preset, out var report), string.Join(";", report.Errors)); return preset!; }
var center = new QN(2, 2, 2, 2); var zero = new QN(0, 0, 0, 0);
PreviewSession Position(params TrialPiece[] other) => new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("subject", "m", Side.A, center) }.Concat(other));
HashSet<QN> IndependentStep(QN source, int k, int mask = 15)
{
    var expected = new HashSet<QN>();
    for (int x = 0; x <= 4; x++) for (int y = 0; y <= 4; y++) for (int z = 0; z <= 4; z++) for (int w = 0; w <= 4; w++)
    {
        var target = new QN(x, y, z, w); int changed = 0; bool allowed = true;
        for (int i = 0; i < 4; i++) { int d = target[i] - source[i]; if (d != 0) changed++; if (Math.Abs(d) > 1 || (d != 0 && (mask & (1 << i)) == 0)) allowed = false; }
        if (allowed && changed > 0 && changed <= k) expected.Add(target);
    }
    return expected;
}

Test("F1-original-80-32-8-full-coordinate-sets-and-26-18-6-slice", () =>
{
    foreach (var pair in new[] { (4, 80, 26), (2, 32, 18), (1, 8, 6) })
    {
        var result = PreviewQuery.Execute(Compile(Fixture(pair.Item1)), Position(), "subject");
        Assert(result.Status == QueryStatus.Complete && result.TheoreticalCount == pair.Item2);
        Assert(result.G.Count == pair.Item2 && result.P.Count == pair.Item2);
        Assert(IndependentStep(center, pair.Item1).SetEquals(result.G));
        Assert(result.G.Count(q => q.W == 2) == pair.Item3);
        Assert(result.G.Count(q => q.V == 2 && q.W == 2) == (pair.Item1 == 1 ? 4 : 8));
        Assert(result.LStatus.Contains("未評価"));
    }
});
Test("F2-original-disabled-W-preserves-N-and-other-pieces", () =>
{
    var draft = Fixture(); draft.Edit(d => d.Definitions[0].AxesMask = "7");
    var session = Position(new TrialPiece("off", "m", Side.B, new QN(0, 0, 0, 4))); string before = session.Hash;
    var result = PreviewQuery.Execute(Compile(draft), session, "subject");
    Assert(result.G.Count == 26 && IndependentStep(center, 4, 7).SetEquals(result.G));
    Assert(session.Hash == before && session.Find("off")!.Position.W == 4 && draft.Current.Dimensions == "4");
    draft.TryCompile(out _, out var report); Assert(report.Warnings.Any(x => x.Contains("非") || x.Contains("対称性")));
});
Test("F3-original-occupancy-and-capture-79-78", () =>
{
    var session = Position(new TrialPiece("friend", "m", Side.A, new QN(3, 2, 2, 2)), new TrialPiece("enemy", "m", Side.B, new QN(2, 3, 2, 2)));
    var draft = Fixture(); var withCapture = PreviewQuery.Execute(Compile(draft), session, "subject");
    Assert(withCapture.G.Count == 80 && withCapture.P.Count == 79 && !withCapture.P.Contains(new QN(3, 2, 2, 2)));
    draft.Edit(d => d.Definitions[0].AllowCapture = false); var emptyOnly = PreviewQuery.Execute(Compile(draft), session, "subject"); Assert(emptyOnly.P.Count == 78);
    draft.Edit(d => { d.Definitions[0].AllowCapture = true; d.Definitions[0].AllowEmpty = false; }); Assert(PreviewQuery.Execute(Compile(draft), session, "subject").P.SequenceEqual(new[] { new QN(2, 3, 2, 2) }));
    Reject(() => session.Place(new TrialPiece("overlap", "m", Side.A, center)));
});
Test("F4-original-hidden-B-rook-W4-attacks-king-W2", () =>
{
    var session = Position(new TrialPiece("enemy-rook", "rook", Side.B, new QN(2, 2, 2, 4)), new TrialPiece("enemy-king", "m", Side.B, zero));
    var preset = Compile(Fixture()); string before = session.Hash;
    var attack = PreviewQuery.Attackers(preset, session, center, Side.B); Assert(attack.Status == QueryStatus.Complete && attack.Attackers.Single().Id == "enemy-rook");
    Assert(attack.Meaning.Contains("王捕獲許可") && session.Hash == before);
    Assert(PreviewQuery.Execute(preset, session, "enemy-rook").G.Any(q => q.W == 3));
    session.Place(new TrialPiece("hidden-blocker", "m", Side.A, new QN(2, 2, 2, 3))); Assert(PreviewQuery.Attackers(preset, session, center, Side.B).Attackers.Count == 0);
});
Test("all-four-forward-axes-both-sides-and-sideways", () =>
{
    for (int axis = 0; axis < 4; axis++) foreach (Side side in new[] { Side.A, Side.B })
    {
        var draft = Fixture(); draft.Edit(d => { d.ForwardAxis = axis.ToString(); d.Definitions[0].Backward = false; d.Definitions[0].Sideways = false; });
        var session = new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("subject", "m", side, center) });
        var result = PreviewQuery.Execute(Compile(draft), session, "subject"); Assert(result.G.Count == 27 && result.G.All(q => q[axis] - center[axis] == (side == Side.A ? 1 : -1)));
        draft.Edit(d => { d.Definitions[0].Forward = false; d.Definitions[0].Sideways = true; }); Assert(PreviewQuery.Execute(Compile(draft), session, "subject").G.Count == 26);
    }
});
Test("step-board-edge-and-distance-two-rejected", () =>
{
    var draft = Fixture(); var session = new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("subject", "m", Side.A, zero) });
    var result = PreviewQuery.Execute(Compile(draft), session, "subject"); Assert(result.TheoreticalCount == 80 && result.G.Count == 15 && IndependentStep(zero, 4).SetEquals(result.G));
    draft.Edit(d => d.Definitions[0].MaxDistance = "2"); Assert(!draft.TryCompile(out _, out _));
});
Test("H11-H22-distance-and-board-edge-independent-sets", () =>
{
    foreach (string kind in new[] { "h11/1", "h22/1" }) foreach (int max in new[] { 1, 2, 8 })
    {
        var preset = Compile(Fixture(4, kind, max)); var result = PreviewQuery.Execute(preset, Position(), "subject");
        var expected = new HashSet<QN>();
        for (int x = 0; x <= 4; x++) for (int y = 0; y <= 4; y++) for (int z = 0; z <= 4; z++) for (int w = 0; w <= 4; w++)
        {
            var q = new QN(x, y, z, w); var components = Enumerable.Range(0, 4).Select(i => Math.Abs(q[i] - 2)).Where(v => v != 0).ToArray();
            if (components.Length == (kind == "h11/1" ? 1 : 2) && components.Distinct().Count() == 1 && components.Max() <= max) expected.Add(q);
        }
        Assert(expected.SetEquals(result.G) && expected.SetEquals(result.P)); Assert(result.TheoreticalCount == (kind == "h11/1" ? 8 : 24) * max);
    }
});
Test("both-occlusion-versions-H11-H22-middle-endpoint-and-corner", () =>
{
    foreach (string mode in new[] { "lattice-line/1", "supercover-closed/1" }) foreach (string kind in new[] { "h11/1", "h22/1" })
    {
        var draft = Fixture(4, kind, 4); draft.Edit(d => d.Occlusion = mode); var preset = Compile(draft);
        var start = zero; var target = kind == "h11/1" ? new QN(2, 0, 0, 0) : new QN(2, 2, 0, 0); var middle = kind == "h11/1" ? new QN(1, 0, 0, 0) : new QN(1, 1, 0, 0);
        var session = new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("subject", "m", Side.A, start), new TrialPiece("block", "m", Side.B, middle) });
        Assert(!PreviewQuery.Execute(preset, session, "subject").P.Contains(target));
        session.Erase(middle); session.Place(new TrialPiece("target", "m", Side.B, target)); Assert(PreviewQuery.Execute(preset, session, "subject").P.Contains(target), "endpoint is capturable, not a path blocker");
        if (kind == "h22/1") { session.Place(new TrialPiece("corner", "m", Side.B, new QN(1, 0, 0, 0))); Assert(PreviewQuery.Execute(preset, session, "subject").P.Contains(target) == (mode == "lattice-line/1")); }
        Assert(preset.OcclusionId == mode);
    }
});
Test("one-step-checks-endpoint-only-not-supercover-corner", () =>
{
    var draft = Fixture(); draft.Edit(d => d.Occlusion = "supercover-closed/1");
    var session = new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("subject", "m", Side.A, zero), new TrialPiece("corner", "m", Side.A, new QN(1, 0, 0, 0)) });
    Assert(PreviewQuery.Execute(Compile(draft), session, "subject").P.Contains(new QN(1, 1, 0, 0)));
});
Test("preview-move-capture-erase-clear-undo-redo-and-legacy-isolation", () =>
{
    var legacy = new RuleCore().InitialState(); string legacyHash = legacy.Hash; var draft = Fixture(); string rule = Compile(draft).RuleHash; var session = Position(new TrialPiece("enemy", "m", Side.B, new QN(2, 3, 2, 2))); string before = session.Hash;
    Assert(session.TryMove(Compile(draft), "subject", new QN(2, 3, 2, 2), out _)); Assert(session.Pieces.Count == 1 && session.Find("enemy") == null); Assert(session.Undo() && session.Hash == before); Assert(session.Redo() && session.Pieces.Count == 1);
    session.Clear(); Assert(session.Pieces.Count == 0 && session.Undo() && session.Pieces.Count == 1);
    Assert(session.Erase(session.Pieces[0].Position) && session.Undo());
    Assert(legacy.Hash == legacyHash && Compile(draft).RuleHash == rule && legacy.Pieces.Count == 40);
    Assert(!session.TryMove(Compile(draft), "subject", new QN(4, 4, 4, 4), out _));
});
Test("selection-definition-preserves-position-obstacles-and-draft", () =>
{
    var draft = Fixture(); var session = Position(new TrialPiece("off", "m", Side.B, zero)); string content = draft.ContentHash;
    session.ReplaceDefinition("subject", "rook"); Assert(session.Find("subject")!.Position == center && session.Find("off")!.Position == zero && draft.ContentHash == content); Assert(session.Undo() && session.Find("subject")!.DefinitionId == "m");
});
Test("raw-edit-history-clone-and-non-rule-name-hash", () =>
{
    var draft = Fixture(); draft.MarkSaved(); string initial = draft.ContentHash, rule = Compile(draft).RuleHash; var detached = draft.Current; detached.Definitions.Clear(); Assert(draft.Current.Definitions.Count == 2);
    draft.Edit(d => d.Name = "別の表示名"); Assert(draft.IsDirty && Compile(draft).RuleHash == rule && draft.IsUnapplied && !draft.CanApply); Assert(draft.Undo() && draft.ContentHash == initial && !draft.IsDirty); Assert(draft.Redo());
    DraftData? leaked = null; draft.Edit(d => { d.Name = "別名2"; leaked = d; }); string protectedHash = draft.ContentHash; leaked!.Name = "callback-after-edit"; Assert(draft.ContentHash == protectedHash);
    var clone = draft.Clone(); clone.Edit(d => d.Definitions[0].MaxAxes = "2"); Assert(draft.Current.Definitions[0].MaxAxes == "4" && Compile(clone).RuleHash != rule);
});
Test("unknown-and-unsupported-never-substitute-or-claim-L", () =>
{
    var draft = SettingsDraft.CreateExperiment(); var preset = Compile(draft); var session = PreviewSession.CreateExperiment();
    session.ReplaceDefinition("subject", "knight-legacy"); var result = PreviewQuery.Execute(preset, session, "subject"); Assert(result.Status == QueryStatus.Unsupported && result.TheoreticalCount == null && result.LStatus.Contains("未評価"));
    draft.Edit(d => d.Definitions[0].Kind = "h23/1"); Assert(!draft.TryCompile(out _, out var report) && report.Errors.Any(x => x.Contains("未対応")));
    draft.Edit(d => d.Definitions[0].Kind = "legacy/unknown/999"); Assert(!draft.TryCompile(out _, out _));
    Assert(!preset.CanApply && PreviewPreset.ApplyUnavailableReason.Contains("CFG-2"));
});
Test("cancel-partial-and-out-of-order-stamps", () =>
{
    var draft = Fixture(); var preset = Compile(draft); var session = Position();
    var one = new QueryStamp(draft.SessionId, 1, session.Revision, "subject", 1); var two = new QueryStamp(draft.SessionId, 2, session.Revision, "subject", 2);
    var gate = new LatestQueryGate(); gate.Begin(one); var old = PreviewQuery.Execute(preset, session, "subject", stamp: one); gate.Begin(two); var latest = PreviewQuery.Execute(preset, session, "subject", stamp: two);
    Assert(gate.TryAccept(latest) && !gate.TryAccept(old) && ReferenceEquals(gate.Latest, latest)); gate.Invalidate(); Assert(!gate.TryAccept(latest) && gate.Latest == null);
    var partial = PreviewQuery.Execute(preset, session, "subject", candidateBudget: 7); Assert(partial.Status == QueryStatus.Partial && partial.G.Count == 7 && partial.TheoreticalCount == null);
    using var cts = new CancellationTokenSource(); cts.Cancel(); var cancelled = PreviewQuery.Execute(preset, session, "subject", cts.Token); Assert(cancelled.Status == QueryStatus.Cancelled && cancelled.TheoreticalCount == null && cancelled.LStatus.Contains("未評価"));
});
Test("query-stamp-session-trial-request-piece-all-contribute", () =>
{
    var stamp = new QueryStamp("s", 1, 2, "p", 3);
    foreach (var wrong in new[] { new QueryStamp("other", 1, 2, "p", 3), new QueryStamp("s", 2, 2, "p", 3), new QueryStamp("s", 1, 3, "p", 3), new QueryStamp("s", 1, 2, "q", 3), new QueryStamp("s", 1, 2, "p", 4) }) Assert(!stamp.Equals(wrong));
});
Test("save-valid-preset-reload-separate-trial-view-and-no-apply", () =>
{
    string path = Path.Combine(dataRoot, "valid.xml"); var draft = Fixture(); var session = Position(new TrialPiece("hidden", "m", Side.B, new QN(0, 0, 0, 4)));
    var view = new ViewSnapshot(new double[] { 1, 2, 3, 4 }, ViewSnapshot.Default.Basis, new double[] { 20, 30, 11, 4, 5, 6 });
    SettingsStore.SavePreset(path, draft, session, view); Assert(!draft.IsDirty && draft.IsUnapplied);
    var loaded = SettingsStore.Load(path); Assert(loaded.Kind == SavedSettingsKind.PreviewPreset && loaded.Draft.ContentHash == draft.ContentHash && loaded.Preview.Hash == session.Hash && loaded.View.Center.SequenceEqual(view.Center));
    Assert(Compile(loaded.Draft).RuleHash == Compile(draft).RuleHash && !loaded.Draft.CanApply);
    loaded.Preview.Clear(); loaded.Draft.Edit(d => d.Definitions[0].MaxAxes = "1"); Assert(session.Pieces.Count == 2 && draft.Current.Definitions[0].MaxAxes == "4");
});
Test("initial-layout-attribute-is-optional-and-flat-files-are-unchanged", () =>
{
    string flat = Path.Combine(dataRoot, "layout-flat.xml"); var draft = Fixture(); SettingsStore.SavePreset(flat, draft, Position(), ViewSnapshot.Default);
    Assert(!File.ReadAllText(flat).Contains("initialLayout") && SettingsStore.Load(flat).Draft.Current.InitialLayout == DraftData.FlatLayout);
    string copy = Path.Combine(dataRoot, "layout-copy.xml"); var copied = Fixture(); copied.Edit(d => d.InitialLayout = DraftData.CopyLayout(2));
    Assert(copied.ContentHash != draft.ContentHash && Compile(copied).RuleHash != Compile(draft).RuleHash && Compile(copied).LayoutCopyAxis == 2);
    SettingsStore.SavePreset(copy, copied, Position(), ViewSnapshot.Default); var loaded = SettingsStore.Load(copy);
    Assert(File.ReadAllText(copy).Contains("initialLayout=\"copy/2\"") && loaded.Draft.Current.InitialLayout == "copy/2" && loaded.Draft.ContentHash == copied.ContentHash);
    string other = Path.Combine(dataRoot, "layout-unknown.xml"); File.WriteAllText(other, File.ReadAllText(copy).Replace("initialLayout=", "futureLayout=")); Reject(() => SettingsStore.Load(other));
});
Test("save-as-new-ID-preserves-original-and-does-not-overwrite", () =>
{
    string original = Path.Combine(dataRoot, "original.xml"), other = Path.Combine(dataRoot, "other.xml"); var draft = Fixture(); var session = Position();
    SettingsStore.SavePreset(original, draft, session, ViewSnapshot.Default); string oldId = draft.Id; byte[] oldBytes = File.ReadAllBytes(original);
    SettingsStore.SavePreset(other, draft, session, ViewSnapshot.Default, true); Assert(draft.Id != oldId && SettingsStore.Load(other).Draft.Id == draft.Id && File.ReadAllBytes(original).SequenceEqual(oldBytes));
    Reject(() => SettingsStore.SavePreset(other, draft, session, ViewSnapshot.Default, true));
});
Test("invalid-raw-draft-roundtrip-without-clamping-or-preset-overwrite", () =>
{
    string path = Path.Combine(dataRoot, "raw-draft.xml"), valid = Path.Combine(dataRoot, "protected-preset.xml"); var draft = Fixture(); var session = Position();
    SettingsStore.SavePreset(valid, draft, session, ViewSnapshot.Default); byte[] before = File.ReadAllBytes(valid);
    draft.Edit(d => { d.Definitions[0].MaxAxes = "not-a-number <未確定>"; d.Definitions[0].Kind = "future/general-H"; });
    SettingsStore.SaveDraft(path, draft, session, ViewSnapshot.Default); var loaded = SettingsStore.Load(path);
    Assert(loaded.Kind == SavedSettingsKind.Draft && loaded.Draft.Current.Definitions[0].MaxAxes == "not-a-number <未確定>" && loaded.Draft.Current.Definitions[0].Kind == "future/general-H" && !loaded.Validation.IsValid);
    Reject(() => SettingsStore.SavePreset(valid, draft, session, ViewSnapshot.Default)); Reject(() => SettingsStore.SaveDraft(valid, draft, session, ViewSnapshot.Default)); Assert(before.SequenceEqual(File.ReadAllBytes(valid)));
});
Test("invalid-draft-missing-reference-is-preserved-with-diagnostic", () =>
{
    var draft = Fixture(); draft.Edit(d => d.Definitions[0].Id = "renamed-unresolved"); var session = Position(); string path = Path.Combine(dataRoot, "unresolved-draft.xml");
    SettingsStore.SaveDraft(path, draft, session, ViewSnapshot.Default); var loaded = SettingsStore.Load(path);
    Assert(loaded.Draft.Current.Definitions[0].Id == "renamed-unresolved" && loaded.Preview.Find("subject")!.DefinitionId == "m" && !loaded.Validation.IsValid);
    Assert(PreviewQuery.Execute(Compile(loaded.Draft), loaded.Preview, "subject").Status == QueryStatus.Invalid);
});
Test("atomic-faults-preserve-old-bytes-and-unsaved-draft", () =>
{
    foreach (SaveStage stage in Enum.GetValues<SaveStage>())
    {
        string path = Path.Combine(dataRoot, "fault-" + stage + ".xml"); var draft = Fixture(); var session = Position(); SettingsStore.SavePreset(path, draft, session, ViewSnapshot.Default); byte[] before = File.ReadAllBytes(path);
        draft.Edit(d => d.Definitions[0].MaxAxes = "2"); string edited = draft.ContentHash;
        Reject(() => SettingsStore.SavePreset(path, draft, session, ViewSnapshot.Default, fault: at => { if (at == stage) throw new IOException("injected " + stage); }));
        Assert(File.ReadAllBytes(path).SequenceEqual(before) && draft.ContentHash == edited && draft.IsDirty);
        SettingsStore.SavePreset(path, draft, session, ViewSnapshot.Default); Assert(!draft.IsDirty && SettingsStore.Load(path).Draft.Current.Definitions[0].MaxAxes == "2");
    }
    Assert(!Directory.EnumerateFiles(dataRoot, ".cfg1-*.tmp").Any());
});
Test("failure-before-first-save-never-creates-destination", () =>
{
    string path = Path.Combine(dataRoot, "no-first.xml"); var draft = Fixture(); Reject(() => SettingsStore.SavePreset(path, draft, Position(), ViewSnapshot.Default, fault: at => { if (at == SaveStage.BeforeReplace) throw new IOException("injected"); })); Assert(!File.Exists(path) && draft.IsDirty);
});
Test("unknown-schema-attributes-elements-DTD-and-invalid-preset-rejected", () =>
{
    string source = Path.Combine(dataRoot, "strict-source.xml"); SettingsStore.SavePreset(source, Fixture(), Position(), ViewSnapshot.Default); string text = File.ReadAllText(source);
    var mutations = new[] { text.Replace("schema=\"1\"", "schema=\"999\""), text.Replace("schema=\"1\"", "schema=\"1\" executable=\"plugin.dll\""), text.Replace("</bundle>", "<future /></bundle>"), "<!DOCTYPE bundle [<!ENTITY x SYSTEM 'file:///unavailable'>]>" + text, text.Replace("maxAxes=\"4\"", "maxAxes=\"NaN\""), text.Replace("kind=\"step/1\"", "kind=\"code/dll\"") };
    for (int i = 0; i < mutations.Length; i++) { string path = Path.Combine(dataRoot, "bad-" + i + ".xml"); File.WriteAllText(path, mutations[i]); byte[] before = File.ReadAllBytes(path); Reject(() => SettingsStore.Load(path)); Assert(before.SequenceEqual(File.ReadAllBytes(path))); }
});
Test("load-bounds-reference-duplicate-cell-and-nonfinite-view-rejected", () =>
{
    string source = Path.Combine(dataRoot, "strict-more.xml"); SettingsStore.SavePreset(source, Fixture(), Position(), ViewSnapshot.Default); string text = File.ReadAllText(source);
    var mutations = new[] { text.Replace("cell=\"2,2,2,2\"", "cell=\"5,2,2,2\""), text.Replace("definition=\"m\"", "definition=\"missing\""), text.Replace("center=\"0,0,0,0\"", "center=\"NaN,0,0,0\""), text.Replace("side=\"A\"", "side=\"99\"") };
    for (int i = 0; i < mutations.Length; i++) { string path = Path.Combine(dataRoot, "bad-more-" + i + ".xml"); File.WriteAllText(path, mutations[i]); Reject(() => SettingsStore.Load(path)); }
    Reject(() => new PreviewSession(new BoundsN(0, 4), new[] { new TrialPiece("a", "m", Side.A, center), new TrialPiece("b", "m", Side.B, center) }));
    Reject(() => new ViewSnapshot(new double[4], new double[16], ViewSnapshot.Default.Camera));
});
Test("bounded-declarative-file-and-no-all-cell-state-array", () =>
{
    string path = Path.Combine(dataRoot, "oversize.xml"); File.WriteAllText(path, new string('x', 1024 * 1024 + 1)); Reject(() => SettingsStore.Load(path));
    Assert(typeof(PreviewSession).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).All(f => !f.FieldType.IsArray || f.FieldType.GetArrayRank() == 1));
    var session = new PreviewSession(); Assert(session.Pieces.Count == 0);
});
Test("bounded-load-query-measurement-256-sparse-pieces", () =>
{
    var draft = Fixture(4, "h22/1", 8); var pieces = new List<TrialPiece> { new TrialPiece("subject", "m", Side.A, center) };
    for (int x = 0; x < 5 && pieces.Count < 256; x++) for (int y = 0; y < 5 && pieces.Count < 256; y++) for (int z = 0; z < 5 && pieces.Count < 256; z++) for (int w = 0; w < 5 && pieces.Count < 256; w++) { var q = new QN(x, y, z, w); if (q != center) pieces.Add(new TrialPiece("b" + pieces.Count, "m", Side.B, q)); }
    var session = new PreviewSession(new BoundsN(0, 4), pieces); var preset = Compile(draft); long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew(); var result = PreviewQuery.Execute(preset, session, "subject"); watch.Stop();
    Assert(result.Status == QueryStatus.Complete && result.TheoreticalCount == 192 && session.Pieces.Count == 256);
    File.WriteAllText(Path.Combine(evidence, "performance.json"), JsonSerializer.Serialize(new { fixture = "N4 0..4 H22 max8 256 sparse pieces", milliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before, processWorkingSetBytes = Environment.WorkingSet, candidateCount = result.G.Count, noProductThresholdAdopted = true }, new JsonSerializerOptions { WriteIndented = true }));
});

PivotStorageTests.Run(dataRoot, evidence, Test, Assert, Reject);
duration.Stop();
File.WriteAllText(Path.Combine(evidence, "tests.json"), JsonSerializer.Serialize(new { status = failed == 0 ? "PASS" : "FAIL", passed, failed, elapsedMilliseconds = duration.Elapsed.TotalMilliseconds, fixtureScope = "F1-F4 original bounds 0..4; UI experiment uses separately named -4..4", dataRoot, tests = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Settings tests: {passed} passed, {failed} failed. Evidence: {evidence}");
Environment.ExitCode = failed == 0 ? 0 : 1;


