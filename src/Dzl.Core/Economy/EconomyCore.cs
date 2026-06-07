using System.Xml.Linq;

namespace Dzl.Core.Economy;

public enum CeKind { Types, SpawnableTypes, Events, RandomPresets, Globals, Other }
public enum CeOrigin { Vanilla, Mod, Custom }

public sealed record CeFileRef(string Path, CeKind Kind, CeOrigin Origin, string ModSource);

/// <summary>Parse a mission's <c>cfgeconomycore.xml</c> into the CE files it references. Pure;
/// throws on malformed XML (callers catch). Vanilla <c>db/types.xml</c> is implicit — add it separately.</summary>
public static class EconomyCore
{
    public static List<CeFileRef> Parse(string economyCoreXml, string missionDir)
    {
        var doc = XDocument.Parse(economyCoreXml);
        var list = new List<CeFileRef>();
        foreach (var ce in doc.Descendants("ce"))
        {
            var folder = ce.Attribute("folder")?.Value?.Trim() ?? "";
            var (origin, modSource) = Classify(folder);
            foreach (var file in ce.Elements("file"))
            {
                var name = file.Attribute("name")?.Value?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var kind = MapKind(file.Attribute("type")?.Value?.Trim());
                var path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(missionDir, folder.Replace('/', System.IO.Path.DirectorySeparatorChar), name));
                list.Add(new CeFileRef(path, kind, origin, modSource));
            }
        }
        return list;
    }

    private static (CeOrigin, string) Classify(string folder)
    {
        var parts = folder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals("ce", StringComparison.OrdinalIgnoreCase))
            return (CeOrigin.Mod, parts[1]);
        return (CeOrigin.Custom, parts.Length > 0 ? parts[^1] : folder);
    }

    private static CeKind MapKind(string? type) => type?.ToLowerInvariant() switch
    {
        "types" => CeKind.Types,
        "spawnabletypes" => CeKind.SpawnableTypes,
        "events" => CeKind.Events,
        "randompresets" => CeKind.RandomPresets,
        "globals" => CeKind.Globals,
        _ => CeKind.Other,
    };
}
