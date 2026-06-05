using Dzl.Core.Config;
using Dzl.Core.Mods;
using FluentAssertions;
using Xunit;

public class ModDiscoveryTests
{
    private static string MakeTree()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "@CF", "addons"));                    // mod via addons/
        Directory.CreateDirectory(Path.Combine(root, "DevMod", "scripts"));                // mod via config.cpp + scripts/
        File.WriteAllText(Path.Combine(root, "DevMod", "config.cpp"), "class CfgPatches {};");
        Directory.CreateDirectory(Path.Combine(root, "junk"));                             // not a mod
        Directory.CreateDirectory(Path.Combine(root, "halfmod", "scripts"));               // scripts but no config.cpp -> not a mod
        return root;
    }

    [Fact]
    public void Discover_finds_addons_and_configcpp_mods_only()
    {
        var root = MakeTree();
        var found = ModDiscovery.Discover(new[] { root }).Select(Path.GetFileName).ToList();
        found.Should().Contain("@CF").And.Contain("DevMod");
        found.Should().NotContain("junk").And.NotContain("halfmod");
    }

    [Fact]
    public void Discover_skips_missing_or_inaccessible_roots_without_throwing()
    {
        var good = MakeTree();
        var bad = Path.Combine(good, "does", "not", "exist");   // missing path (stands in for a dangling junction)
        List<string> found = null!;
        var act = () => found = ModDiscovery.Discover(new[] { bad, good });
        act.Should().NotThrow();
        found.Select(Path.GetFileName).Should().Contain("@CF").And.Contain("DevMod");   // good root still scanned
    }

    [Fact]
    public void Merge_keeps_saved_order_enabled_side_and_appends_new_disabled()
    {
        var root = MakeTree();
        var discovered = ModDiscovery.Discover(new[] { root }).ToList(); // @CF, DevMod (some order)
        var cfPath = discovered.Single(p => Path.GetFileName(p) == "@CF");
        var saved = new List<ModEntry> { new() { Path = cfPath, Enabled = true, Side = "server" } };
        var merged = ModDiscovery.Merge(saved, discovered);

        merged[0].Path.Should().Be(cfPath);          // saved stays first
        merged[0].Enabled.Should().BeTrue();
        merged[0].Side.Should().Be("server");
        merged[0].Missing.Should().BeFalse();
        merged.Should().Contain(m => Path.GetFileName(m.Path) == "DevMod" && !m.Enabled && !m.Missing);
    }

    [Fact]
    public void Merge_marks_saved_mod_missing_when_not_on_disk()
    {
        var merged = ModDiscovery.Merge(
            new List<ModEntry> { new() { Path = @"P:\@Gone", Enabled = true, Side = "both" } },
            new List<string>());
        merged.Should().ContainSingle();
        merged[0].Missing.Should().BeTrue();
        merged[0].Enabled.Should().BeTrue();  // selection preserved even if missing
        merged[0].Name.Should().Be("@Gone");
    }

    [Fact]
    public void ResolveName_prefers_meta_then_mod_then_folder()
    {
        var root = Directory.CreateTempSubdirectory().FullName;

        // 1) meta.cpp wins (Workshop name) even when mod.cpp is also present.
        var withMeta = Path.Combine(root, "1559212036");
        Directory.CreateDirectory(withMeta);
        File.WriteAllText(Path.Combine(withMeta, "meta.cpp"),
            "protocol = 1;\npublishedid = 1559212036;\nname = \"Community Framework\";\ntimestamp = 0;\n");
        File.WriteAllText(Path.Combine(withMeta, "mod.cpp"), "name = \"CF presentation\";\n");
        ModDiscovery.ResolveName(withMeta).Should().Be("Community Framework");

        // 2) mod.cpp used when there's no meta.cpp.
        var withMod = Path.Combine(root, "2545327648");
        Directory.CreateDirectory(withMod);
        File.WriteAllText(Path.Combine(withMod, "mod.cpp"),
            "name = \"GameLabs\";\npicture = \"x.edds\";\nauthor = \"CFTools\";\n");
        ModDiscovery.ResolveName(withMod).Should().Be("GameLabs");

        // 3) folder name when neither file exists.
        var bare = Path.Combine(root, "@LocalDev");
        Directory.CreateDirectory(bare);
        ModDiscovery.ResolveName(bare).Should().Be("@LocalDev");

        // 4) blank/garbage name falls through to folder.
        var blank = Path.Combine(root, "3171576913");
        Directory.CreateDirectory(blank);
        File.WriteAllText(Path.Combine(blank, "meta.cpp"), "publishedid = 3171576913;\nname = \"\";\n");
        ModDiscovery.ResolveName(blank).Should().Be("3171576913");
    }
}
