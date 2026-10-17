using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NDShogi.Showcase.Core.Match;

namespace NDShogi.Showcase.Core.Settings
{
    public sealed class LoadedPivotSettings
    {
        public SettingsDraft Draft { get; }
        public MatchOptionsRaw Options { get; }
        public PreviewSession Preview { get; }
        public PivotViewSnapshot View { get; }
        public MatchConfig? Config { get; }
        public ValidationReport Validation { get; }
        public bool IsValidSettings { get; }
        public string LogicalHash { get; }
        public string PreviewHash => Preview.Hash;
        public string MigrationNotice { get; }
        internal LoadedPivotSettings(LoadedMatchSettings logical, PreviewSession preview, PivotViewSnapshot view, string migrationNotice = "")
        {
            Draft = logical.Draft; Options = logical.Options; Config = logical.Config; IsValidSettings = logical.IsValidSettings;
            Preview = preview; View = view; MigrationNotice = migrationNotice;
            LogicalHash = PivotSettingsStore.LogicalContentHash(Draft, Options);
            var errors = logical.Validation.Errors.ToList();
            if (preview.Pieces.Any(p => !Draft.Current.Definitions.Any(d => d.Id == p.DefinitionId))) errors.Add("試し盤に未知の駒定義参照があります。原文を保持し、G/Pは未評価です。");
            if (Draft.TryCompile(out var preset, out _) && (preset!.Bounds.Min != preview.Bounds.Min || preset.Bounds.Max != preview.Bounds.Max)) errors.Add("設定と試し盤の盤域が一致しません。");
            if (!string.IsNullOrEmpty(view.SelectedPieceId) && preview.Find(view.SelectedPieceId) == null) throw new InvalidDataException("保存表示の選択駒が対応する試し盤にありません。");
            if (!preview.Bounds.Contains(view.SelectedCell) || !preview.Bounds.Contains(view.PlaneReferenceCell)) throw new InvalidDataException("表示の基準セルが対応する試し盤の範囲外です。");
            Validation = new ValidationReport(errors, logical.Validation.Warnings);
            if (IsValidSettings && !Validation.IsValid) throw new InvalidDataException("正常設定bundleの試し盤が不正です：" + string.Join(" / ", errors));
        }
    }

    // A new document and path; cfg1:view/1 and cfg2:settings/1 are not extended.
    public static class PivotSettingsStore
    {
        private static readonly XNamespace N = "urn:ndshogi:pivot:settings-bundle/1";
        private static readonly XNamespace V = "urn:ndshogi:pivot:view/1";
        private static readonly XNamespace T = "urn:ndshogi:pivot:preview/1";
        private const int MaxBytes = 1024 * 1024;
        public const string ValidFileName = "pivot-next-match.xml";
        public const string DraftFileName = "pivot-invalid-draft.xml";

