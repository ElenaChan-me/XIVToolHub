using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Act;

/// <summary>
/// XIVToolHub が管理するポータブル版 ACT の設定ファイルに、プラグインを登録する。
/// ポータブル版の設定は ACT のフォルダ内の Config\Advanced Combat Tracker.config.xml にある。
/// </summary>
public sealed class ActConfigEditor
{
    public const string ConfigFolderName = "Config";
    public const string ConfigFileName = "Advanced Combat Tracker.config.xml";

    private const string RootName = "Config";
    private const string PluginsName = "ActPlugins";
    private const string PluginName = "Plugin";
    private const long MaxConfigBytes = 32 * 1024 * 1024;

    private readonly string _actDirectory;
    private readonly IProcessDetector _actDetector;
    private readonly TimeProvider _clock;

    public ActConfigEditor(string actDirectory, IProcessDetector actDetector, TimeProvider clock)
    {
        _actDirectory = Path.GetFullPath(actDirectory);
        _actDetector = actDetector;
        _clock = clock;
    }

    public string ConfigPath => Path.Combine(_actDirectory, ConfigFolderName, ConfigFileName);

    public string ActDirectory => _actDirectory;

    public bool ActInstalled => Directory.Exists(_actDirectory);

    public IReadOnlyList<string> GetRegisteredPaths()
    {
        if (!File.Exists(ConfigPath))
        {
            return [];
        }
        return [.. GetPluginElements(Load()).Select(e => ResolvePath((string?)e.Attribute("Path"))).OfType<string>()];
    }

    /// <summary>
    /// プラグインの DLL を登録して有効にする。設定ファイルがまだ無い場合（ACT を一度も起動していない場合）は作成する。
    /// </summary>
    public bool Register(string dllPath)
    {
        var fullPath = Path.GetFullPath(dllPath);
        EnsureEditable();

        var exists = File.Exists(ConfigPath);
        // ACT は最小限の設定ファイルでも起動し、足りない項目は既定値で補うことを確認している
        var document = exists ? Load() : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(RootName));
        var root = document.Root!;
        var plugins = root.Element(PluginsName);
        if (plugins is null)
        {
            plugins = new XElement(PluginsName);
            root.Add(plugins);
        }

        var existing = plugins.Elements(PluginName).FirstOrDefault(e => SamePath(e, fullPath));
        if (existing is not null)
        {
            if (string.Equals((string?)existing.Attribute("Enabled"), "True", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            existing.SetAttributeValue("Enabled", "True");
        }
        else
        {
            plugins.Add(new XElement(PluginName, new XAttribute("Enabled", "True"), new XAttribute("Path", fullPath)));
        }

        Save(document, backup: exists);
        return true;
    }

    public bool Unregister(string dllPath)
    {
        if (!File.Exists(ConfigPath))
        {
            return false;
        }
        var fullPath = Path.GetFullPath(dllPath);
        EnsureEditable();
        var document = Load();
        var targets = GetPluginElements(document).Where(e => SamePath(e, fullPath)).ToList();
        if (targets.Count == 0)
        {
            return false;
        }
        foreach (var target in targets)
        {
            target.Remove();
        }
        Save(document, backup: true);
        return true;
    }

    public void EnsureEditable()
    {
        if (!ActInstalled)
        {
            throw new HubException("ACT がインストールされていません。先に ACT をインストールしてください。");
        }
        // ACT は終了時に設定を書き戻すため、起動中に変更しても上書きされてしまう
        if (_actDetector.IsRunning())
        {
            throw new HubException("ACT の起動中は ACT プラグインを変更できません。ACT を終了してからお試しください。");
        }
    }

    private XDocument Load()
    {
        var info = new FileInfo(ConfigPath);
        if (info.Length > MaxConfigBytes)
        {
            throw new HubException("ACT の設定ファイルが大きすぎます。");
        }
        try
        {
            // 外部エンティティは読み込まない
            using var reader = XmlReader.Create(ConfigPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name.LocalName != RootName)
            {
                throw new HubException("ACT の設定ファイルの形式が想定と異なります。");
            }
            return document;
        }
        catch (XmlException ex)
        {
            throw new HubException("ACT の設定ファイルを読み込めません。ファイルが壊れている可能性があります。", ex);
        }
    }

    private void Save(XDocument document, bool backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        if (backup)
        {
            var stamp = _clock.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var backupPath = $"{ConfigPath}.xivtoolhub-{stamp}.bak";
            for (var i = 1; File.Exists(backupPath); i++)
            {
                backupPath = $"{ConfigPath}.xivtoolhub-{stamp}-{i}.bak";
            }
            File.Copy(ConfigPath, backupPath);
        }

        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
        {
            document.Root!.WriteTo(writer);
        }
        // ACT が保存するファイルと同じく、XML 宣言付き・BOM 付きの UTF-8 で書く
        AtomicFile.WriteAllText(ConfigPath, "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + builder);
    }

    private static IEnumerable<XElement> GetPluginElements(XDocument document) =>
        document.Root?.Element(PluginsName)?.Elements(PluginName) ?? [];

    private bool SamePath(XElement element, string fullPath) =>
        string.Equals(ResolvePath((string?)element.Attribute("Path")), fullPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ポータブル版の ACT はプラグインのパスを ACT のフォルダからの相対パスで保存し直すため、絶対パスに戻して比較する。
    /// </summary>
    private string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_actDirectory, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
