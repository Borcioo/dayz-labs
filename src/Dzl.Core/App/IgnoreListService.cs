using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>cfgignorelist.xml</c> — the flat list of item classnames
/// the Central Economy ignores.</summary>
public sealed class IgnoreListService : CeFileService
{
    public IgnoreListService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgignorelist.xml";
    protected override string? SeedRootName => "ignore";

    public string? IgnoreListPath() => FilePath();

    /// <summary>Read all ignored classnames (empty when the file is absent/unresolvable).</summary>
    public List<string> Load() => LoadList(IgnoreListXml.Parse);

    public (bool ok, string msg) Add(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "classname must not be empty");
        return Edit(doc => IgnoreListXml.Add(doc, name), $"ignored '{name}'", $"'{name}' is already in the list");
    }

    public (bool ok, string msg) Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "classname must not be empty");
        return Edit(doc => IgnoreListXml.Remove(doc, name), $"removed '{name}'", $"'{name}' not found");
    }
}
