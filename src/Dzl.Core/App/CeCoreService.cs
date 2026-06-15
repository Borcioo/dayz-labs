using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active mission's <c>cfgeconomycore.xml</c> — the CE master config (root classes,
/// default knobs, and the custom-file routing manifest).
/// </summary>
public sealed class CeCoreService : CeFileService
{
    public CeCoreService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgeconomycore.xml";
    protected override string? SeedRootName => "economycore";

    public string? CeCorePath() => FilePath();

    /// <summary>Parse the config (empty when the file is absent/unresolvable).</summary>
    public CeCoreConfig Load()
    {
        var raw = ReadRaw();
        return raw is null ? new CeCoreConfig(System.Array.Empty<CeRootClass>(), System.Array.Empty<CeDefault>(), System.Array.Empty<CeRoutedFile>())
                           : CeCoreXml.Parse(raw);
    }

    /// <summary>Upsert a default knob.</summary>
    public (bool ok, string msg) SetDefault(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "default name must not be empty");
        return Edit(doc => CeCoreXml.SetDefault(doc, name, value),
            $"set default '{name}'", $"failed to set default '{name}'");
    }

    /// <summary>Register a custom CE file (folder + name + type).</summary>
    public (bool ok, string msg) AddFile(string folder, string name, string type)
    {
        if (string.IsNullOrWhiteSpace(folder)) return (false, "folder must not be empty");
        if (string.IsNullOrWhiteSpace(name)) return (false, "file name must not be empty");
        return Edit(doc => CeCoreXml.AddFile(doc, folder, name, type),
            $"registered {folder}/{name} ({type})",
            $"not registered: invalid type '{type}' or {folder}/{name} already present");
    }

    /// <summary>Unregister a custom CE file.</summary>
    public (bool ok, string msg) RemoveFile(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "file name must not be empty");
        return Edit(doc => CeCoreXml.RemoveFile(doc, folder, name),
            $"unregistered {folder}/{name}", $"{folder}/{name} not found");
    }
}
