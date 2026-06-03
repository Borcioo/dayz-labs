namespace Dzl.Core.Tools;

public enum ToolKind { LaunchOnly, CliWrappable }

public sealed record ToolEntry(string Key, string DisplayName, string ExePath, bool Exists, ToolKind Kind);

public static class ToolCatalog
{
    // key -> (display, relative path under Bin\, kind). Verified against a real install.
    private static readonly (string Key, string Name, string Rel, ToolKind Kind)[] Known =
    {
        ("workbench",       "Workbench",         @"Workbench\workbenchApp.exe",            ToolKind.LaunchOnly),
        ("objectbuilder",   "Object Builder",    @"ObjectBuilder\ObjectBuilder.exe",       ToolKind.LaunchOnly),
        ("terrainbuilder",  "Terrain Builder",   @"TerrainBuilder\terrainBuilder.exe",     ToolKind.LaunchOnly),
        ("publisher",       "Publisher",         @"Publisher\Publisher.exe",               ToolKind.LaunchOnly),
        ("launcher",        "DayZ Tools Launcher",@"Launcher\DayZToolsLauncher.exe",       ToolKind.LaunchOnly),
        ("ceeditor",        "CE Editor",         @"CeEditor\CeEditor.exe",                 ToolKind.LaunchOnly),
        ("terrainprocessor","Terrain Processor", @"TerrainProcessor\TerrainProcessor.exe", ToolKind.LaunchOnly),
        ("navmesh",         "NavMesh Generator", @"NavMeshGenerator\NavMeshGenerator_x64.exe", ToolKind.LaunchOnly),
        ("imagetopaa",      "ImageToPAA",        @"ImageToPAA\ImageToPAA.exe",             ToolKind.CliWrappable),
        ("addonbuilder",    "Addon Builder",     @"AddonBuilder\AddonBuilder.exe",         ToolKind.CliWrappable),
        ("cfgconvert",      "CfgConvert (DeRap)",@"CfgConvert\CfgConvert.exe",             ToolKind.CliWrappable),
        ("binarize",        "Binarize",          @"Binarize\binarize.exe",                 ToolKind.CliWrappable),
        ("bankrev",         "BankRev (extract PBO)",@"PboUtils\BankRev.exe",               ToolKind.CliWrappable),
        ("filebank",        "FileBank (pack PBO)",  @"PboUtils\FileBank.exe",              ToolKind.CliWrappable),
        ("dssignfile",      "DSSignFile (sign)", @"DsUtils\DSSignFile.exe",                ToolKind.CliWrappable),
    };

    public static List<ToolEntry> Discover(string toolsPath)
    {
        var bin = Path.Combine(toolsPath, "Bin");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ToolEntry>();
        foreach (var (key, name, rel, kind) in Known)
        {
            var full = Path.Combine(bin, rel);
            known.Add(Path.GetFullPath(full));
            list.Add(new ToolEntry(key, name, full, File.Exists(full), kind));
        }
        // glob fallback: any other *.exe directly under Bin\<dir>\ not already mapped.
        if (Directory.Exists(bin))
        {
            foreach (var exe in Directory.EnumerateFiles(bin, "*.exe", SearchOption.AllDirectories))
            {
                if (known.Contains(Path.GetFullPath(exe))) continue;
                var key = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                list.Add(new ToolEntry(key, Path.GetFileNameWithoutExtension(exe), exe, true, ToolKind.LaunchOnly));
            }
        }
        return list;
    }

    public static ToolEntry? Find(string toolsPath, string key) =>
        Discover(toolsPath).FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
