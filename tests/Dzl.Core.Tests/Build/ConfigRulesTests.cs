using Dzl.Core.Build.Preflight;
using FluentAssertions;

public class ConfigRulesTests
{
    // Engine run over a throwaway mod dir with no work drive and no CfgConvert (gate skipped).
    private static (string dir, PreflightOptions opts) TmpMod()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return (dir, new PreflightOptions { WorkDriveRoot = null, CheckReferences = false,
            CheckFileSystem = false, CheckScripts = false, CheckP3dStrings = false, CheckModCpp = false });
    }

    private static void Write(string dir, string rel, string content)
    {
        var path = Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static List<string> Rules(PreflightReport r) => r.Findings.Select(f => f.Rule).ToList();

    // --- mod dir / config presence ---

    [Fact]
    public void Missing_mod_dir_is_an_error_not_an_exception()
    {
        var r = PreflightEngine.Run(@"C:\definitely\missing\dir", "Ghost");
        r.Ok.Should().BeFalse();
        Rules(r).Should().Contain("mod-missing");
    }

    [Fact]
    public void Mod_without_any_config_is_an_error()
    {
        var (dir, opts) = TmpMod();
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("config-missing");
    }

    // --- CfgPatches ---

    [Fact]
    public void Missing_CfgPatches_is_an_error()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class CfgMods { class Foo { }; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgpatches-missing");
    }

    [Fact]
    public void Empty_CfgPatches_is_an_error()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class CfgPatches { };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgpatches-empty");
    }

    [Fact]
    public void Missing_requiredAddons_is_a_warning()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class CfgPatches { class Foo { units[] = {}; }; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("requiredaddons-missing");
        r.Ok.Should().BeTrue();   // warning, not error
    }

    [Fact]
    public void Empty_requiredAddons_with_external_bases_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {}; }; };
            class CfgVehicles { class MyVest : Clothing_Base { }; };
            """);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("requiredaddons-empty");
    }

    [Fact]
    public void Vanilla_base_inheritance_produces_requiredAddons_hint()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {"DZ_Data"}; }; };
            class CfgVehicles { class MyVest : Clothing_Base { }; };
            """);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        var hint = r.Findings.Single(f => f.Rule == "requiredaddons-hint");
        hint.Message.Should().Contain("DZ_Characters");
    }

    [Fact]
    public void Clean_item_mod_passes_without_errors()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "$PBOPREFIX$", "Foo");
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {"DZ_Data"}; }; };
            class CfgVehicles { class MyItem : Inventory_Base { }; };
            """);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        r.Ok.Should().BeTrue();
        // syntax gate is off (no CfgConvert) → the skip itself must be visible
        Rules(r).Should().Contain("syntax-gate-skipped");
    }

    // --- CfgMods ↔ scripts ---

    [Fact]
    public void Scripts_without_CfgMods_is_an_error()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class CfgPatches { class Foo { requiredAddons[] = {}; }; };");
        Write(dir, @"scripts\4_World\item.c", "class MyItem {}");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgmods-missing");
    }

    [Fact]
    public void Script_folder_not_referenced_by_its_module_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {}; }; };
            class CfgMods {
                class Foo {
                    type = "mod";
                    class defs {
                        class gameScriptModule { value = ""; files[] = {"Foo/scripts/3_Game"}; };
                    };
                };
            };
            """);
        Write(dir, @"scripts\3_Game\a.c", "class A {}");
        Write(dir, @"scripts\4_World\b.c", "class B {}");   // exists, unregistered
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgmods-module-unregistered");   // 4_World has no module entry
        Rules(r).Should().NotContain("cfgmods-path-missing");       // 3_Game resolves prefix-relative
    }

    [Fact]
    public void Module_files_path_that_does_not_exist_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {}; }; };
            class CfgMods {
                class Foo {
                    type = "mod";
                    class defs {
                        class worldScriptModule { value = ""; files[] = {"Foo/scripts/4_World"}; };
                    };
                };
            };
            """);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgmods-path-missing");
    }

    [Fact]
    public void Missing_type_mod_warns_when_scripts_exist()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", """
            class CfgPatches { class Foo { requiredAddons[] = {}; }; };
            class CfgMods {
                class Foo {
                    class defs {
                        class gameScriptModule { value = ""; files[] = {"Foo/scripts/3_Game"}; };
                    };
                };
            };
            """);
        Write(dir, @"scripts\3_Game\a.c", "class A {}");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("cfgmods-type");
    }

    // --- includes ---

    [Fact]
    public void CfgPatches_inside_an_include_is_found()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "#include \"patches.hpp\"\nclass CfgVehicles { };");
        Write(dir, "patches.hpp", "class CfgPatches { class Foo { requiredAddons[] = {\"DZ_Data\"}; }; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().NotContain("cfgpatches-missing");
        r.Ok.Should().BeTrue();
    }

    // --- prefix file ---

    [Fact]
    public void Prefix_with_drive_letter_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "$PBOPREFIX$", @"P:\Foo");
        Write(dir, "config.cpp", "class CfgPatches { class Foo { requiredAddons[] = {}; }; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Rules(r).Should().Contain("prefix-drive");
    }

    [Fact]
    public void Missing_prefix_file_is_info_only()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class CfgPatches { class Foo { requiredAddons[] = {}; }; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        r.Findings.Single(f => f.Rule == "prefix-missing").Severity.Should().Be(FindingSeverity.Info);
    }
}
