using System.IO;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Loads the whole Central Economy of the active mission by composing the per-file CE
/// services into a single <see cref="CeWorld"/> for the validator and the dashboard. Never throws —
/// each service already returns empty data for an absent/unparsable file, and the per-file
/// <see cref="CeFileInfo"/> records which files actually resolved.</summary>
public sealed class CeWorldLoader
{
    private readonly string _configPath;

    public CeWorldLoader(string configPath) => _configPath = configPath;

    public CeWorld Load()
    {
        var types = new TypesService(_configPath);
        var dict = new DictionaryService(_configPath);
        var events = new EventsService(_configPath);
        var globals = new GlobalsService(_configPath);
        var spawn = new SpawnableTypesService(_configPath);
        var presets = new RandomPresetsService(_configPath);
        var pspawn = new PlayerSpawnsService(_configPath);

        return new CeWorld
        {
            Types = new CeFileSet(types.List()),
            Limits = dict.Load(),
            UserGroups = dict.LoadGroups(),
            Events = events.Load(),
            Globals = globals.Load(),
            SpawnableTypes = spawn.Load(),
            RandomPresets = presets.Load(),
            PlayerSpawns = pspawn.Load(),
            Files = new[]
            {
                Info(CeKind.Events, events.EventsPath()),
                Info(CeKind.Globals, globals.GlobalsPath()),
                Info(CeKind.SpawnableTypes, spawn.SpawnableTypesPath()),
                Info(CeKind.RandomPresets, presets.PresetsPath()),
                Info(CeKind.PlayerSpawns, pspawn.SpawnsPath()),
            },
        };
    }

    private static CeFileInfo Info(CeKind kind, string? path) =>
        new(kind, path is null ? "" : Path.GetFileName(path), path ?? "",
            path is not null && File.Exists(path));
}
