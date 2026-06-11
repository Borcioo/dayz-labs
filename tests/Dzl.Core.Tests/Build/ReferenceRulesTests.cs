using Dzl.Core.Build.Preflight;
using FluentAssertions;
using Xunit;

public class ReferenceRulesTests
{
    private static (string dir, PreflightOptions opts) TmpMod()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return (dir, new PreflightOptions { WorkDriveRoot = null, CheckConfig = false,
            CheckFileSystem = false, CheckScripts = false, CheckModCpp = false });
    }

    private static void Write(string dir, string rel, string content)
    {
        var path = Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static List<Finding> Of(PreflightReport r, string rule) =>
        r.Findings.Where(f => f.Rule == rule).ToList();

    // --- missing / resolving references ---

    [Fact]
    public void Missing_texture_reference_is_an_error_with_file_and_line()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "class X {\n tex = \"Foo\\data\\missing_co.paa\";\n};");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        var f = Of(r, "ref-missing").Single();
        f.Severity.Should().Be(FindingSeverity.Error);
        f.File.Should().Be("config.cpp");
        f.Line.Should().Be(2);
    }

    [Fact]
    public void Reference_resolving_inside_the_mod_passes()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"data\vest_co.paa", "fake");
        Write(dir, "config.cpp", "class X { tex = \"Foo\\data\\vest_co.paa\"; };");   // prefix-relative
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().BeEmpty();
    }

    [Fact]
    public void Reference_resolving_via_explicit_prefix_passes()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "$PBOPREFIX$", @"MyPrefix\Stuff");
        Write(dir, @"data\vest_co.paa", "fake");
        Write(dir, "config.cpp", "class X { tex = \"MyPrefix\\data\\vest_co.paa\"; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().BeEmpty();
    }

    [Fact]
    public void Absolute_drive_path_is_an_error_even_when_the_file_exists()
    {
        var (dir, opts) = TmpMod();
        var abs = Path.Combine(dir, "data", "x_co.paa");
        Write(dir, @"data\x_co.paa", "fake");
        Write(dir, "config.cpp", $"class X {{ tex = \"{abs}\"; }};");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-absolute-path").Should().ContainSingle();
    }

    [Fact]
    public void GUID_resource_prefix_is_stripped_before_resolution()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"gui\loading.edds", "fake");
        Write(dir, @"gui\screen.layout", "image = \"{03C79F5D93FF384F}Foo/gui/loading.edds\";");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().BeEmpty();
    }

    [Fact]
    public void Include_lines_are_not_treated_as_runtime_references()
    {
        var (dir, opts) = TmpMod();
        Write(dir, "config.cpp", "#include \"missing\\file.hpp\"\nclass X { };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().BeEmpty();
    }

    [Fact]
    public void Concatenated_script_paths_are_skipped()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\a.c", "string p = base + \"icons\\thing_co.paa\";\nstring q = \"sounds\\full.ogg\" + suffix;");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().BeEmpty();
    }

    [Fact]
    public void Static_script_reference_is_still_checked()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"scripts\3_Game\a.c", "string p = \"Foo\\gui\\icon_co.paa\";");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-missing").Should().ContainSingle();
    }

    // --- excluded-but-referenced ---

    [Fact]
    public void Reference_to_an_excluded_file_is_an_error()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @".gui-sources\icons\big.paa", "fake");   // .gui-sources is dev-only by default
        Write(dir, "config.cpp", "class X { tex = \"Foo\\.gui-sources\\icons\\big.paa\"; };");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "ref-excluded").Should().ContainSingle();
    }

    // --- rvmat ---

    [Fact]
    public void Rvmat_with_png_texture_warns_and_missing_paa_errors()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"data\mat.rvmat", "class Stage1 { texture = \"Foo\\data\\cloth_co.png\"; };\ntexture = \"Foo\\data\\gone_nohq.paa\";");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "rvmat-source-texture").Should().ContainSingle();
        Of(r, "ref-missing").Should().NotBeEmpty();
    }

    // --- hiddenSelections arity ---

    [Fact]
    public void More_textures_than_selections_warns()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"data\a_co.paa", "x");
        Write(dir, @"data\b_co.paa", "x");
        Write(dir, "config.cpp", """
            class MyVest {
                hiddenSelections[] = {"camo"};
                hiddenSelectionsTextures[] = {"Foo\data\a_co.paa", "Foo\data\b_co.paa"};
            };
            """);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        Of(r, "hiddensel-arity").Should().ContainSingle();
    }

    // --- texture suffixes ---

    [Fact]
    public void Paa_without_role_suffix_is_info()
    {
        var (dir, opts) = TmpMod();
        Write(dir, @"data\randomtexture.paa", "x");
        Write(dir, @"data\proper_co.paa", "x");
        var r = PreflightEngine.Run(dir, "Foo", opts);
        var f = Of(r, "texture-suffix").Single();
        f.Severity.Should().Be(FindingSeverity.Info);
        f.File.Should().Contain("randomtexture");
    }

    // --- p3d binary scan ---

    [Fact]
    public void P3d_internal_missing_reference_is_a_warning()
    {
        var (dir, opts) = TmpMod();
        var bytes = System.Text.Encoding.ASCII.GetBytes("MLOD....foo\\data\\gone_co.paa....");
        File.WriteAllBytes(Path.Combine(dir, "model.p3d"), bytes);
        var r = PreflightEngine.Run(dir, "Foo", opts);
        var f = Of(r, "ref-missing").Single();
        f.Severity.Should().Be(FindingSeverity.Warning);
    }
}
