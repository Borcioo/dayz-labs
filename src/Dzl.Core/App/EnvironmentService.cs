using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>cfgenvironment.xml</c> — animal/infected territories
/// (the per-territory count/radius knobs; the referenced env/*_territories.xml geometry is opened externally).</summary>
public sealed class EnvironmentService : CeFileService
{
    public EnvironmentService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgenvironment.xml";
    protected override string? SeedRootName => null;   // edits require an existing file

    public string? EnvironmentPath() => FilePath();

    /// <summary>The mission directory (to resolve the relative env/*_territories.xml paths), or null.</summary>
    public string? MissionDir()
    {
        var p = FilePath();
        return p is null ? null : System.IO.Path.GetDirectoryName(p);
    }

    public EnvConfig Load()
    {
        var raw = ReadRaw();
        return raw is null ? new EnvConfig(System.Array.Empty<string>(), System.Array.Empty<EnvTerritory>()) : EnvironmentXml.Parse(raw);
    }

    /// <summary>Upsert a territory-level item knob.</summary>
    public (bool ok, string msg) SetItem(string territory, string itemName, string val)
    {
        if (string.IsNullOrWhiteSpace(territory)) return (false, "territory must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(doc => EnvironmentXml.SetItem(doc, territory, itemName, val),
            $"set {territory}/{itemName}", $"territory '{territory}' not found");
    }
}
