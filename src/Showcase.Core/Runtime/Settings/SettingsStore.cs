using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NDShogi.Showcase.Core.Settings
{
    public enum SavedSettingsKind { PreviewPreset, Draft }
    public enum SaveStage { BeforeWrite, AfterFlush, BeforeReplace }

    public sealed class ViewSnapshot
    {
        public double[] Center { get; }
        public double[] Basis { get; }
        public double[] Camera { get; }
        public ViewSnapshot(double[] center, double[] basis, double[] camera)
        {
            if (center.Length != 4 || basis.Length != 16 || camera.Length != 6) throw new ArgumentException("View dimensions are center4 / basis16 / camera6.");
            if (center.Concat(basis).Concat(camera).Any(v => double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1000000)) throw new ArgumentException("View values must be finite and bounded.");
            if (camera[2] <= 0) throw new ArgumentException("Camera distance must be positive.");
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
            {
                double dot = 0; for (int i = 0; i < 4; i++) dot += basis[a * 4 + i] * basis[b * 4 + i];
                if (Math.Abs(dot - (a == b ? 1 : 0)) > 1e-6) throw new ArgumentException("Saved 4D view basis must be orthonormal.");
            }
            Center = (double[])center.Clone(); Basis = (double[])basis.Clone(); Camera = (double[])camera.Clone();
        }
        public static ViewSnapshot Default => new ViewSnapshot(new double[4], new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }, new double[] { 30, 25, 12, 0, 0, 0 });
    }

    public sealed class LoadedSettings
    {
        public SavedSettingsKind Kind { get; }
        public SettingsDraft Draft { get; }
        public PreviewSession Preview { get; }
        public ViewSnapshot View { get; }
        public ValidationReport Validation { get; }
        internal LoadedSettings(SavedSettingsKind kind, DraftData data, PreviewSession preview, ViewSnapshot view)
        {
            Kind = kind; Draft = new SettingsDraft(data, true); Preview = preview; View = view;
            Draft.TryCompile(out var compiled, out var validation);
            var errors = validation.Errors.ToList();
            if (preview.Pieces.Any(p => !data.Definitions.Any(d => d.Id == p.DefinitionId))) errors.Add("試し盤に未知の駒定義参照があります。原文を保持し、G/Pは未評価です。");
            if (compiled != null && compiled.Bounds.Dimensions != SettingsStore.Dimensions) errors.Add(SettingsStore.FourDimensionalOnly(compiled.Bounds.Dimensions));
            else if (compiled != null && !compiled.Bounds.SameDomain(preview.Bounds)) errors.Add("下書きと試し盤の盤域が一致しません。自動変換していません。");
            Validation = new ValidationReport(errors, validation.Warnings);
        }
    }

    // Declarative, bounded XML only. DTDs and external resolution are prohibited.
    public static class SettingsStore
    {
        private static readonly XNamespace S = "urn:ndshogi:cfg1:settings/1";
        private static readonly XNamespace T = "urn:ndshogi:cfg1:preview/1";
        private static readonly XNamespace V = "urn:ndshogi:cfg1:view/1";
        private const int MaxBytes = 1024 * 1024;
        /// <summary>CFG1 files are 4D only: the trial board is read back on a 4D board and the view is center4/basis16.
        /// A preset of another N, or a trial board of another N, is refused rather than silently read as 4D.</summary>
        public const int Dimensions = 4;
        internal static string FourDimensionalOnly(int n) => "CFG1の保存形式は4次元（N=4）専用です。N=" + n + "の設定・試し盤は保存・読込できません（N≠4はCFG2で扱います）。";
        public static void SavePreset(string path, SettingsDraft draft, PreviewSession preview, ViewSnapshot view, bool saveAs = false, Action<SaveStage>? fault = null)
        {
            if (!draft.TryCompile(out _, out var report)) throw new InvalidOperationException("編集・試し盤用プリセットを保存できません。不正入力は下書き保存してください。 " + string.Join(" / ", report.Errors));
            Save(path, draft, preview, view, SavedSettingsKind.PreviewPreset, saveAs, fault);
        }
        public static void SaveDraft(string path, SettingsDraft draft, PreviewSession preview, ViewSnapshot view, bool saveAs = false, Action<SaveStage>? fault = null)
            => Save(path, draft, preview, view, SavedSettingsKind.Draft, saveAs, fault);
        private static void Save(string path, SettingsDraft draft, PreviewSession preview, ViewSnapshot view, SavedSettingsKind kind, bool saveAs, Action<SaveStage>? fault)
        {
            string full = Path.GetFullPath(path); string directory = Path.GetDirectoryName(full)!;
            string beforeHash = draft.ContentHash; var data = draft.Current;
            if (saveAs) { if (File.Exists(full)) throw new IOException("別名保存先は新しいファイルを指定してください。"); data.Id = Guid.NewGuid().ToString("N"); }
            var snapshot = preview.Snapshot();
            // Copy and revalidate mutable arrays at the persistence boundary.
            var viewCopy = new ViewSnapshot(view.Center, view.Basis, view.Camera);
            if (preview.Bounds.Dimensions != Dimensions) throw new InvalidOperationException(FourDimensionalOnly(preview.Bounds.Dimensions));
            if (kind == SavedSettingsKind.PreviewPreset && snapshot.Pieces.Any(p => !data.Definitions.Any(d => d.Id == p.DefinitionId))) throw new InvalidOperationException("試し盤に未知の駒定義参照があります。保存前に明示的に修正してください。");
            if (kind == SavedSettingsKind.PreviewPreset && (!RuleValidator.Compile(data, out var preset, out _) || !preset!.Bounds.SameDomain(preview.Bounds)))
                throw new InvalidOperationException(preset != null && preset.Bounds.Dimensions != Dimensions ? FourDimensionalOnly(preset.Bounds.Dimensions) : "設定と試し盤の盤域が一致しません。");
            string kindId = kind == SavedSettingsKind.PreviewPreset ? "preview-preset" : "draft";
            if (File.Exists(full))
            {
                var prior = Load(full); if (prior.Kind != kind) throw new IOException("不正下書きと検証済み編集プリセットは別ファイルです。旧保存版を上書きできません。");
            }
            var definitions = data.Definitions.Select(d => new XElement(S + "definition",
                new XAttribute("id", d.Id), new XAttribute("name", d.Name), new XAttribute("kind", d.Kind), new XAttribute("axes", d.AxesMask), new XAttribute("maxAxes", d.MaxAxes), new XAttribute("distance", d.MaxDistance),
                new XAttribute("forward", d.Forward), new XAttribute("sideways", d.Sideways), new XAttribute("backward", d.Backward), new XAttribute("empty", d.AllowEmpty), new XAttribute("capture", d.AllowCapture)));
            var settings = new XElement(S + "settings", new XAttribute("id", data.Id), new XAttribute("name", data.Name), new XAttribute("source", data.SourceId), new XAttribute("dimensions", data.Dimensions),
                new XAttribute("min", data.Min), new XAttribute("max", data.Max), new XAttribute("forwardAxis", data.ForwardAxis), new XAttribute("occlusion", data.Occlusion), definitions);
            // Written only when it says something, so a flat-layout file is byte-identical to one saved before the attribute existed.
            if (data.InitialLayout != DraftData.FlatLayout) settings.Add(new XAttribute("initialLayout", data.InitialLayout));
            var trial = new XElement(T + "preview", new XAttribute("min", preview.Bounds.Min), new XAttribute("max", preview.Bounds.Max), snapshot.Pieces.Select(p => new XElement(T + "piece", new XAttribute("id", p.Id), new XAttribute("definition", p.DefinitionId), new XAttribute("side", p.Side.ToString()), new XAttribute("cell", p.Position.ToString()))));
            string Vector(double[] values) => string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
            var viewElement = new XElement(V + "view", new XAttribute("center", Vector(viewCopy.Center)), new XAttribute("basis", Vector(viewCopy.Basis)), new XAttribute("camera", Vector(viewCopy.Camera)));
            var document = new XDocument(new XElement(S + "bundle", new XAttribute("schema", "1"), new XAttribute("kind", kindId), settings, trial, viewElement));
            byte[] bytes = new UTF8Encoding(false).GetBytes(document.ToString(SaveOptions.DisableFormatting));
            if (bytes.Length > MaxBytes) throw new InvalidOperationException("保存データが上限1 MiBを超えています。");
            // Round-trip validation happens before touching an old file.
            Parse(document);
            Match.AtomicFile.Write(full, bytes, ".cfg1-", fault);
            if (draft.ContentHash == beforeHash)
            {
                if (saveAs) draft.Edit(d => d.Id = data.Id);
                draft.MarkSaved();
            }
        }
        public static LoadedSettings Load(string path)
        {
            var info = new FileInfo(path); if (info.Length > MaxBytes) throw new InvalidDataException("設定ファイルが上限1 MiBを超えています。");
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes, IgnoreComments = false };
            using (var reader = XmlReader.Create(path, options)) return Parse(XDocument.Load(reader, LoadOptions.None));
        }
        private static string Attr(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("必須属性がありません：" + name);
        private static void Attributes(XElement e, params string[] names) => Attributes(e, Array.Empty<string>(), names);
        private static void Attributes(XElement e, string[] optional, params string[] names)
        {
            if (e.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || !names.Contains(a.Name.LocalName) && !optional.Contains(a.Name.LocalName))) || names.Any(n => e.Attribute(n) == null)) throw new InvalidDataException("未知または欠損した属性：" + e.Name);
            if (e.Nodes().Any(n => !(n is XElement) && !(n is XText text && string.IsNullOrWhiteSpace(text.Value)))) throw new InvalidDataException("未知のXML内容：" + e.Name);
        }
        private static int Int(string value)
        { if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) throw new InvalidDataException("整数が必要です。"); return result; }
        private static bool Bool(string value)
        { if (value == "true") return true; if (value == "false") return false; throw new InvalidDataException("真偽値が必要です。"); }
        private static double[] Vector(string value, int length)
        {
            var values = value.Split(','); if (values.Length != length) throw new InvalidDataException("ベクトルの次元が不正です。");
            return values.Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : throw new InvalidDataException("有限数値が必要です。")).ToArray();
        }
        private static LoadedSettings Parse(XDocument document)
        {
            if (document.DocumentType != null || document.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("宣言的XML要素以外は対応しません。");
            var root = document.Root ?? throw new InvalidDataException("設定がありません。");
            if (root.Name != S + "bundle") throw new InvalidDataException("未知の設定schemaです。変換も削除もしていません。");
            Attributes(root, "schema", "kind"); if (Attr(root, "schema") != "1") throw new InvalidDataException("未知のschema版です。元ファイルは保持されています。");
            var kindText = Attr(root, "kind"); if (kindText != "draft" && kindText != "preview-preset") throw new InvalidDataException("未知の保存種別です。");
            var children = root.Elements().ToArray();
            if (children.Length != 3 || children[0].Name != S + "settings" || children[1].Name != T + "preview" || children[2].Name != V + "view") throw new InvalidDataException("未知・欠損・重複の設定要素です。");
            var s = children[0]; Attributes(s, new[] { "initialLayout" }, "id", "name", "source", "dimensions", "min", "max", "forwardAxis", "occlusion");
            var data = new DraftData { Id = Attr(s, "id"), Name = Attr(s, "name"), SourceId = Attr(s, "source"), Dimensions = Attr(s, "dimensions"), Min = Attr(s, "min"), Max = Attr(s, "max"), ForwardAxis = Attr(s, "forwardAxis"), Occlusion = Attr(s, "occlusion"), InitialLayout = (string?)s.Attribute("initialLayout") ?? DraftData.FlatLayout };
            if (s.Elements().Count() > 64) throw new InvalidDataException("駒定義数が上限を超えています。");
            foreach (var d in s.Elements())
            {
                if (d.Name != S + "definition" || d.HasElements) throw new InvalidDataException("未知の駒定義要素です。");
                Attributes(d, "id", "name", "kind", "axes", "maxAxes", "distance", "forward", "sideways", "backward", "empty", "capture");
                data.Definitions.Add(new RawPieceDefinition { Id = Attr(d, "id"), Name = Attr(d, "name"), Kind = Attr(d, "kind"), AxesMask = Attr(d, "axes"), MaxAxes = Attr(d, "maxAxes"), MaxDistance = Attr(d, "distance"), Forward = Bool(Attr(d, "forward")), Sideways = Bool(Attr(d, "sideways")), Backward = Bool(Attr(d, "backward")), AllowEmpty = Bool(Attr(d, "empty")), AllowCapture = Bool(Attr(d, "capture")) });
            }
            var t = children[1]; Attributes(t, "min", "max"); var bounds = new BoundsN(Int(Attr(t, "min")), Int(Attr(t, "max")), Dimensions);
            var pieces = t.Elements().Select(p =>
            {
                if (p.Name != T + "piece" || p.HasElements) throw new InvalidDataException("未知の試し駒要素です。");
                Attributes(p, "id", "definition", "side", "cell");
                if (!QN.TryParse(Attr(p, "cell"), 0, out var cell) || cell.Dimensions < BoundsN.MinDimensions) throw new InvalidDataException("N次元座標が必要です。");
                string side = Attr(p, "side"); if (side != "A" && side != "B") throw new InvalidDataException("未知の陣営です。");
                string definition = Attr(p, "definition"); if (kindText == "preview-preset" && !data.Definitions.Any(d => d.Id == definition)) throw new InvalidDataException("未知の試し駒定義参照です。");
                return new TrialPiece(Attr(p, "id"), definition, side == "A" ? Side.A : Side.B, cell);
            }).ToArray();
            var preview = new PreviewSession(bounds, pieces);
            var v = children[2]; if (v.HasElements) throw new InvalidDataException("未知の表示設定要素です。"); Attributes(v, "center", "basis", "camera");
            var view = new ViewSnapshot(Vector(Attr(v, "center"), 4), Vector(Attr(v, "basis"), 16), Vector(Attr(v, "camera"), 6));
            var kind = kindText == "draft" ? SavedSettingsKind.Draft : SavedSettingsKind.PreviewPreset;
            if (kind == SavedSettingsKind.PreviewPreset && (!RuleValidator.Compile(data, out var preset, out _) || !preset!.Bounds.SameDomain(bounds)))
                throw new InvalidDataException(preset != null && preset.Bounds.Dimensions != Dimensions ? FourDimensionalOnly(preset.Bounds.Dimensions) : "検証済み編集プリセットの内容が不正です。下書きへ黙って変換しません。");
            return new LoadedSettings(kind, data, preview, view);
        }
    }
}

