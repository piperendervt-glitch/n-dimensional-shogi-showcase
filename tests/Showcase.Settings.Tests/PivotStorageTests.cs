using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Match;
using NDShogi.Showcase.Core.Settings;

static class PivotStorageTests
{
    static readonly XNamespace N = "urn:ndshogi:pivot:settings-bundle/1", V = "urn:ndshogi:pivot:view/1", T = "urn:ndshogi:pivot:preview/1";
    static double[] Identity => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
    static PivotViewSnapshot View(double[]? center = null, double[]? basis = null, double[]? anchor = null, double[]? camera = null)
        => new PivotViewSnapshot(3, center ?? new double[] { 0, 0, 3, 0 }, basis ?? Identity, anchor ?? new double[] { 0, 1.01, 0 }, 4.5, .05,
            camera ?? new double[] { 32, 29, .82, 0, 0, 0 }, "subject", new QN(0, 0, 0, 0), new QN(0, 0, 0, 0));
    static PivotViewSnapshot ViewN(int n, string selected = "", double[]? basis = null)
    {
        var identity = new double[n * n]; for (int i = 0; i < n; i++) identity[i * n + i] = 1; var center = new double[n]; center[2] = 3;
        return new PivotViewSnapshot(3, center, basis ?? identity, new double[] { 0, 1.01, 0 }, 4.5, .05, new double[] { 32, 29, .82, 0, 0, 0 }, selected, default, default);
    }
    static SettingsDraft MatchDraft(int n)
    {
        var draft = SettingsDraft.CreateMatchDemo();
        if (n != 4) draft.Edit(d => { d.Dimensions = n.ToString(); foreach (var x in d.Definitions) { x.AxesMask = BoundsN.FullAxesMask(n).ToString(); if (x.MaxAxes == "4") x.MaxAxes = n.ToString(); } });
        return draft;
    }
    static object State(LoadedPivotSettings loaded) => new
    {
        loaded.LogicalHash, loaded.PreviewHash, draftHash = loaded.Draft.ContentHash, ruleHash = loaded.Config?.RuleHash, settingsHash = loaded.Config?.SettingsHash,
        loaded.IsValidSettings, loaded.View.EmbeddingId, loaded.View.Height, loaded.View.Center, loaded.View.Basis, loaded.View.Anchor, loaded.View.HalfWindow, loaded.View.CellScale,
        loaded.View.Camera, loaded.View.SelectedPieceId, selectedCell = loaded.View.SelectedCell.ToString(), planeReference = loaded.View.PlaneReferenceCell.ToString()
    };
    public static int? RunChild(string[] args)
    {
        if (args.Length == 0 || args[0] != "--pivot-read" && args[0] != "--pivot-resolve") return null;
        try
        {
            if(args[0]=="--pivot-resolve")
            {
                if(args.Length!=4)throw new ArgumentException("Child resolver requires runtime root, default path and output path.");
                string resolved=PivotSettingsLocation.Resolve(args[1],args[2]);
                File.WriteAllText(args[3],JsonSerializer.Serialize(new{processId=Environment.ProcessId,resolved,state=State(PivotSettingsStore.Load(resolved))},new JsonSerializerOptions{WriteIndented=true}));
                return 0;
            }
            if (args.Length != 3) throw new ArgumentException("Child reader requires input and output paths.");
            var loaded = PivotSettingsStore.Load(args[1]);
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { processId = Environment.ProcessId, state = State(loaded) }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 2; }
    }
    public static void Run(string dataRoot, string evidence, Action<string, Action> test, Action<bool, string> assert, Action<Action> reject)
    {
        void Check(bool value, string detail = "pivot assertion failed") => assert(value, detail);
        string Fault(Action action) { try { action(); } catch (Exception e) { return e.Message; } assert(false, "fault not rejected"); return ""; }
        string PathFor(string name) => Path.Combine(dataRoot, "pivot-" + name + ".xml");
        void Save(string path, PivotViewSnapshot? view = null) => PivotSettingsStore.SaveValid(path, SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw(), PreviewSession.CreateExperiment(), view ?? View());
        test("pivot-bundle-coherent-logical-preview-view-and-defensive-arrays", () =>
        {
            string path = PathFor("coherent"); var draft = SettingsDraft.CreateMatchDemo(); var options = new MatchOptionsRaw(); var preview = PreviewSession.CreateExperiment();
            var center = new double[] { .5, -1.2, 3, .125 }; var view = View(center); center[0] = 999;
            var before = draft.ContentHash; PivotSettingsStore.SaveValid(path, draft, options, preview, view);
            var saved = PivotSettingsStore.Load(path); var output = saved.View.Center; output[1] = 999;
            Check(!draft.IsDirty && saved.Draft.ContentHash == before && saved.PreviewHash == preview.Hash && saved.View.Center[0] == .5 && saved.View.Center[1] == -1.2);
            Check(saved.LogicalHash == PivotSettingsStore.LogicalContentHash(draft, options) && saved.Config != null && saved.Validation.IsValid);
            Check(!saved.Draft.CanApply, "Saving the new view never claims an unconnected settings mode was applied.");
        });
        test("pivot-arbitrary-origin-initial-three-separate-process-roundtrips", () =>
        {
            // Independent row-vector rotation composition, deliberately without Geometry references.
            var basis = Identity;
            void Rotate(int a, int b, double angle)
            {
                double c = Math.Cos(angle), s = Math.Sin(angle);
                for (int row = 0; row < 4; row++) { double x = basis[row * 4 + a], y = basis[row * 4 + b]; basis[row * 4 + a] = c * x - s * y; basis[row * 4 + b] = s * x + c * y; }
            }
            Rotate(1, 3, .413); Rotate(2, 3, -.287); Rotate(0, 3, .71);
            var poses = new[] { View(new double[] { .7, -1.3, 2.15, .85 }, basis, new double[] { .091, 1.004, -.032 }, new double[] { 54.125, -16.25, 1.12, .03, -.02, .08 }),
                View(new double[4], Identity, new double[] { 0, .86, 0 }), View() };
            for (int i = 0; i < poses.Length; i++)
            {
                string path = PathFor("child-" + i), output = Path.Combine(evidence, "pivot-child-" + i + ".json"); Save(path, poses[i]);
                var before = PivotSettingsStore.Load(path); var expected = JsonSerializer.Serialize(State(before));
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                info.ArgumentList.Add("--pivot-read"); info.ArgumentList.Add(path); info.ArgumentList.Add(output);
                using var child = Process.Start(info)!;
                Check(child.WaitForExit(20000), "Separate-process reader exceeded 20 seconds.");
                Check(child.ExitCode == 0, child.StandardError.ReadToEnd());
                using var doc = JsonDocument.Parse(File.ReadAllText(output));
                Check(doc.RootElement.GetProperty("processId").GetInt32() != Environment.ProcessId);
                var actual = JsonSerializer.Serialize(doc.RootElement.GetProperty("state")); Check(actual == expected, "Exact DTO and logical state did not round-trip through the separate process.");
                Check(before.View.Center.SequenceEqual(poses[i].Center) && before.View.Basis.SequenceEqual(poses[i].Basis) && before.View.Anchor.SequenceEqual(poses[i].Anchor) && before.View.Camera.SequenceEqual(poses[i].Camera));
                Check(before.View.Height == poses[i].Height && before.View.HalfWindow == poses[i].HalfWindow && before.View.CellScale == poses[i].CellScale && before.View.EmbeddingId == poses[i].EmbeddingId);
                Check(before.View.SelectedPieceId == poses[i].SelectedPieceId && before.View.SelectedCell == poses[i].SelectedCell && before.View.PlaneReferenceCell == poses[i].PlaneReferenceCell);
            }
        });
        test("pivot-invalid-raw-draft-separate-from-last-valid-bundle", () =>
        {
            string valid = PathFor("valid-kept"), invalid = PathFor("invalid"); var draft = SettingsDraft.CreateMatchDemo(); var preview = PreviewSession.CreateExperiment(); var options = new MatchOptionsRaw();
            PivotSettingsStore.SaveValid(valid, draft, options, preview, View()); byte[] bytes = File.ReadAllBytes(valid);
            draft.Edit(d => d.Definitions.Single(x => x.Id == "king-step").MaxAxes = "not a number <raw>"); options.TimeSeconds = "unresolved";
            PivotSettingsStore.SaveDraft(invalid, draft, options, preview, View()); var saved = PivotSettingsStore.Load(invalid);
            Check(!saved.IsValidSettings && !saved.Validation.IsValid && saved.Draft.ContentHash == draft.ContentHash && saved.Options.TimeSeconds == "unresolved");
            reject(() => PivotSettingsStore.SaveValid(valid, draft, options, preview, View())); reject(() => PivotSettingsStore.SaveDraft(valid, draft, options, preview, View()));
            Check(File.ReadAllBytes(valid).SequenceEqual(bytes));
        });
        test("pivot-atomic-all-fault-stages-preserve-old-bundle-and-edit", () =>
        {
            foreach (SaveStage stage in Enum.GetValues<SaveStage>())
            {
                string path = PathFor("fault-" + stage); var draft = SettingsDraft.CreateMatchDemo(); var preview = PreviewSession.CreateExperiment(); var options = new MatchOptionsRaw(); var view = View();
                PivotSettingsStore.SaveValid(path, draft, options, preview, view); byte[] before = File.ReadAllBytes(path);
                draft.Edit(d => d.Definitions.Single(x => x.Id == "king-step").MaxAxes = "2"); string edit = draft.ContentHash; var anchor = view.Anchor;
                reject(() => PivotSettingsStore.SaveValid(path, draft, options, preview, view, at => { if (at == stage) throw new IOException("injected " + at); }));
                Check(File.ReadAllBytes(path).SequenceEqual(before) && draft.ContentHash == edit && draft.IsDirty && view.Anchor.SequenceEqual(anchor));
                PivotSettingsStore.SaveValid(path, draft, options, preview, view); Check(!draft.IsDirty && PivotSettingsStore.Load(path).Draft.ContentHash == edit);
            }
            Check(!Directory.EnumerateFiles(dataRoot, ".pivot-*.tmp").Any());
        });
        test("pivot-flushed-file-is-reparsed-before-commit", () =>
        {
            string path = PathFor("flush-validation"); Save(path); var before = File.ReadAllBytes(path); var draft = SettingsDraft.CreateMatchDemo();
            reject(() => PivotSettingsStore.SaveValid(path, draft, new MatchOptionsRaw(), PreviewSession.CreateExperiment(), View(), at =>
            {
                if (at == SaveStage.AfterFlush) File.WriteAllText(Directory.EnumerateFiles(dataRoot, ".pivot-*.tmp").Single(), "<corrupted />");
            }));
            Check(File.ReadAllBytes(path).SequenceEqual(before) && draft.IsDirty && !Directory.EnumerateFiles(dataRoot, ".pivot-*.tmp").Any());
        });
        test("pivot-first-save-fault-and-real-invalid-destination-preserve-edit", () =>
        {
            string path = PathFor("first-failure"); var draft = SettingsDraft.CreateMatchDemo();
            reject(() => PivotSettingsStore.SaveValid(path, draft, new MatchOptionsRaw(), PreviewSession.CreateExperiment(), View(), at => { if (at == SaveStage.BeforeReplace) throw new IOException("first write fault"); }));
            Check(!File.Exists(path) && draft.IsDirty);
            string directory = Path.Combine(dataRoot, "pivot-destination-directory"); Directory.CreateDirectory(directory);
            reject(() => PivotSettingsStore.SaveValid(directory, draft, new MatchOptionsRaw(), PreviewSession.CreateExperiment(), View())); Check(Directory.Exists(directory) && draft.IsDirty);
        });
        test("pivot-strict-schema-attributes-elements-bindings-and-corruption", () =>
        {
            string source = PathFor("strict-source"); Save(source); string text = File.ReadAllText(source);
            Action<XDocument>[] mutations = {
                d => d.Root!.SetAttributeValue("schema", "999"), d => d.Root!.SetAttributeValue("unknown", "1"), d => d.Root!.Add(new XElement(N + "unknown")),
                d => d.Root!.Element(V + "view")!.SetAttributeValue("schema", "2"), d => d.Root!.Element(V + "view")!.SetAttributeValue("embedding", "future/1"),
                d => d.Root!.Element(V + "view")!.SetAttributeValue("anchor", "0,NaN,0"), d => d.Root!.Element(V + "view")!.SetAttributeValue("height", "Infinity"),
                d => d.Root!.Element(V + "view")!.SetAttributeValue("halfWindow", "0"), d => d.Root!.Element(V + "view")!.SetAttributeValue("center", "0,0,0"),
                d => d.Root!.Element(V + "view")!.Attribute("anchor")!.Remove(), d => d.Root!.Element(V + "view")!.SetAttributeValue("logicalHash", new string('0',64)),
                d => d.Root!.Element(V + "view")!.SetAttributeValue("previewHash", new string('0',64)), d => d.Root!.Element(V + "view")!.SetAttributeValue("selectedPiece", "unrelated-match-piece"),
                d => d.Root!.Element(V + "view")!.SetAttributeValue("planeReference", "0,0,5,0"), d => d.Root!.Element(T + "preview")!.Elements().First().SetAttributeValue("cell", "0,1,0,0"),
                d => d.Root!.Elements().First().Elements().Last().SetAttributeValue("timeSeconds", "601"), d => d.Root!.Add(new XElement(d.Root!.Element(V + "view")!))
            };
            for (int i = 0; i < mutations.Length; i++)
            {
                var doc = XDocument.Parse(text); mutations[i](doc); string path = PathFor("bad-" + i); File.WriteAllText(path, doc.ToString()); var before = File.ReadAllBytes(path);
                reject(() => PivotSettingsStore.Load(path)); Check(before.SequenceEqual(File.ReadAllBytes(path)));
            }
            foreach (var pair in new[] { ("dtd", "<!DOCTYPE bundle [<!ENTITY x SYSTEM 'file:///unavailable'>]>" + text), ("broken", text.Substring(0, text.Length / 2)), ("huge", new string('x',1024*1024+1)) })
            { string path = PathFor(pair.Item1); File.WriteAllText(path, pair.Item2); reject(() => PivotSettingsStore.Load(path)); }
        });
        test("pivot-reflection-nonorthogonal-camera-and-unbounded-view-rejected", () =>
        {
            var reflected = Identity; reflected[15] = -1; reject(() => View(basis: reflected));
            var skew = Identity; skew[1] = .001; reject(() => View(basis: skew));
            reject(() => View(camera: new double[] { 0, 76, 1, 0, 0, 0 })); reject(() => View(camera: new double[] { 0, 0, .1, 0, 0, 0 }));
            reject(() => View(center: new double[] { 0, 0, double.NaN, 0 })); reject(() => View(anchor: new double[] { 0, 1000001, 0 }));
        });
        test("pivot-board-at-origin-view-migrates-anchor-to-board-surface", () =>
        {
            var basis = Identity; var legacy = new PivotViewSnapshot(3, new double[] { .5, -1, 3, .25 }, basis, new double[] { .1, 1.01, -.2 }, 4.5, .05,
                new double[] { 27, 31, 1.04, 0, 0, 0 }, "", default, default, PivotViewSnapshot.LegacyBoardAtOriginVersion);
            Check(legacy.EmbeddingId == PivotViewSnapshot.EmbeddingVersion && Math.Abs(legacy.Anchor[1] - .86) < 1e-12 && legacy.Anchor[0] == .1 && legacy.Anchor[2] == -.2 && legacy.Center.SequenceEqual(new double[] { .5, -1, 3, .25 }));
            reject(() => new PivotViewSnapshot(3, new double[4], basis, new double[3], 4.5, .05, new double[] { 27, 31, 1.04, 0, 0, 0 }, "", default, default, "core-v-plus-height/0"));
        });
        test("pivot-board-at-origin-file-loads-with-migration-notice", () =>
        {
            string path = PathFor("view-embedding-1"); var view = View();
            PivotSettingsStore.SaveValid(path, SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw(), PreviewSession.CreateExperiment(), view);
            Check(PivotSettingsStore.Load(path).MigrationNotice == "", "Current embedding needs no notice.");
            var doc = XDocument.Load(path); doc.Root!.Element(V + "view")!.SetAttributeValue("embedding", PivotViewSnapshot.LegacyBoardAtOriginVersion); File.WriteAllText(path, doc.ToString());
            var loaded = PivotSettingsStore.Load(path);
            Check(loaded.MigrationNotice.Length > 0 && loaded.View.EmbeddingId == PivotViewSnapshot.EmbeddingVersion, "A /1 file must be migrated with a notice so it is rewritten.");
            Check(Math.Abs(loaded.View.Anchor[1] - (view.Anchor[1] - view.Height * view.CellScale)) < 1e-12, "Anchor moves to the board surface.");
        });
        test("pivot-explicit-known-legacy-migration-keeps-logical-and-original-bytes", () =>
        {
            string match = PathFor("old-match"), trial = PathFor("old-trial"), target = PathFor("migrated"); var draft = SettingsDraft.CreateMatchDemo(); var options = new MatchOptionsRaw(); var preview = PreviewSession.CreateExperiment();
            MatchSettingsStore.SaveValid(match, draft, options); SettingsStore.SaveDraft(trial, draft, preview, ViewSnapshot.Default);
            byte[] oldMatch = File.ReadAllBytes(match), oldTrial = File.ReadAllBytes(trial); var imported = PivotSettingsStore.ImportLegacy(match, trial, View());
            Check(imported.MigrationNotice.Contains("表示設定を新方式の初期状態にした") && imported.Draft.ContentHash == draft.ContentHash && imported.PreviewHash == preview.Hash);
            Check(imported.View.Center.SequenceEqual(new double[] { 0, 0, 3, 0 }) && imported.View.Anchor[1] == 1.01);
            PivotSettingsStore.SaveValid(target, imported.Draft, imported.Options, imported.Preview, imported.View);
            Check(File.ReadAllBytes(match).SequenceEqual(oldMatch) && File.ReadAllBytes(trial).SequenceEqual(oldTrial));
            reject(() => PivotSettingsStore.SaveValid(match, draft, options, preview, View())); Check(File.ReadAllBytes(match).SequenceEqual(oldMatch));
            reject(() => PivotSettingsStore.Load(match)); reject(() => SettingsStore.Load(target));
        });
        test("pivot-legacy-mismatch-unknown-and-missing-never-guessed", () =>
        {
            string match = PathFor("mismatch-match"), trial = PathFor("mismatch-trial"); var draft = SettingsDraft.CreateMatchDemo();
            MatchSettingsStore.SaveValid(match, draft, new MatchOptionsRaw());
            SettingsStore.SaveDraft(trial, SettingsDraft.CreateMatchDemo(), PreviewSession.CreateExperiment(), ViewSnapshot.Default);
            reject(() => PivotSettingsStore.ImportLegacy(match, trial, View()));
            reject(() => PivotSettingsStore.ImportLegacy(match, PathFor("nonexistent"), View()));
            var known = PivotSettingsStore.ImportLegacy(match, null, View()); Check(known.Draft.ContentHash == draft.ContentHash && known.MigrationNotice.Length > 0);
            string unknown = PathFor("legacy-unknown"); File.WriteAllText(unknown, File.ReadAllText(match).Replace("schema=\"1\"", "schema=\"future\""));
            reject(() => PivotSettingsStore.ImportLegacy(unknown, null, View()));
        });
        test("pivot-legacy-view1-exact-contract-not-silently-expanded", () =>
        {
            string old = PathFor("old-view-exact"); SettingsStore.SaveDraft(old, SettingsDraft.CreateMatchDemo(), PreviewSession.CreateExperiment(), ViewSnapshot.Default);
            var doc = XDocument.Load(old); var view = doc.Root!.Elements().Last(); view.SetAttributeValue("anchor", "0,1.01,0"); File.WriteAllText(old, doc.ToString());
            reject(() => SettingsStore.Load(old));
        });
        test("pivot-location-recovers-to-sibling-without-touching-broken-default-and-separate-process-resolves", () =>
        {
            string root=Path.Combine(dataRoot,"location-recover");Directory.CreateDirectory(root);string fallback=Path.Combine(root,PivotSettingsStore.ValidFileName),recovered=Path.Combine(root,"recovered-one.xml");
            Check(PivotSettingsLocation.Resolve(root,fallback)==fallback,"Absent selector must retain the default path even before its first save.");
            File.WriteAllText(fallback,"<broken-original>");byte[] original=File.ReadAllBytes(fallback);
            var draft=SettingsDraft.CreateMatchDemo();draft.Edit(d=>d.Name="Recovered current edit");string editedHash=draft.ContentHash;
            var pose=View(new double[]{.7,-1.3,2.15,.85},anchor:new double[]{.091,1.004,-.032},camera:new double[]{54.125,-16.25,1.12,.03,-.02,.08});
            PivotSettingsLocation.SaveRecovery(root,recovered,draft,new MatchOptionsRaw(),PreviewSession.CreateExperiment(),pose);
            Check(PivotSettingsLocation.Resolve(root,fallback)==recovered&&File.ReadAllBytes(fallback).SequenceEqual(original));
            Check(draft.ContentHash==editedHash&&!draft.IsDirty&&draft.CanUndo,"Successful recovery must keep the current draft and undo history.");
            string expected=JsonSerializer.Serialize(State(PivotSettingsStore.Load(recovered)));
            string output=Path.Combine(evidence,"pivot-location-child.json");
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
            if(string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(string arg in new[]{"--pivot-resolve",root,fallback,output})info.ArgumentList.Add(arg);
            using var child=Process.Start(info)!;Check(child.WaitForExit(20000),"Separate process resolve timed out.");Check(child.ExitCode==0,child.StandardError.ReadToEnd());
            using var document=JsonDocument.Parse(File.ReadAllText(output));Check(document.RootElement.GetProperty("processId").GetInt32()!=Environment.ProcessId&&document.RootElement.GetProperty("resolved").GetString()==recovered);
            Check(JsonSerializer.Serialize(document.RootElement.GetProperty("state"))==expected,"Recovery selection must restore the entire coherent bundle in another process.");
            Check(File.ReadAllBytes(fallback).SequenceEqual(original));
        });
        test("pivot-recovery-bundle-and-selector-faults-preserve-current-edit-and-previous-selection", () =>
        {
            foreach(bool selectorFailure in new[]{false,true})foreach(SaveStage stage in Enum.GetValues<SaveStage>())
            {
                string root=Path.Combine(dataRoot,"recovery-fault-"+selectorFailure+"-"+stage);Directory.CreateDirectory(root);
                string original=Path.Combine(root,"broken.xml"),first=Path.Combine(root,"first.xml"),target=Path.Combine(root,"recovered.xml");
                File.WriteAllText(original,"<broken-original>");Save(first);PivotSettingsLocation.Select(root,first);
                string selector=Path.Combine(root,PivotSettingsLocation.SelectorFileName);var originalBytes=File.ReadAllBytes(original);var firstBytes=File.ReadAllBytes(first);var selectorBytes=File.ReadAllBytes(selector);
                var draft=SettingsDraft.CreateMatchDemo();draft.Edit(d=>d.Name="Do not discard this edit");string hash=draft.ContentHash;
                var options=new MatchOptionsRaw();var preview=PreviewSession.CreateExperiment();string previewHash=preview.Hash;
                var pose=View(new double[]{1,2,3,4},camera:new double[]{42,12,1,.02,.03,.04});
                Action<SaveStage> fault=at=>{if(at==stage)throw new IOException("injected recovery "+at);};
                reject(()=>PivotSettingsLocation.SaveRecovery(root,target,draft,options,preview,pose,selectorFailure?null:fault,selectorFailure?fault:null));
                Check(draft.ContentHash==hash&&draft.IsDirty&&draft.CanUndo&&preview.Hash==previewHash,"Failed recovery changed the live edit or its unsaved state.");
                Check(originalBytes.SequenceEqual(File.ReadAllBytes(original))&&firstBytes.SequenceEqual(File.ReadAllBytes(first))&&selectorBytes.SequenceEqual(File.ReadAllBytes(selector)));
                Check(PivotSettingsLocation.Resolve(root,original)==first&&!Directory.EnumerateFiles(root,".pivot-*.tmp").Any());
                Check(File.Exists(target)==selectorFailure,"Only an already committed recovery candidate may remain after selector failure.");
                if(selectorFailure)Check(PivotSettingsStore.Load(target).Draft.ContentHash==hash,"Retained recovery candidate must remain readable.");
            }
        });
        test("pivot-recovery-rejects-existing-target-invalid-edit-and-unknown-selector-without-reset", () =>
        {
            string root=Path.Combine(dataRoot,"recovery-reject");Directory.CreateDirectory(root);string target=Path.Combine(root,"target.xml");Save(target);var before=File.ReadAllBytes(target);
            var draft=SettingsDraft.CreateMatchDemo();draft.Edit(d=>d.Name="Unsaved current edit");string hash=draft.ContentHash;
            var options=new MatchOptionsRaw();var preview=PreviewSession.CreateExperiment();
            reject(()=>PivotSettingsLocation.SaveRecovery(root,target,draft,options,preview,View()));Check(before.SequenceEqual(File.ReadAllBytes(target))&&draft.IsDirty&&draft.ContentHash==hash);
            string invalid=Path.Combine(root,"invalid-edit.xml");draft.Edit(d=>d.Definitions.Single(x=>x.Id=="king-step").MaxAxes="invalid raw");hash=draft.ContentHash;
            reject(()=>PivotSettingsLocation.SaveRecovery(root,invalid,draft,options,preview,View()));Check(!File.Exists(invalid)&&draft.IsDirty&&draft.ContentHash==hash&&draft.CanUndo);
            draft.Undo();string selector=Path.Combine(root,PivotSettingsLocation.SelectorFileName);File.WriteAllText(selector,"<unknown-original />");var selectorBytes=File.ReadAllBytes(selector);
            string blocked=Path.Combine(root,"unknown-selector.xml");hash=draft.ContentHash;
            reject(()=>PivotSettingsLocation.SaveRecovery(root,blocked,draft,options,preview,View()));Check(!File.Exists(blocked)&&selectorBytes.SequenceEqual(File.ReadAllBytes(selector))&&draft.IsDirty&&draft.ContentHash==hash);
        });
        test("pivot-recovery-does-not-mark-a-later-live-edit-as-saved", () =>
        {
            string root=Path.Combine(dataRoot,"recovery-later-edit");Directory.CreateDirectory(root);string target=Path.Combine(root,"target.xml");
            var draft=SettingsDraft.CreateMatchDemo();draft.Edit(d=>d.Name="Captured edit");string captured=draft.ContentHash;
            PivotSettingsLocation.SaveRecovery(root,target,draft,new MatchOptionsRaw(),PreviewSession.CreateExperiment(),View(),selectorFault:at=>{if(at==SaveStage.AfterFlush)draft.Edit(d=>d.Name="Later live edit");});
            Check(PivotSettingsStore.Load(target).Draft.ContentHash==captured&&draft.ContentHash!=captured&&draft.IsDirty&&draft.CanUndo);
            Check(PivotSettingsLocation.Resolve(root,target)==target);
        });
        test("pivot-location-all-atomic-faults-keep-current-selection-and-bundles", () =>
        {
            foreach(SaveStage stage in Enum.GetValues<SaveStage>())
            {
                string root=Path.Combine(dataRoot,"location-fault-"+stage);Directory.CreateDirectory(root);string first=Path.Combine(root,"first.xml"),second=Path.Combine(root,"second.xml");Save(first);Save(second);
                PivotSettingsLocation.Select(root,first);string selector=Path.Combine(root,PivotSettingsLocation.SelectorFileName);var before=File.ReadAllBytes(selector);var firstBytes=File.ReadAllBytes(first);var secondBytes=File.ReadAllBytes(second);
                reject(()=>PivotSettingsLocation.Select(root,second,at=>{if(at==stage)throw new IOException("injected "+stage);}));
                Check(before.SequenceEqual(File.ReadAllBytes(selector))&&PivotSettingsLocation.Resolve(root,first)==first&&firstBytes.SequenceEqual(File.ReadAllBytes(first))&&secondBytes.SequenceEqual(File.ReadAllBytes(second)));
                Check(!Directory.EnumerateFiles(root,".pivot-location-*.tmp").Any());
                PivotSettingsLocation.Select(root,second);Check(PivotSettingsLocation.Resolve(root,first)==second);
            }
        });
        test("pivot-location-rejects-traversal-unknown-malformed-missing-and-invalid-bundle", () =>
        {
            string root=Path.Combine(dataRoot,"location-strict");Directory.CreateDirectory(root);string target=Path.Combine(root,"target.xml"),fallback=Path.Combine(root,"default.xml");Save(target);
            string selector=Path.Combine(root,PivotSettingsLocation.SelectorFileName);
            XDocument Pointer(string name)=>new XDocument(new XElement(XName.Get("selection","urn:ndshogi:pivot:settings-location/1"),new XAttribute("schema","1"),new XAttribute("file",name)));
            foreach(string name in new[]{"../target.xml","..\\target.xml","sub/target.xml","sub\\target.xml","C:\\target.xml","target..xml","target.txt","target.xml ","pivot-selected-bundle.xml"})
            {File.WriteAllText(selector,Pointer(name).ToString());var bytes=File.ReadAllBytes(selector);reject(()=>PivotSettingsLocation.Resolve(root,fallback));reject(()=>PivotSettingsLocation.Select(root,target));Check(bytes.SequenceEqual(File.ReadAllBytes(selector)));}
            var unknown=Pointer("target.xml");unknown.Root!.SetAttributeValue("schema","2");File.WriteAllText(selector,unknown.ToString());reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            unknown=Pointer("target.xml");unknown.Root!.SetAttributeValue("future","1");File.WriteAllText(selector,unknown.ToString());reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            unknown=Pointer("target.xml");unknown.Root!.Add(new XElement("unknown"));File.WriteAllText(selector,unknown.ToString());reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            File.WriteAllText(selector,"<!DOCTYPE selection [<!ENTITY x SYSTEM 'file:///unavailable'>]>"+Pointer("target.xml"));reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            File.WriteAllText(selector,Pointer("missing.xml").ToString());reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            File.WriteAllText(selector,Pointer("target.xml").ToString());string nested=Path.Combine(root,"nested");Directory.CreateDirectory(nested);string child=Path.Combine(nested,"child.xml");Save(child);
            reject(()=>PivotSettingsLocation.Select(root,child));reject(()=>PivotSettingsLocation.Select(root,Path.Combine(root,"..","other.xml")));
            string invalid=Path.Combine(root,"invalid.xml");PivotSettingsStore.SaveDraft(invalid,SettingsDraft.CreateMatchDemo(),new MatchOptionsRaw(),PreviewSession.CreateExperiment(),View());
            reject(()=>PivotSettingsLocation.Select(root,invalid));File.WriteAllText(selector,Pointer("invalid.xml").ToString());reject(()=>PivotSettingsLocation.Resolve(root,fallback));
            Check(File.Exists(target)&&File.Exists(invalid)&&File.Exists(child));
        });
        test("pivot-location-revalidates-flushed-selector-and-first-save-fault", () =>
        {
            string root=Path.Combine(dataRoot,"location-flush");Directory.CreateDirectory(root);string target=Path.Combine(root,"target.xml");Save(target);string selector=Path.Combine(root,PivotSettingsLocation.SelectorFileName);
            reject(()=>PivotSettingsLocation.Select(root,target,at=>{if(at==SaveStage.BeforeReplace)throw new IOException("first selector failure");}));Check(!File.Exists(selector));
            PivotSettingsLocation.Select(root,target);var bytes=File.ReadAllBytes(selector);
            reject(()=>PivotSettingsLocation.Select(root,target,at=>{if(at==SaveStage.AfterFlush)File.WriteAllText(Directory.EnumerateFiles(root,".pivot-location-*.tmp").Single(),"<corrupt />");}));
            Check(bytes.SequenceEqual(File.ReadAllBytes(selector))&&PivotSettingsLocation.Resolve(root,target)==target&&!Directory.EnumerateFiles(root,".pivot-location-*.tmp").Any());
        });
        test("pivot-five-dimensional-bundle-round-trips-and-four-dimensional-form-is-unchanged", () =>
        {
            var draft = SettingsDraft.CreateMatchDemo();
            draft.Edit(d => { d.Dimensions = "5"; foreach (var x in d.Definitions) { x.AxesMask = "31"; if (x.MaxAxes == "4") x.MaxAxes = "5"; } });
            var preview = new PreviewSession(new BoundsN(dimensions: 5), new[] { new TrialPiece("subject", "king-step", Side.A, new QN(0, 0, 0, 0, 1)), new TrialPiece("flat", "pawn-step", Side.B, new QN(1, 0, 0, 0)) });
            var identity5 = new double[25]; for (int i = 0; i < 5; i++) identity5[i * 5 + i] = 1;
            var view5 = new PivotViewSnapshot(3, new double[] { 0, 0, 3, 0, .5 }, identity5, new double[] { 0, 1.01, 0 }, 4.5, .05, new double[] { 32, 29, .82, 0, 0, 0 }, "subject", new QN(0, 0, 0, 0, 1), new QN(0, 0, 0, 0));
            Check(view5.Dimensions == 5 && View().Dimensions == 4, "snapshot N from the centre vector");
            string path = Path.Combine(dataRoot, "pivot-5d.xml"); PivotSettingsStore.SaveValid(path, draft, new MatchOptionsRaw(), preview, view5);
            var loaded = PivotSettingsStore.Load(path);
            Check(loaded.View.Dimensions == 5 && loaded.View.Center.Length == 5 && loaded.View.Basis.Length == 25 && Math.Abs(loaded.View.Center[4] - .5) < 1e-12, "5D view restored");
            Check(loaded.Preview.Bounds.Dimensions == 5 && loaded.Preview.Pieces.All(p => p.Position.Dimensions == 5) && loaded.Preview.Find("flat")!.Position == new QN(1, 0, 0, 0), "5D trial board restored, flat cell padded");
            Check(loaded.View.SelectedCell == new QN(0, 0, 0, 0, 1) && loaded.Config!.Dimensions == 5, "5D selection and rules restored");
            string xml = File.ReadAllText(path); Check(xml.Contains("center=\"0,0,3,0,0.5\"") && xml.Contains("cell=\"0,0,0,0,1\""), "5D vectors written with five components");
            reject(() => new PivotViewSnapshot(3, new double[] { 0, 0, 3, 0, 0 }, Identity, new double[] { 0, 1.01, 0 }, 4.5, .05, new double[] { 32, 29, .82, 0, 0, 0 }, "subject", new QN(), new QN()));
            reject(() => new PivotViewSnapshot(3, new double[] { 0, 0, 3, 0 }, Identity, new double[] { 0, 1.01, 0 }, 4.5, .05, new double[] { 32, 29, .82, 0, 0, 0 }, "subject", new QN(0, 0, 0, 0, 1), new QN()));
            string four = Path.Combine(dataRoot, "pivot-4d.xml"); PivotSettingsStore.SaveValid(four, SettingsDraft.CreateMatchDemo(), new MatchOptionsRaw(), PreviewSession.CreateExperiment(), View());
            string fourXml = File.ReadAllText(four); Check(fourXml.Contains("center=\"0,0,3,0\"") && fourXml.Contains("dimensions=\"4\"") && PivotSettingsStore.Load(four).View.Dimensions == 4, "4D bundle keeps its four-component form");
        });
        test("pivot-unselected-cells-save-as-the-padded-origin-in-every-n", () =>
        {
            // A match starts with no selected cell (QN default, which prints as ""); the saved view must still carry N coordinates.
            foreach (int n in new[] { 4, 5, 7 })
            {
                var identity = new double[n * n]; for (int i = 0; i < n; i++) identity[i * n + i] = 1;
                var center = new double[n]; center[2] = 3;
                var view = new PivotViewSnapshot(3, center, identity, new double[] { 0, 1.01, 0 }, 4.5, .05, new double[] { 32, 29, .82, 0, 0, 0 }, "", default, default);
                Check(view.SelectedCell.Dimensions == n && view.PlaneReferenceCell.ToString() == string.Join(",", Enumerable.Repeat("0", n)), "default cells padded to " + n);
                var draft = SettingsDraft.CreateMatchDemo();
                if (n != 4) draft.Edit(d => { d.Dimensions = n.ToString(); foreach (var x in d.Definitions) { x.AxesMask = BoundsN.FullAxesMask(n).ToString(); if (x.MaxAxes == "4") x.MaxAxes = n.ToString(); } });
                string path = Path.Combine(dataRoot, "pivot-unselected-" + n + ".xml");
                PivotSettingsStore.SaveValid(path, draft, new MatchOptionsRaw(), new PreviewSession(new BoundsN(dimensions: n)), view);
                var loaded = PivotSettingsStore.Load(path); Check(loaded.View.SelectedCell == default && loaded.View.Dimensions == n, "unselected view round trip in " + n + "D");
                if (n == 4) Check(File.ReadAllText(path).Contains("selectedCell=\"0,0,0,0\""), "4D unselected cell keeps the original four-zero form");
            }
        });

        test("H3-copy-layout-match-position-saves-and-reloads-through-the-bundle", () =>
        {
            Check(PreviewSnapshot.MaxPieces == 1024, "one piece limit for the trial board, the match and the bundle");
            foreach (var (n, axis) in new[] { (5, 2), (4, 3) })
            {
                var draft = MatchDraft(n); draft.Edit(d => d.InitialLayout = DraftData.CopyLayout(axis));
                Check(MatchConfig.TryCreate(draft, new MatchOptionsRaw(), out var config, out var report), string.Join(" / ", report.Errors));
                var session = MatchSession.Create(config!, MatchMode.PvP, new StopwatchClock()); var position = session.ToPreview();
                var matchDraft = new SettingsDraft(session.Config.Preset.ToDraftData()); var options = session.Config.ToRaw(); string path = PathFor("copy-layout-" + n);
                // The save the match screen makes; the 256-piece bundle limit used to refuse it.
                PivotSettingsStore.SaveValid(path, matchDraft, options, position, ViewN(n, "A-King-0"));
                var loaded = PivotSettingsStore.Load(path);
                Check(position.Pieces.Count == 360 && loaded.IsValidSettings && loaded.Preview.Pieces.Count == 360 && loaded.PreviewHash == position.Hash && loaded.Preview.Hash == session.ToPreview().Hash, "360-piece position reloads in " + n + "D");
                Check(loaded.LogicalHash == PivotSettingsStore.LogicalContentHash(matchDraft, options) && loaded.Config!.RuleHash == session.Config.RuleHash && loaded.View.SelectedPieceId == "A-King-0" && loaded.View.Dimensions == n);
                Check(PreviewQuery.Execute(loaded.Config!.Preset, loaded.Preview, "A-King-0").Status == QueryStatus.Complete);
                if (n == 5)
                {
                    var doc = XDocument.Load(path); var trial = doc.Root!.Element(T + "preview")!; var first = trial.Elements().First();
                    for (int i = 0; i < PreviewSnapshot.MaxPieces + 1 - 360; i++) { var copy = new XElement(first); copy.SetAttributeValue("id", "extra-" + i); trial.Add(copy); }
                    string over = PathFor("over-limit"); File.WriteAllText(over, doc.ToString()); string message = Fault(() => PivotSettingsStore.Load(over)); Check(message.Contains("1024"), message);
                }
            }
            var many = Enumerable.Range(0, PreviewSnapshot.MaxPieces + 1).Select(i => new TrialPiece("p" + i, "pawn-step", Side.A, new QN(i % 9 - 4, i / 9 % 9 - 4, i / 81 % 9 - 4, i / 729 - 4))).ToArray();
            Check(new PreviewSnapshot(many.Take(PreviewSnapshot.MaxPieces)).Pieces.Count == 1024); reject(() => new PreviewSnapshot(many));
        });
        test("M4-trial-board-and-view-of-another-N-are-refused-by-name-not-by-hash", () =>
        {
            string path = PathFor("n-mismatch");
            string message = Fault(() => PivotSettingsStore.SaveValid(path, MatchDraft(4), new MatchOptionsRaw(), PreviewSession.CreateExperiment(5), View()));
            Check(message.Contains("試し盤の次元数N=5") && message.Contains("設定のN=4") && !message.Contains("hash") && !File.Exists(path), message);
            message = Fault(() => PivotSettingsStore.SaveDraft(path, MatchDraft(4), new MatchOptionsRaw(), PreviewSession.CreateExperiment(), ViewN(5)));
            Check(message.Contains("表示の次元数N=5") && !File.Exists(path), message);
            // A 5D bundle whose rules were edited to N=4 with the logical hash recomputed: the leftover 5D view, then the 5D trial board, are named.
            string five = PathFor("n5-edited"); PivotSettingsStore.SaveValid(five, MatchDraft(5), new MatchOptionsRaw(), PreviewSession.CreateExperiment(5), ViewN(5));
            var saved = PivotSettingsStore.Load(five); var four = new SettingsDraft(saved.Draft.Current); four.Edit(d => { d.Dimensions = "4"; foreach (var x in d.Definitions) { if (x.AxesMask == "31") x.AxesMask = "15"; if (x.MaxAxes == "5") x.MaxAxes = "4"; } });
            string logical = PivotSettingsStore.LogicalContentHash(four, saved.Options);
            XDocument Edited(bool view4)
            {
                var doc = XDocument.Load(five); var rules = doc.Root!.Elements().First().Elements().First(); rules.SetAttributeValue("dimensions", "4");
                foreach (var d in rules.Elements()) { if ((string?)d.Attribute("axes") == "31") d.SetAttributeValue("axes", "15"); if ((string?)d.Attribute("maxAxes") == "5") d.SetAttributeValue("maxAxes", "4"); }
                doc.Root.SetAttributeValue("logicalHash", logical); var v = doc.Root.Element(V + "view")!; v.SetAttributeValue("logicalHash", logical);
                if (view4) { v.SetAttributeValue("center", "0,0,3,0"); v.SetAttributeValue("basis", string.Join(",", Identity)); v.SetAttributeValue("selectedCell", "0,0,0,0"); v.SetAttributeValue("planeReference", "0,0,0,0"); }
                return doc;
            }
            foreach (var (view4, expected) in new[] { (false, "表示の次元数N=5が設定のN=4"), (true, "試し駒のセル（5成分）がN=4") })
            {
                string edited = PathFor("n5-edited-" + view4); File.WriteAllText(edited, Edited(view4).ToString()); message = Fault(() => PivotSettingsStore.Load(edited));
                Check(message.Contains(expected) && !message.Contains("hash"), message);
            }
            // A raw draft whose N is not a number yet keeps its board's N (it used to be read as 4, so a 5D board failed on vector length).
            foreach (int n in new[] { 4, 5 })
            {
                var raw = MatchDraft(n); raw.Edit(d => d.Dimensions = "x"); string rawPath = PathFor("raw-n-x-" + n);
                PivotSettingsStore.SaveDraft(rawPath, raw, new MatchOptionsRaw(), PreviewSession.CreateExperiment(n), ViewN(n)); var loaded = PivotSettingsStore.Load(rawPath);
                Check(!loaded.IsValidSettings && loaded.Draft.Current.Dimensions == "x" && loaded.Preview.Bounds.Dimensions == n && loaded.View.Dimensions == n && loaded.Draft.ContentHash == raw.ContentHash, "raw N preserved in " + n + "D");
                message = Fault(() => PivotSettingsStore.SaveDraft(rawPath, raw, new MatchOptionsRaw(), PreviewSession.CreateExperiment(n), ViewN(n == 4 ? 5 : 4)));
                Check(message.Contains("試し盤の次元数N=" + n) && message.Contains("「x」"), message);
            }
        });
        test("M4-legacy-import-builds-the-trial-board-at-the-settings-N", () =>
        {
            string match = PathFor("legacy-n5-match"), trial = PathFor("legacy-n5-trial"); var draft = MatchDraft(5); MatchSettingsStore.SaveValid(match, draft, new MatchOptionsRaw());
            var imported = PivotSettingsStore.ImportLegacy(match, null, ViewN(5));
            Check(imported.Preview.Bounds.Dimensions == 5 && imported.Preview.Pieces.Single().Position.Dimensions == 5 && imported.IsValidSettings && imported.Config!.Dimensions == 5);
            string target = PathFor("legacy-n5-migrated"); PivotSettingsStore.SaveValid(target, imported.Draft, imported.Options, imported.Preview, imported.View); Check(PivotSettingsStore.Load(target).Preview.Bounds.Dimensions == 5);
            string message = Fault(() => PivotSettingsStore.ImportLegacy(match, null, View())); Check(message.Contains("表示の次元数N=4が設定のN=5"), message);
            SettingsStore.SaveDraft(trial, draft, PreviewSession.CreateExperiment(), ViewSnapshot.Default);
            message = Fault(() => PivotSettingsStore.ImportLegacy(match, trial, ViewN(5))); Check(message.Contains("旧試し盤は4次元専用"), message);
        });
        test("L16-high-N-orthonormal-basis-is-accepted-by-orientation-sign", () =>
        {
            // Each row 1−4.9e-7 long is orthonormal within 1e-6, but det ≈ 1−4.4e-6 fell under the former fixed 1−4e-6 floor.
            var scaled = new double[81]; for (int i = 0; i < 9; i++) scaled[i * 9 + i] = 1 - 4.9e-7;
            Check(ViewN(9, basis: scaled).Dimensions == 9);
            var reflected = new double[81]; for (int i = 0; i < 9; i++) reflected[i * 9 + i] = i == 8 ? -1 : 1; reject(() => ViewN(9, basis: reflected));
            var swapped = new double[81]; for (int i = 0; i < 9; i++) swapped[i * 9 + (i < 2 ? 1 - i : i)] = 1; reject(() => ViewN(9, basis: swapped));
        });
    }
}
