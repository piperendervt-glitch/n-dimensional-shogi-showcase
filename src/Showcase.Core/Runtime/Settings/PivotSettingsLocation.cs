using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NDShogi.Showcase.Core.Match;

namespace NDShogi.Showcase.Core.Settings
{
    // A recovery target is selected without renaming or replacing the rejected
    // original bundle. The pointer can name only one sibling XML bundle.
    public static class PivotSettingsLocation
    {
        public const string SelectorFileName = "pivot-selected-bundle.xml";
        private static readonly XNamespace N = "urn:ndshogi:pivot:settings-location/1";
        private const int MaxBytes = 4096;

        public static string Resolve(string runtimeRoot, string defaultPath)
        {
            string root = Root(runtimeRoot), fallback = Sibling(root, defaultPath);
            string selector = Path.Combine(root, SelectorFileName);
            if (!File.Exists(selector)) return fallback;
            string selected = Path.Combine(root, ReadName(selector));
            RequireValid(selected);
            return selected;
        }

        public static void SaveRecovery(string runtimeRoot, string bundlePath, SettingsDraft draft, MatchOptionsRaw options,
            PreviewSession preview, PivotViewSnapshot view, Action<SaveStage>? bundleFault = null, Action<SaveStage>? selectorFault = null)
        {
            string root = Root(runtimeRoot), bundle = Sibling(root, bundlePath);
            if (File.Exists(bundle) || Directory.Exists(bundle)) throw new IOException("復旧先には未使用の新しいファイル名が必要です。");
            string selector = Path.Combine(root, SelectorFileName);
            if (File.Exists(selector)) ReadName(selector);
            string logicalHash = PivotSettingsStore.LogicalContentHash(draft, options), previewHash = preview.Hash;
            // A durable candidate alone is not a completed recovery. Keep the
            // caller's dirty state and undo history until its selector commits.
            var savedDraft = draft.Clone();
            PivotSettingsStore.SaveValid(bundle, savedDraft, options.Clone(), new PreviewSession(preview.Bounds, preview.Snapshot().Pieces), view, bundleFault);
            Select(root, bundle, selectorFault);
            if (PivotSettingsStore.LogicalContentHash(draft, options) == logicalHash && preview.Hash == previewHash) draft.MarkSaved();
        }

        public static void Select(string runtimeRoot, string bundlePath, Action<SaveStage>? fault = null)
        {
            string root = Root(runtimeRoot), bundle = Sibling(root, bundlePath);
            RequireValid(bundle);
            string selector = Path.Combine(root, SelectorFileName);
            // Do not silently repair or replace unknown selector data.
            if (File.Exists(selector)) ReadName(selector);
            string name = Path.GetFileName(bundle);
            var document = new XDocument(new XElement(N + "selection", new XAttribute("schema", "1"), new XAttribute("file", name)));
            byte[] bytes = new UTF8Encoding(false).GetBytes(document.ToString(SaveOptions.DisableFormatting));
            Directory.CreateDirectory(root);
            string temporary = Path.Combine(root, ".pivot-location-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                fault?.Invoke(SaveStage.BeforeWrite);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                fault?.Invoke(SaveStage.AfterFlush);
                if (ReadName(temporary) != name) throw new InvalidDataException("保存先selectorの書込再検証に失敗しました。");
                RequireValid(bundle);
                fault?.Invoke(SaveStage.BeforeReplace);
                if (File.Exists(selector)) File.Replace(temporary, selector, null); else File.Move(temporary, selector);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Root(string runtimeRoot)
        {
            if (string.IsNullOrWhiteSpace(runtimeRoot)) throw new ArgumentException("runtime保存先が必要です。");
            return Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        private static string Sibling(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("bundleの保存先がありません。");
            string full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("bundleはruntime保存先の直下だけを選択できます。");
            CheckName(Path.GetFileName(full));
            return full;
        }
        private static void CheckName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains("..") || name.Contains("/") || name.Contains("\\") || name != name.Trim() ||
                !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(name) ||
                string.Equals(name, SelectorFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("保存先selectorには安全なXMLファイル名だけを指定できます。");
        }
        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("保存先selectorとbundleのリンクは読み込みません。");
        }
        private static void RequireValid(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("選択された設定bundleがありません。別の保存へ推測復旧していません。", path);
            RejectLink(path);
            if (!PivotSettingsStore.Load(path).IsValidSettings) throw new InvalidDataException("正常設定以外を起動時のbundleに選択できません。");
        }
        private static string ReadName(string path)
        {
            RejectLink(path);
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("保存先selectorがサイズ上限を超えています。");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes, IgnoreComments = false };
            XDocument document;
            using (var reader = XmlReader.Create(path, settings)) document = XDocument.Load(reader, LoadOptions.None);
            if (document.DocumentType != null || document.Nodes().Any(n => !(n is XElement) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value)))) throw new InvalidDataException("保存先selectorのXML内容が不正です。");
            var root = document.Root ?? throw new InvalidDataException("保存先selectorがありません。");
            if (root.Name != N + "selection" || root.HasElements || root.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || a.Name.LocalName != "schema" && a.Name.LocalName != "file")) ||
                (string?)root.Attribute("schema") != "1" || root.Attribute("file") == null || root.Nodes().Any(n => !(n is XText t && string.IsNullOrWhiteSpace(t.Value))))
                throw new InvalidDataException("未知・欠損した保存先selector schemaです。元ファイルを保持しています。");
            string name = (string)root.Attribute("file")!; CheckName(name); return name;
        }
    }
}
