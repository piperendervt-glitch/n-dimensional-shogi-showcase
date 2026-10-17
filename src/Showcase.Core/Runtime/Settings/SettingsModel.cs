using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace NDShogi.Showcase.Core.Settings
{
    public enum PreviewMovementKind { Step, H11, H22, Unsupported, Gold, Silver, Knight, Lance }

    /// <summary>Board domain: every one of <see cref="Dimensions"/> axes spans Min..Max. N=4 with -4..4 is the original 4D board.</summary>
    public sealed class BoundsN
    {
        public const int MinDimensions = 3, MaxDimensions = QN.MaxDimensions;
        public int Min { get; }
        public int Max { get; }
        public int Dimensions { get; }
        public BoundsN(int min = -4, int max = 4, int dimensions = 4)
        {
            if (min < -4 || max > 4 || min > max) throw new ArgumentException("Preview bounds must be within the existing -4..4 domain.");
            if (dimensions < MinDimensions || dimensions > MaxDimensions) throw new ArgumentException("N must be " + MinDimensions + ".." + MaxDimensions + ".");
            Min = min; Max = max; Dimensions = dimensions;
        }
        public bool Contains(QN q) => q.Rank <= Dimensions && Enumerable.Range(0, Dimensions).All(i => q.Axis(i) >= Min && q.Axis(i) <= Max);
        /// <summary>The same cell carrying exactly this board's N coordinates (the form that is hashed and saved).</summary>
        public QN Normalize(QN q) => q.Dimensions == Dimensions ? q : QN.Padded(q.ToArray(), Dimensions);
        public QN Origin => QN.Zero(Dimensions);
        public bool SameDomain(BoundsN other) => Min == other.Min && Max == other.Max && Dimensions == other.Dimensions;
        /// <summary>Bit mask with every axis set: the "all axes" value of a piece definition for this N.</summary>
        public static int FullAxesMask(int dimensions) => (1 << dimensions) - 1;
    }

    // Raw input is deliberately separate from a validated preview preset.
    public sealed class RawPieceDefinition
    {
        public string Id = "";
        public string Name = "";
        public string Kind = "step/1";
        public string AxesMask = "15";
        public string MaxAxes = "4";
        public string MaxDistance = "1";
        public bool Forward = true;
        public bool Sideways = true;
        public bool Backward = true;
        public bool AllowEmpty = true;
        public bool AllowCapture = true;
        public RawPieceDefinition Clone() => (RawPieceDefinition)MemberwiseClone();
    }

    public sealed class DraftData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "4D操作・編集用試験設定（製品ルール未採用）";
        public string SourceId = "cfg1-preview-fixture/1";
        public string Dimensions = "4";
        public string Min = "-4";
        public string Max = "4";
        public string ForwardAxis = "0";
        public string Occlusion = "lattice-line/1";
        /// <summary>"flat": the 40 pieces on the F×U plane only. "copy/&lt;axis&gt;": that layout copied to every cell of one more core axis (2..N−1).</summary>
        public string InitialLayout = FlatLayout;
        public const string FlatLayout = "flat";
        public static string CopyLayout(int coreAxis) => "copy/" + coreAxis.ToString(CultureInfo.InvariantCulture);
        /// <summary>The copy axis named by a "copy/&lt;axis&gt;" layout (2 or more); null for the flat layout or malformed text. The validator also requires axis &lt; N.</summary>
        public static int? CopyAxisOf(string? layout)
        {
            var parts = (layout ?? "").Split('/');
            return parts.Length == 2 && parts[0] == "copy" && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axis) && axis >= 2 ? axis : (int?)null;
        }
        public List<RawPieceDefinition> Definitions = new List<RawPieceDefinition>();
        public DraftData Clone()
        {
            var result = (DraftData)MemberwiseClone();
            result.Definitions = Definitions.Select(x => x.Clone()).ToList();
            return result;
        }
        internal string Canonical()
        {
            string S(string text) => text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
            // The flat layout adds nothing, so every hash of an unchanged 4D setting stays as it was.
            return S(Id) + S(Name) + S(SourceId) + S(Dimensions) + S(Min) + S(Max) + S(ForwardAxis) + S(Occlusion) + (InitialLayout == FlatLayout ? "" : S(InitialLayout)) +
                string.Concat(Definitions.Select(d => S(d.Id) + S(d.Name) + S(d.Kind) + S(d.AxesMask) + S(d.MaxAxes) + S(d.MaxDistance) +
                    ":" + d.Forward + ":" + d.Sideways + ":" + d.Backward + ":" + d.AllowEmpty + ":" + d.AllowCapture));
        }
    }

    public sealed class ValidationReport
    {
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;
        public ValidationReport(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        {
            Errors = Array.AsReadOnly(errors.ToArray());
            Warnings = Array.AsReadOnly((warnings ?? Array.Empty<string>()).ToArray());
        }
    }

    public sealed class PieceDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string KindId { get; }
        public PreviewMovementKind Kind { get; }
        public int AxesMask { get; }
        public int MaxAxes { get; }
        public int MaxDistance { get; }
        public bool Forward { get; }
        public bool Sideways { get; }
        public bool Backward { get; }
        public bool AllowEmpty { get; }
        public bool AllowCapture { get; }
        public bool IsSupported => Kind != PreviewMovementKind.Unsupported;
        public bool IsEditable => Kind == PreviewMovementKind.Step || Kind == PreviewMovementKind.H11 || Kind == PreviewMovementKind.H22;
        public bool IsSlider => Kind == PreviewMovementKind.H11 || Kind == PreviewMovementKind.H22 || Kind == PreviewMovementKind.Lance;
        internal PieceKind FixedKind => Kind == PreviewMovementKind.Gold ? PieceKind.Gold : Kind == PreviewMovementKind.Silver ? PieceKind.Silver : Kind == PreviewMovementKind.Knight ? PieceKind.Knight : PieceKind.Lance;
        internal PieceDefinition(RawPieceDefinition raw, PreviewMovementKind kind, int axes, int maxAxes, int maxDistance)
        {
            Id = raw.Id; Name = raw.Name; KindId = raw.Kind; Kind = kind; AxesMask = axes; MaxAxes = maxAxes; MaxDistance = maxDistance;
            Forward = raw.Forward; Sideways = raw.Sideways; Backward = raw.Backward; AllowEmpty = raw.AllowEmpty; AllowCapture = raw.AllowCapture;
        }
        internal bool Allows(QN delta, Side side, int forwardAxis)
        {
            if (!IsSupported) return false;
            // CFG2 fixed components delegate to the existing verified Movement definitions.
            // Legacy IDs remain unsupported; only the explicit cfg2/* version enters here.
            if (!IsEditable) return forwardAxis == 0 && Movement.Allows(FixedKind, side, delta.ToArray());
            int nonzero = 0, maximum = 0;
            for (int axis = 0; axis < delta.Dimensions; axis++)
            {
                int a = Math.Abs(delta[axis]);
                if (a != 0) { if ((AxesMask & (1 << axis)) == 0) return false; nonzero++; }
                maximum = Math.Max(maximum, a);
            }
            if (nonzero == 0) return false;
            int forward = delta[forwardAxis] * (side == Side.A ? 1 : -1);
            if ((forward > 0 && !Forward) || (forward == 0 && !Sideways) || (forward < 0 && !Backward)) return false;
            if (Kind == PreviewMovementKind.Step) return maximum == 1 && nonzero <= MaxAxes;
            if (maximum > MaxDistance) return false;
            if (Kind == PreviewMovementKind.H11) return nonzero == 1;
            return nonzero == 2 && Enumerable.Range(0, delta.Dimensions).Count(i => Math.Abs(delta[i]) == maximum) == 2;
        }
    }

    // Valid for editing/preview only: this type never asserts match legality or compatibility.
    public sealed class PreviewPreset
    {
        private readonly DraftData source;
        public string Id => source.Id;
        public string Name => source.Name;
        public BoundsN Bounds { get; }
        public int ForwardAxis { get; }
        public OcclusionKind Occlusion { get; }
        public string OcclusionId => Occlusion == OcclusionKind.LatticeLine ? "lattice-line/1" : "supercover-closed/1";
        public IReadOnlyList<PieceDefinition> Definitions { get; }
        /// <summary>Core axis the flat initial layout is copied along, or null for the flat layout alone.</summary>
        public int? LayoutCopyAxis { get; }
        public string InitialLayout => source.InitialLayout;
        public string RuleHash { get; }
        public bool CanApply => false;
        public const string ApplyUnavailableReason = "CFG-2待ち：実行可能な対局モードに未接続のため適用できません。保存は編集・試し盤用です。";
        internal PreviewPreset(DraftData data, BoundsN bounds, int forwardAxis, OcclusionKind occlusion, IEnumerable<PieceDefinition> definitions, int? layoutCopyAxis = null)
        {
            source = data.Clone(); Bounds = bounds; ForwardAxis = forwardAxis; Occlusion = occlusion; LayoutCopyAxis = layoutCopyAxis;
            Definitions = Array.AsReadOnly(definitions.ToArray());
            var rule = data.Clone(); rule.Id = ""; rule.Name = ""; rule.SourceId = "";
            foreach (var definition in rule.Definitions) definition.Name = "";
            RuleHash = StableHash.Of("cfg1-preview-rules/1|" + rule.Canonical());
        }
        public PieceDefinition? Find(string id) => Definitions.FirstOrDefault(x => x.Id == id);
        public DraftData ToDraftData() => source.Clone();
    }

    public sealed class SettingsDraft
    {
        private DraftData data;
        private readonly Stack<DraftData> undo = new Stack<DraftData>();
        private readonly Stack<DraftData> redo = new Stack<DraftData>();
        private string savedHash = "";
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public long Revision { get; private set; }
        public DraftData Current => data.Clone();
        public string Id => data.Id;
        public string Name => data.Name;
        public string ContentHash => StableHash.Of(data.Canonical());
        public bool IsDirty => ContentHash != savedHash;
        public bool IsUnapplied => true;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public bool CanApply => false;
        public SettingsDraft(DraftData initial, bool isSaved = false)
        {
            data = initial.Clone(); if (isSaved) savedHash = ContentHash;
        }
        public void Edit(Action<DraftData> edit)
        {
            var next = data.Clone(); edit(next);
            if (next.Canonical() == data.Canonical()) return;
            undo.Push(data); data = next.Clone(); redo.Clear(); Revision++;
        }
        public bool Undo()
        {
            if (!CanUndo) return false;
            redo.Push(data); data = undo.Pop(); Revision++; return true;
        }
        public bool Redo()
        {
            if (!CanRedo) return false;
            undo.Push(data); data = redo.Pop(); Revision++; return true;
        }
        public SettingsDraft Clone() => new SettingsDraft(data);
        public void MarkSaved() => savedHash = ContentHash;
        public bool TryCompile(out PreviewPreset? preset, out ValidationReport report) => RuleValidator.Compile(data, out preset, out report);
        public static SettingsDraft CreateExperiment()
        {
            var data = new DraftData();
            data.Definitions.Add(new RawPieceDefinition { Id = "king-step", Name = "王型（一歩・試験用）" });
            data.Definitions.Add(new RawPieceDefinition { Id = "rook-h11", Name = "飛型 H(1,1)", Kind = "h11/1", MaxDistance = "4" });
            data.Definitions.Add(new RawPieceDefinition { Id = "bishop-h22", Name = "角型 H(2,2)", Kind = "h22/1", MaxDistance = "4" });
            data.Definitions.Add(new RawPieceDefinition { Id = "gold-legacy", Name = "金（既存成分・読取のみ）", Kind = "legacy/gold/1" });
            data.Definitions.Add(new RawPieceDefinition { Id = "silver-legacy", Name = "銀（既存成分・読取のみ）", Kind = "legacy/silver/1" });
            data.Definitions.Add(new RawPieceDefinition { Id = "knight-legacy", Name = "桂（跳び・未対応）", Kind = "legacy/knight/1" });
            data.Definitions.Add(new RawPieceDefinition { Id = "lance-legacy", Name = "香（既存成分・読取のみ）", Kind = "legacy/lance/1" });
            data.Definitions.Add(new RawPieceDefinition { Id = "pawn-step", Name = "歩型（一歩・試験用）", MaxAxes = "1", Backward = false });
            foreach (var promoted in new[] { ("rook", "龍"), ("bishop", "馬"), ("silver", "成銀"), ("knight", "成桂"), ("lance", "成香"), ("pawn", "と金") })
                data.Definitions.Add(new RawPieceDefinition { Id = "promoted-" + promoted.Item1 + "-legacy", Name = promoted.Item2 + "（成り・複合成分は未対応）", Kind = "legacy/promoted-" + promoted.Item1 + "/1" });
            return new SettingsDraft(data);
        }
        public static SettingsDraft CreateMatchDemo()
        {
            var data = CreateExperiment().Current;
            data.Name = "4D王捕獲デモ"; data.SourceId = "CFG2_DEMO_4D/1";
            foreach (var pair in new[] { ("gold", "金", 1), ("silver", "銀", 1), ("knight", "桂", 2), ("lance", "香", 8) })
            {
                var d = data.Definitions.Single(x => x.Id == pair.Item1 + "-legacy");
                d.Kind = "cfg2/" + pair.Item1 + "/1"; d.Name = pair.Item2 + "（固定定義）"; d.MaxDistance = pair.Item3.ToString(CultureInfo.InvariantCulture);
            }
            data.Definitions.Single(x => x.Id == "king-step").Name = "王（一歩）";
            data.Definitions.Single(x => x.Id == "pawn-step").Name = "歩（一歩）";
            return new SettingsDraft(data);
        }
    }

    public static class RuleValidator
    {
        public static bool Compile(DraftData data, out PreviewPreset? preset, out ValidationReport report)
        {
            var errors = new List<string>(); var warnings = new List<string>(); var definitions = new List<PieceDefinition>();
            int Number(string raw, string name, int min, int max)
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
                { errors.Add(name + "：整数 " + min + "〜" + max + " を入力してください。入力は保持されています。"); return min; }
                return value;
            }
            bool ValidText(string value, int maximum) => value != null && value.Length > 0 && value.Length <= maximum && !value.Any(char.IsControl);
            if (!ValidText(data.Id, 128) || !ValidText(data.Name, 256) || !ValidText(data.SourceId, 256)) errors.Add("設定ID・名称・出所IDが不正です。");
            int dimensions = Number(data.Dimensions, "N（次元数）", BoundsN.MinDimensions, BoundsN.MaxDimensions);
            int fullMask = BoundsN.FullAxesMask(dimensions);
            int min = Number(data.Min, "盤最小値", -4, 4), max = Number(data.Max, "盤最大値", -4, 4);
            if (min > max) errors.Add("盤最小値が最大値を超えています。");
            int forward = Number(data.ForwardAxis, "前方軸", 0, dimensions - 1);
            // The layout plane is F×U, so a copy axis is any other axis of the board.
            int? copyAxis = null;
            if (data.InitialLayout != DraftData.FlatLayout)
            {
                int? axis = DraftData.CopyAxisOf(data.InitialLayout);
                if (axis.HasValue && axis.Value < dimensions) copyAxis = axis;
                else errors.Add("初期配置：flat または copy/<軸番号 2〜" + (dimensions - 1) + "> を指定してください。入力は保持されています。");
            }
            OcclusionKind occlusion = OcclusionKind.LatticeLine;
            if (data.Occlusion == "lattice-line/1") occlusion = OcclusionKind.LatticeLine;
            else if (data.Occlusion == "supercover-closed/1") occlusion = OcclusionKind.SupercoverClosed;
            else errors.Add("未知・未対応の遮蔽方式：" + data.Occlusion);
            if (data.Definitions.Count == 0 || data.Definitions.Count > 64) errors.Add("駒定義数は1〜64です。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in data.Definitions)
            {
                if (!ValidText(raw.Id, 128) || !ids.Add(raw.Id)) errors.Add("駒定義IDが不正または重複しています：" + raw.Id);
                if (!ValidText(raw.Name, 256)) errors.Add("駒名称が不正です：" + raw.Id);
                PreviewMovementKind kind = raw.Kind == "step/1" ? PreviewMovementKind.Step : raw.Kind == "h11/1" ? PreviewMovementKind.H11 : raw.Kind == "h22/1" ? PreviewMovementKind.H22 : PreviewMovementKind.Unsupported;
                if (raw.Kind == "cfg2/gold/1") kind = PreviewMovementKind.Gold;
                else if (raw.Kind == "cfg2/silver/1") kind = PreviewMovementKind.Silver;
                else if (raw.Kind == "cfg2/knight/1") kind = PreviewMovementKind.Knight;
                else if (raw.Kind == "cfg2/lance/1") kind = PreviewMovementKind.Lance;
                int axes = Number(raw.AxesMask, raw.Id + " 使用軸mask", 1, fullMask);
                int maxAxes = Number(raw.MaxAxes, raw.Id + " 同時軸数", 1, dimensions);
                int distance = Number(raw.MaxDistance, raw.Id + " 最大座標変位", 1, 8);
                if (kind == PreviewMovementKind.Step && distance != 1) errors.Add(raw.Id + "：一歩は各軸±1です。距離を変更して別方式へ置換できません。");
                if (kind == PreviewMovementKind.Gold || kind == PreviewMovementKind.Silver || kind == PreviewMovementKind.Knight || kind == PreviewMovementKind.Lance)
                {
                    int fixedDistance = kind == PreviewMovementKind.Knight ? 2 : kind == PreviewMovementKind.Lance ? 8 : 1;
                    if (forward != 0 || axes != fullMask || maxAxes != dimensions || distance != fixedDistance || !raw.Forward || !raw.Sideways || !raw.Backward || !raw.AllowEmpty || !raw.AllowCapture)
                        errors.Add(raw.Id + "：CFG2固定成分の編集は未対応です。既存Movement定義を変更しません。");
                }
                if (kind == PreviewMovementKind.Unsupported)
                {
                    var knownLegacy = new[] { "legacy/gold/1", "legacy/silver/1", "legacy/knight/1", "legacy/lance/1", "legacy/promoted-rook/1", "legacy/promoted-bishop/1", "legacy/promoted-silver/1", "legacy/promoted-knight/1", "legacy/promoted-lance/1", "legacy/promoted-pawn/1" };
                    if (!knownLegacy.Contains(raw.Kind)) errors.Add(raw.Id + "：未知・未対応の移動機能 " + raw.Kind);
                    else warnings.Add(raw.Name + "：既存成分は読取専用。初回の共通G/Pには未対応です。");
                }
                if (axes != fullMask) warnings.Add(raw.Name + "：軸対称性を崩す実験設定。Nと他駒座標は変わりません。");
                definitions.Add(new PieceDefinition(raw, kind, axes, maxAxes, distance));
            }
            report = new ValidationReport(errors, warnings);
            preset = report.IsValid ? new PreviewPreset(data, new BoundsN(min, max, dimensions), forward, occlusion, definitions, copyAxis) : null;
            return report.IsValid;
        }
    }
}

