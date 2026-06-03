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
}
