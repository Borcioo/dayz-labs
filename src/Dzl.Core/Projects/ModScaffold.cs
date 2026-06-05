using System.Text.Json;
using Dzl.Core.Config;

namespace Dzl.Core.Projects;

public sealed record ScaffoldResult(bool Ok, string ModDir, string Message);

/// <summary>Per-mod dzl metadata persisted at <c>&lt;Mod&gt;\.dzl\mod.json</c> (snake_case).</summary>
public sealed record ModMeta
{
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Created { get; init; } = "";   // ISO-8601 UTC
}

/// <summary>Writes the standard DayZ mod source skeleton into &lt;root&gt;/&lt;Mod&gt;/. Idempotent:
/// existing files are never overwritten. Pure template builders are unit-tested; the writer is thin.</summary>
public static class ModScaffold
{
    public static string ConfigCpp(string mod, string author) =>
$$"""
class CfgPatches
{
    class {{mod}}
    {
        units[] = {};
        weapons[] = {};
        requiredVersion = 0.1;
        requiredAddons[] = { "DZ_Data" };
        author = "{{author}}";
    };
};

class CfgMods
{
    class {{mod}}
    {
        dir = "{{mod}}";
        name = "{{mod}}";
        author = "{{author}}";
        type = "mod";
        dependencies[] = { "Game", "World", "Mission" };
        class defs
        {
            class gameScriptModule { value = ""; files[] = { "{{mod}}/scripts/3_Game" }; };
            class worldScriptModule { value = ""; files[] = { "{{mod}}/scripts/4_World" }; };
            class missionScriptModule { value = ""; files[] = { "{{mod}}/scripts/5_Mission" }; };
        };
    };
};
""";

    public static string Readme(string mod) =>
$"# {mod}\n\nDayZ mod scaffolded by dzl.\n\n- Source: this folder (under your ProjectsRoot)\n- Built PBO target: `P:\\Mods\\@{mod}\\Addons\\`\n- Scripts: `scripts/3_Game`, `4_World`, `5_Mission`\n";

    private const string AuthorFile = "dayz-author.txt";
    public static void SaveAuthor(string configDir, string author)
    {
        try { Directory.CreateDirectory(configDir); File.WriteAllText(Path.Combine(configDir, AuthorFile), author.Trim()); }
        catch { /* best-effort */ }
    }
    public static string? CachedAuthor(string configDir)
    {
        try { var p = Path.Combine(configDir, AuthorFile); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; }
        catch { return null; }
    }

    public static ScaffoldResult Scaffold(string root, string mod, string author)
    {
        if (!ProjectPaths.IsValidName(mod))
            return new ScaffoldResult(false, "", $"invalid mod name: {mod}");
        var modDir = ProjectPaths.ModDir(root, mod);
        try
        {
            Directory.CreateDirectory(modDir);
            WriteIfAbsent(Path.Combine(modDir, "config.cpp"), ConfigCpp(mod, author));
            WriteIfAbsent(Path.Combine(modDir, "$PBOPREFIX$"), mod);
            WriteIfAbsent(Path.Combine(modDir, "README.md"), Readme(mod));
            foreach (var sub in new[] { @"scripts\3_Game", @"scripts\4_World", @"scripts\5_Mission", "data", "gui" })
            {
                var d = Path.Combine(modDir, sub);
                Directory.CreateDirectory(d);
                WriteIfAbsent(Path.Combine(d, ".gitkeep"), "");
            }

            // dzl metadata folder (.dzl\mod.json) — our per-mod configs/metadata live here.
            var metaDir = ProjectPaths.ModMetaDir(root, mod);
            Directory.CreateDirectory(metaDir);
            WriteIfAbsent(Path.Combine(metaDir, "mod.json"),
                JsonSerializer.Serialize(
                    new ModMeta { Name = mod, Author = author, Created = DateTime.UtcNow.ToString("O") },
                    ConfigStore.Json));

            return new ScaffoldResult(true, modDir, "scaffolded");
        }
        catch (Exception ex) { return new ScaffoldResult(false, modDir, ex.Message); }
    }

    private static void WriteIfAbsent(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
