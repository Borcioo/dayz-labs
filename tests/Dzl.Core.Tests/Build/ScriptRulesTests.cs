using Dzl.Core.Build.Preflight;
using FluentAssertions;

public class ScriptRulesTests
{
    private static (string dir, PreflightOptions opts) TmpMod()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return (dir, new PreflightOptions { WorkDriveRoot = null, CheckConfig = false,
            CheckReferences = false, CheckFileSystem = false, CheckP3dStrings = false, CheckModCpp = false });
    }

    private static void Write(string dir, string rel, string content)
    {
        var path = Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static List<Finding> Of(PreflightReport r, string rule) =>
        r.Findings.Where(f => f.Rule == rule).ToList();

    // --- modded class base clause ---

    [Fact]
    public void Modded_class_with_extends_warns_with_line()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\4_World\player.c", "// patch\nmodded class PlayerBase extends ManBase\n{\n}");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        var f = Of(r, "modded-base-clause").Single();
        f.Line.Should().Be(2);
        f.Message.Should().Contain("PlayerBase");
    }

    [Fact]
    public void Modded_class_with_colon_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\4_World\a.c", "modded class ItemBase : InventoryItem {}");
        PreflightEngine.Run(dir, "Foo", opts)
            .Findings.Should().Contain(f => f.Rule == "modded-base-clause");
    }

    [Fact]
    public void Plain_modded_class_is_clean()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\4_World\a.c", "modded class PlayerBase\n{\n\tvoid Foo() {}\n}");
        Of(PreflightEngine.Run(dir, "Foo", opts), "modded-base-clause").Should().BeEmpty();
    }

    // --- SetActions super ---

    [Fact]
    public void SetActions_without_super_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\4_World\item.c", """
            modded class MyItem
            {
                override void SetActions()
                {
                    AddAction(ActionOpen);
                }
            }
            """);
        Of(PreflightEngine.Run(dir, "Foo", opts), "setactions-no-super").Should().ContainSingle();
    }

    [Fact]
    public void SetActions_with_super_is_clean()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\4_World\item.c", """
            modded class MyItem
            {
                override void SetActions()
                {
                    super.SetActions();
                    AddAction(ActionOpen);
                }
            }
            """);
        Of(PreflightEngine.Run(dir, "Foo", opts), "setactions-no-super").Should().BeEmpty();
    }

    // --- duplicate classes ---

    [Fact]
    public void Same_class_in_two_files_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\a.c", "class Helper { }");
        Write(dir, @"scripts\4_World\b.c", "class Helper { }");
        var f = Of(PreflightEngine.Run(dir, "Foo", opts), "script-duplicate-class").Single();
        f.Message.Should().Contain("modded class");
    }

    [Fact]
    public void Modded_class_does_not_count_as_duplicate()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\a.c", "class Helper { }");
        Write(dir, @"scripts\4_World\b.c", "modded class Helper { }");
        Of(PreflightEngine.Run(dir, "Foo", opts), "script-duplicate-class").Should().BeEmpty();
    }

    // --- balance ---

    [Fact]
    public void Unclosed_brace_warns_with_open_line()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\broken.c", "class A\n{\n\tvoid F()\n\t{\n}");   // one close short
        var fs = Of(PreflightEngine.Run(dir, "Foo", opts), "script-balance");
        fs.Should().NotBeEmpty();
    }

    [Fact]
    public void Braces_in_strings_do_not_confuse_balance()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\ok.c", "class A\n{\n\tstring s = \"{[(\";\n}");
        Of(PreflightEngine.Run(dir, "Foo", opts), "script-balance").Should().BeEmpty();
    }

    // --- config traps ---

    [Fact]
    public void InventorySlot_append_trap_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgVehicles {
                class MyCrate : WoodenCrate { inventorySlot[] += {"MySlot"}; };
            };
            """);
        var f = Of(PreflightEngine.Run(dir, "Foo", opts), "trap-inventoryslot-append").Single();
        f.Message.Should().Contain("T148506");
    }

    [Fact]
    public void HealthLevelValues_legacy_trap_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class X { healthLevelValues[] = {1.0, 0.7}; };");
        Of(PreflightEngine.Run(dir, "Foo", opts), "trap-healthlevelvalues").Should().ContainSingle();
    }

    [Fact]
    public void Proper_healthLevels_is_clean()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class X {
                class DamageSystem { class GlobalHealth { class Health {
                    healthLevels[] = { {1.0, {"a.rvmat"}} };
                }; }; };
            };
            """);
        Of(PreflightEngine.Run(dir, "Foo", opts), "trap-healthlevelvalues").Should().BeEmpty();
    }
}

public class ReportExportTests
{
    [Fact]
    public void Text_and_json_roundtrip_contain_findings()
    {
        var report = new PreflightReport();
        report.Error("ref-missing", "Missing referenced file: x.paa", "config.cpp", 12);
        report.Warn("path-uppercase", "1 packed path(s) contain uppercase characters");

        var txt = ReportExport.ToText(report, "Foo");
        txt.Should().Contain("ERROR").And.Contain("config.cpp:12").And.Contain("errors: 1");

        var json = ReportExport.ToJson(report, "Foo");
        json.Should().Contain("\"ok\": false").And.Contain("ref-missing").And.Contain("\"line\": 12");
    }

    [Fact]
    public void Save_writes_both_files()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var report = new PreflightReport();
        report.Info("x", "y");
        var (txt, json) = ReportExport.Save(report, "Foo", Path.Combine(dir, "run1"));
        File.Exists(txt).Should().BeTrue();
        File.Exists(json).Should().BeTrue();
    }
}