        public static string LogicalContentHash(SettingsDraft draft, MatchOptionsRaw options)
        {
            string S(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
            return StableHash.Of("pivot-logical/1|" + draft.Current.Canonical() + S(options.TimeSeconds) + S(options.CpuSeed) + S(options.CpuBudget) + S(options.CpuDelayMilliseconds));
        }
        private static string PreviewBinding(PreviewSession preview) => StableHash.Of("pivot-preview/1|" + preview.Bounds.Min.ToString(CultureInfo.InvariantCulture) + "|" + preview.Bounds.Max.ToString(CultureInfo.InvariantCulture) + "|" + preview.Hash);
        public static void SaveValid(string path, SettingsDraft draft, MatchOptionsRaw options, PreviewSession preview, PivotViewSnapshot view, Action<SaveStage>? fault = null)
            => Save(path, draft, options, preview, view, true, fault);
        public static void SaveDraft(string path, SettingsDraft draft, MatchOptionsRaw options, PreviewSession preview, PivotViewSnapshot view, Action<SaveStage>? fault = null)
            => Save(path, draft, options, preview, view, false, fault);
        private static XDocument LogicalDocument(SettingsDraft draft, MatchOptionsRaw options, bool valid) => new XDocument(new XElement(MatchSettingsStore.N + "settings-bundle",
            new XAttribute("schema", "1"), new XAttribute("kind", valid ? "next-match" : "raw-draft"), MatchSettingsStore.RulesElement(draft.Current), MatchSettingsStore.OptionsElement(options)));
        private static XDocument Document(SettingsDraft draft, MatchOptionsRaw options, PreviewSession preview, PivotViewSnapshot view, bool valid)
        {
            string logicalHash = LogicalContentHash(draft, options), previewHash = PreviewBinding(preview);
            var trial = new XElement(T + "preview", new XAttribute("min", preview.Bounds.Min), new XAttribute("max", preview.Bounds.Max), new XAttribute("hash", previewHash),
                preview.Pieces.Select(p => new XElement(T + "piece", new XAttribute("id", p.Id), new XAttribute("definition", p.DefinitionId), new XAttribute("side", p.Side), new XAttribute("cell", p.Position))));
            var display = new XElement(V + "view", new XAttribute("schema", "1"), new XAttribute("embedding", view.EmbeddingId), new XAttribute("height", D(view.Height)),
                new XAttribute("center", Vector(view.Center)), new XAttribute("basis", Vector(view.Basis)), new XAttribute("anchor", Vector(view.Anchor)), new XAttribute("halfWindow", D(view.HalfWindow)),
                new XAttribute("cellScale", D(view.CellScale)), new XAttribute("camera", Vector(view.Camera)), new XAttribute("selectedPiece", view.SelectedPieceId), new XAttribute("selectedCell", view.SelectedCell),
                new XAttribute("planeReference", view.PlaneReferenceCell), new XAttribute("logicalHash", logicalHash), new XAttribute("previewHash", previewHash));
            return new XDocument(new XElement(N + "bundle", new XAttribute("schema", "1"), new XAttribute("logicalHash", logicalHash), LogicalDocument(draft, options, valid).Root!, trial, display));
        }
        private static void Save(string path, SettingsDraft draft, MatchOptionsRaw options, PreviewSession preview, PivotViewSnapshot view, bool valid, Action<SaveStage>? fault)
        {
            string before = draft.ContentHash, logicalBefore = LogicalContentHash(draft, options);
            var document = Document(new SettingsDraft(draft.Current), options.Clone(), new PreviewSession(preview.Bounds, preview.Snapshot().Pieces), view, valid);
            Parse(document); // Entire coherent snapshot must validate before any old file is touched.
            byte[] bytes = new UTF8Encoding(false).GetBytes(document.ToString(SaveOptions.DisableFormatting));
            if (bytes.Length > MaxBytes) throw new InvalidOperationException("保存bundleが上限1 MiBを超えています。");
            string full = Path.GetFullPath(path), directory = Path.GetDirectoryName(full)!;
            if (File.Exists(full) && Load(full).IsValidSettings != valid) throw new IOException("正常設定bundleと不正下書きbundleは別ファイルへ保存してください。");
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".pivot-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                fault?.Invoke(SaveStage.BeforeWrite);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                fault?.Invoke(SaveStage.AfterFlush);
                Load(temporary); // Re-read flushed file, not merely the in-memory XDocument.
                fault?.Invoke(SaveStage.BeforeReplace);
                if (File.Exists(full)) File.Replace(temporary, full, null); else File.Move(temporary, full);
                if (draft.ContentHash == before && LogicalContentHash(draft, options) == logicalBefore) draft.MarkSaved();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static LoadedPivotSettings Load(string path)
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("保存bundleが上限1 MiBを超えています。");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes, IgnoreComments = false };
            using (var reader = XmlReader.Create(path, settings)) return Parse(XDocument.Load(reader, LoadOptions.None));
        }
        public static LoadedPivotSettings ImportLegacy(string matchPath, string? trialPath, PivotViewSnapshot initialView)
        {
            // Explicit migration only. Reading known formats never rewrites or moves them.
            var logical = MatchSettingsStore.Load(matchPath);
            var preview = PreviewSession.CreateExperiment();
            if (trialPath != null)
            {
                var legacy = SettingsStore.Load(trialPath);
                if (legacy.Draft.ContentHash != logical.Draft.ContentHash) throw new InvalidDataException("旧設定と旧試し盤の論理設定hashが一致しません。異なる保存を混合していません。");
                preview = legacy.Preview;
            }
            return new LoadedPivotSettings(logical, preview, initialView, "表示設定を新方式の初期状態にした。旧保存ファイルは変更していません。");
        }
        private static string D(double number) => number.ToString("R", CultureInfo.InvariantCulture);
        private static string Vector(double[] values) => string.Join(",", values.Select(D));
        private static string A(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("必須属性欠損：" + name);
        private static void Exact(XElement e, params string[] attributes)
        {
            if (e.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || !attributes.Contains(a.Name.LocalName))) || attributes.Any(a => e.Attribute(a) == null)) throw new InvalidDataException("未知/欠損属性：" + e.Name);
            if (e.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("未知XML内容：" + e.Name);
        }
        private static int Integer(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : throw new InvalidDataException("整数が必要です。");
        private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : throw new InvalidDataException("有限数値が必要です。");
        private static double[] Vector(string text, int size) { var values = text.Split(','); if (values.Length != size) throw new InvalidDataException("ベクトル次元が不正です。"); return values.Select(Number).ToArray(); }
        private static Q4 Cell(string text) { var v = text.Split(','); if (v.Length != 4) throw new InvalidDataException("4Dセルが必要です。"); return new Q4(Integer(v[0]), Integer(v[1]), Integer(v[2]), Integer(v[3])); }
        private static LoadedPivotSettings Parse(XDocument document)
        {
            if (document.DocumentType != null || document.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("宣言的XML以外は対応しません。");
            var root = document.Root ?? throw new InvalidDataException("bundleがありません。");
            if (root.Name != N + "bundle") throw new InvalidDataException("未知のbundle schemaです。元ファイルは保持されています。");
            Exact(root, "schema", "logicalHash"); if (A(root, "schema") != "1") throw new InvalidDataException("未知のbundle版です。");
            var children = root.Elements().ToArray();
            if (children.Length != 3 || children[0].Name != MatchSettingsStore.N + "settings-bundle" || children[1].Name != T + "preview" || children[2].Name != V + "view") throw new InvalidDataException("未知/欠損/重複のbundle要素です。");
            var logical = MatchSettingsStore.Parse(new XDocument(new XElement(children[0])));
            string logicalHash = LogicalContentHash(logical.Draft, logical.Options);
            if (A(root, "logicalHash") != logicalHash) throw new InvalidDataException("論理設定hashが一致しません。");
            var trial = children[1]; Exact(trial, "min", "max", "hash");
            if (trial.Elements().Count() > 256) throw new InvalidDataException("試し駒数の上限256を超えています。");
            var pieces = trial.Elements().Select(p =>
            {
                if (p.Name != T + "piece" || p.HasElements) throw new InvalidDataException("未知の試し駒要素です。");
                Exact(p, "id", "definition", "side", "cell"); string side = A(p, "side");
                if (side != "A" && side != "B") throw new InvalidDataException("未知の陣営です。");
                return new TrialPiece(A(p, "id"), A(p, "definition"), side == "A" ? Side.A : Side.B, Cell(A(p, "cell")));
            }).ToArray();
            var preview = new PreviewSession(new Bounds4D(Integer(A(trial, "min")), Integer(A(trial, "max"))), pieces);
            string previewHash = PreviewBinding(preview);
            if (A(trial, "hash") != previewHash) throw new InvalidDataException("試し盤hashが一致しません。");
            var v = children[2]; if (v.HasElements) throw new InvalidDataException("未知の表示子要素です。");
            Exact(v, "schema", "embedding", "height", "center", "basis", "anchor", "halfWindow", "cellScale", "camera", "selectedPiece", "selectedCell", "planeReference", "logicalHash", "previewHash");
            if (A(v, "schema") != "1") throw new InvalidDataException("未知の表示schema版です。");
            if (A(v, "logicalHash") != logicalHash || A(v, "previewHash") != previewHash) throw new InvalidDataException("表示と論理設定・試し盤の対応hashが一致しません。");
            var view = new PivotViewSnapshot(Number(A(v, "height")), Vector(A(v, "center"), 4), Vector(A(v, "basis"), 16), Vector(A(v, "anchor"), 3),
                Number(A(v, "halfWindow")), Number(A(v, "cellScale")), Vector(A(v, "camera"), 6), A(v, "selectedPiece"), Cell(A(v, "selectedCell")), Cell(A(v, "planeReference")), A(v, "embedding"));
            return new LoadedPivotSettings(logical, preview, view);
        }
    }
}
