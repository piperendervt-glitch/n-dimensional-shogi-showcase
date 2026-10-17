using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NDShogi.Showcase.Core.Settings;

namespace NDShogi.Showcase.Core.Match
{
    public sealed class LoadedMatchSettings
    {
        public SettingsDraft Draft { get; }
        public MatchOptionsRaw Options { get; }
        public MatchConfig? Config { get; }
        public ValidationReport Validation { get; }
        public bool IsValidSettings { get; }
        internal LoadedMatchSettings(DraftData data, MatchOptionsRaw options, bool valid)
        {
            Draft = new SettingsDraft(data, true); Options = options.Clone(); IsValidSettings = valid;
            MatchConfig.TryCreate(Draft, Options, out var config, out var report); Config = config; Validation = report;
            if (valid && config == null) throw new InvalidDataException("正常な次局設定として保存された内容が不正です：" + string.Join(" / ", report.Errors));
        }
    }
    // New schema; never edits or migrates a CFG1 file. Display/trial persistence remains separate.
    public static class MatchSettingsStore
    {
        internal static readonly XNamespace N = "urn:ndshogi:cfg2:settings/1";
        private const int MaxBytes = 1024 * 1024;
        public static void SaveValid(string path, SettingsDraft draft, MatchOptionsRaw options, Action<SaveStage>? fault = null)
        {
            if (!MatchConfig.TryCreate(draft, options, out _, out var report)) throw new InvalidOperationException(string.Join(" / ", report.Errors));
            Save(path, draft, options, true, fault);
        }
        public static void SaveDraft(string path, SettingsDraft draft, MatchOptionsRaw options, Action<SaveStage>? fault = null) => Save(path, draft, options, false, fault);
        private static void Save(string path, SettingsDraft draft, MatchOptionsRaw options, bool valid, Action<SaveStage>? fault)
        {
            string before = draft.ContentHash;
            var root = new XElement(N + "settings-bundle", new XAttribute("schema", "1"), new XAttribute("kind", valid ? "next-match" : "raw-draft"), RulesElement(draft.Current), OptionsElement(options));
            Parse(new XDocument(root));
            if (File.Exists(path) && Load(path).IsValidSettings != valid) throw new IOException("正常な次局設定と不正下書きは別ファイルへ保存してください。");
            AtomicFile.Write(path, new XDocument(root).ToString(SaveOptions.DisableFormatting), MaxBytes, fault);
            if (draft.ContentHash == before) draft.MarkSaved();
        }
        internal static XElement RulesElement(DraftData data) => new XElement(N + "rules", new XAttribute("id", data.Id), new XAttribute("name", data.Name), new XAttribute("source", data.SourceId),
            new XAttribute("dimensions", data.Dimensions), new XAttribute("min", data.Min), new XAttribute("max", data.Max), new XAttribute("forwardAxis", data.ForwardAxis), new XAttribute("occlusion", data.Occlusion),
            // Only a non-flat layout is written, so flat-layout files keep their exact former form.
            data.InitialLayout == DraftData.FlatLayout ? null : new XAttribute("initialLayout", data.InitialLayout),
            data.Definitions.Select(d => new XElement(N + "definition", new XAttribute("id", d.Id), new XAttribute("name", d.Name), new XAttribute("kind", d.Kind), new XAttribute("axes", d.AxesMask), new XAttribute("maxAxes", d.MaxAxes), new XAttribute("distance", d.MaxDistance), new XAttribute("forward", d.Forward), new XAttribute("sideways", d.Sideways), new XAttribute("backward", d.Backward), new XAttribute("empty", d.AllowEmpty), new XAttribute("capture", d.AllowCapture))));
        internal static XElement OptionsElement(MatchOptionsRaw raw) => new XElement(N + "options", new XAttribute("timeSeconds", raw.TimeSeconds), new XAttribute("cpuSeed", raw.CpuSeed), new XAttribute("cpuBudget", raw.CpuBudget), new XAttribute("cpuDelayMilliseconds", raw.CpuDelayMilliseconds));
        public static LoadedMatchSettings Load(string path)
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("設定が1MiBを超えています。");
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };
            using (var reader = XmlReader.Create(path, options)) return Parse(XDocument.Load(reader, LoadOptions.None));
        }
        private static void Exact(XElement e, params string[] attributes) => Exact(e, Array.Empty<string>(), attributes);
        private static void Exact(XElement e, string[] optional, params string[] attributes)
        {
            if (e.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || !attributes.Contains(a.Name.LocalName) && !optional.Contains(a.Name.LocalName))) || attributes.Any(a => e.Attribute(a) == null)) throw new InvalidDataException("未知/欠損属性：" + e.Name);
            if (e.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("未知XML内容：" + e.Name);
        }
        private static string A(XElement e, string name) => (string)e.Attribute(name)!;
        private static bool B(XElement e, string name) { string v = A(e, name); return v == "true" ? true : v == "false" ? false : throw new InvalidDataException("不正な真偽値"); }
        // Shared only by the versioned pivot bundle; the legacy schema stays exact.
        internal static LoadedMatchSettings Parse(XDocument document)
        {
            if (document.DocumentType != null || document.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("宣言的XML以外は非対応です。");
            var root = document.Root ?? throw new InvalidDataException("設定rootがありません。");
            if (root.Name != N + "settings-bundle") throw new InvalidDataException("未知schema：元ファイルは保持しました。");
            Exact(root, "schema", "kind");
            string kind = A(root, "kind"); if (A(root, "schema") != "1" || kind != "next-match" && kind != "raw-draft") throw new InvalidDataException("未知schema版/保存種別");
            var children = root.Elements().ToArray();
            if (children.Length != 2 || children[0].Name != N + "rules" || children[1].Name != N + "options") throw new InvalidDataException("未知/欠損要素");
            var rules = children[0]; Exact(rules, new[] { "initialLayout" }, "id", "name", "source", "dimensions", "min", "max", "forwardAxis", "occlusion");
            var data = new DraftData { Id = A(rules, "id"), Name = A(rules, "name"), SourceId = A(rules, "source"), Dimensions = A(rules, "dimensions"), Min = A(rules, "min"), Max = A(rules, "max"), ForwardAxis = A(rules, "forwardAxis"), Occlusion = A(rules, "occlusion"), InitialLayout = (string?)rules.Attribute("initialLayout") ?? DraftData.FlatLayout };
            if (rules.Elements().Count() > 64) throw new InvalidDataException("定義数上限64");
            foreach (var d in rules.Elements())
            {
                if (d.Name != N + "definition" || d.HasElements) throw new InvalidDataException("未知駒定義要素");
                Exact(d, "id", "name", "kind", "axes", "maxAxes", "distance", "forward", "sideways", "backward", "empty", "capture");
                data.Definitions.Add(new RawPieceDefinition { Id = A(d, "id"), Name = A(d, "name"), Kind = A(d, "kind"), AxesMask = A(d, "axes"), MaxAxes = A(d, "maxAxes"), MaxDistance = A(d, "distance"), Forward = B(d, "forward"), Sideways = B(d, "sideways"), Backward = B(d, "backward"), AllowEmpty = B(d, "empty"), AllowCapture = B(d, "capture") });
            }
            var o = children[1]; Exact(o, "timeSeconds", "cpuSeed", "cpuBudget", "cpuDelayMilliseconds"); if (o.HasElements) throw new InvalidDataException("未知options子要素");
            var raw = new MatchOptionsRaw { TimeSeconds = A(o, "timeSeconds"), CpuSeed = A(o, "cpuSeed"), CpuBudget = A(o, "cpuBudget"), CpuDelayMilliseconds = A(o, "cpuDelayMilliseconds") };
            return new LoadedMatchSettings(data, raw, kind == "next-match");
        }
    }
    // The one write-flush-verify-replace sequence used by every store. The original file is touched only by the
    // final Replace/Move, so a fault at any earlier stage leaves it as it was.
    internal static class AtomicFile
    {
        internal static void Write(string path, string text, int maxBytes, Action<SaveStage>? fault)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text); if (bytes.Length > maxBytes) throw new InvalidOperationException("保存サイズ上限超過");
            Write(path, bytes, ".cfg2-", fault);
        }
        // verifyTemporary re-reads the flushed temporary file before it replaces the original.
        internal static void Write(string path, byte[] bytes, string temporaryPrefix, Action<SaveStage>? fault, Action<string>? verifyTemporary = null)
        {
            string full = Path.GetFullPath(path), dir = Path.GetDirectoryName(full)!; Directory.CreateDirectory(dir);
            string temporary = Path.Combine(dir, temporaryPrefix + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                fault?.Invoke(SaveStage.BeforeWrite);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                fault?.Invoke(SaveStage.AfterFlush);
                verifyTemporary?.Invoke(temporary);
                fault?.Invoke(SaveStage.BeforeReplace);
                if (File.Exists(full)) File.Replace(temporary, full, null); else File.Move(temporary, full);
            }
            finally
            {
                // A failed cleanup must not hide the exception that interrupted the save.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
    public static class MatchRecordStore
    {
        private static readonly XNamespace N = "urn:ndshogi:cfg2:match-record/1";
        private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        public static string Serialize(MatchSession session)
        {
            var s = session.Snapshot; var c = s.Config;
            var root = new XElement(N + "match", new XAttribute("schema", "1"), new XAttribute("id", s.MatchId), new XAttribute("mode", s.Mode), new XAttribute("fixture", s.FixtureId),
                new XAttribute("startedUtc", s.StartedUtc.ToString("o", CultureInfo.InvariantCulture)), new XAttribute("ruleId", c.EffectiveRuleId), new XAttribute("ruleVersion", MatchConfig.RuleVersion), new XAttribute("ruleHash", c.RuleHash),
                new XAttribute("settingsHash", c.SettingsHash), new XAttribute("snapshotHash", s.Hash), new XAttribute("initialHash", s.InitialHash), new XAttribute("currentHash", session.State.Hash), new XAttribute("sideA", s.SideA), new XAttribute("sideB", s.SideB),
                new XAttribute("occlusion", c.Preset.OcclusionId), new XAttribute("cpu", MatchConfig.CpuId), new XAttribute("seed", c.CpuSeed), new XAttribute("budget", c.CpuBudget), new XAttribute("clockPolicy", MatchConfig.ClockPolicy),
                new XAttribute("outcome", session.State.Outcome), new XAttribute("winner", session.State.Winner?.ToString() ?? ""), new XAttribute("phase", session.Phase), new XAttribute("aSeconds", D(session.Clock.ASeconds)), new XAttribute("bSeconds", D(session.Clock.BSeconds)),
                MatchSettingsStore.RulesElement(c.Preset.ToDraftData()), MatchSettingsStore.OptionsElement(c.ToRaw()),
                new XElement(N + "initial", new XAttribute("turn", s.InitialState.Turn), s.InitialState.Pieces.Select(p => new XElement(N + "piece", new XAttribute("id", p.Id), new XAttribute("side", p.Side), new XAttribute("kind", p.Kind), new XAttribute("definition", MatchConfig.DefinitionId(p.Kind)), new XAttribute("cell", p.Position)))),
                new XElement(N + "events", session.Events.Select(e =>
                {
                    var item = new XElement(N + "event", new XAttribute("kind", e.Kind), new XAttribute("ply", e.Ply), new XAttribute("before", e.BeforeHash), new XAttribute("after", e.AfterHash), new XAttribute("monotonic", D(e.MonotonicSeconds)), new XAttribute("utc", e.Utc.ToString("o", CultureInfo.InvariantCulture)), new XAttribute("aSeconds", D(e.ASeconds)), new XAttribute("bSeconds", D(e.BSeconds)), new XAttribute("outcome", e.Outcome), new XAttribute("winner", e.Winner?.ToString() ?? ""), new XAttribute("generation", e.Generation), new XAttribute("detail", e.Detail));
                    if (e.Move.HasValue) { var m = e.Move.Value; item.Add(new XElement(N + "move", new XAttribute("piece", m.PieceId), new XAttribute("from", m.From), new XAttribute("to", m.To))); }
                    if (e.Capture != null) { var v = e.Capture.Victim; item.Add(new XElement(N + "capture", new XAttribute("captor", e.Capture.Captor), new XAttribute("victim", v.Id), new XAttribute("formerSide", v.Side), new XAttribute("kind", v.Kind), new XAttribute("cell", v.Position), new XAttribute("tray", v.Kind != PieceKind.King))); }
                    return item;
                })));
            return new XDocument(root).ToString(SaveOptions.DisableFormatting);
        }
        public static void Save(string path, MatchSession session, Action<SaveStage>? fault = null) => AtomicFile.Write(path, Serialize(session), 16 * 1024 * 1024, fault);
    }
}
