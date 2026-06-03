using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class ToolCatalogTests
{
    private static string FakeInstall()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        void Exe(string rel)
        {
            var p = Path.Combine(root, "Bin", rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "");
        }
        Exe("Workbench/workbenchApp.exe");
        Exe("ImageToPAA/ImageToPAA.exe");
        Exe("AddonBuilder/AddonBuilder.exe");
        // a tool NOT in the known map, to exercise the glob fallback:
        Exe("SomeFutureTool/futuretool.exe");
        return root;
    }

    [Fact]
    public void Discover_finds_known_tools_with_correct_kind()
    {
        var cat = ToolCatalog.Discover(FakeInstall());
        cat.Should().Contain(t => t.Key == "workbench" && t.Exists && t.Kind == ToolKind.LaunchOnly);
        cat.Should().Contain(t => t.Key == "imagetopaa" && t.Exists && t.Kind == ToolKind.CliWrappable);
        cat.Should().Contain(t => t.Key == "addonbuilder" && t.Exists && t.Kind == ToolKind.CliWrappable);
    }

    [Fact]
    public void Known_tool_missing_on_disk_is_reported_not_exists()
    {
        // empty install: known tools present in the map but Exists=false
        var empty = Directory.CreateTempSubdirectory().FullName;
        var cat = ToolCatalog.Discover(empty);
        cat.Should().Contain(t => t.Key == "workbench" && !t.Exists);
    }

    [Fact]
    public void Glob_fallback_surfaces_unmapped_exes()
    {
        var cat = ToolCatalog.Discover(FakeInstall());
        cat.Should().Contain(t => t.ExePath.EndsWith("futuretool.exe") && t.Kind == ToolKind.LaunchOnly);
    }

    [Fact]
    public void Missing_tools_path_returns_known_map_all_absent()
    {
        var cat = ToolCatalog.Discover(@"X:\does\not\exist");
        cat.Should().NotBeEmpty();
        cat.Should().OnlyContain(t => !t.Exists);
    }
}
