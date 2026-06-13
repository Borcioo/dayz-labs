using Dzl.Core.Build.Preflight;
using FluentAssertions;

public class CppTextTests
{
    // --- StripComments ---

    [Fact]
    public void StripComments_removes_line_and_block_comments()
    {
        var s = "a = 1; // line\nb = 2; /* block */ c = 3;";
        CppText.StripComments(s).Should().Be("a = 1; \nb = 2;  c = 3;");
    }

    [Fact]
    public void StripComments_keeps_comment_markers_inside_strings()
    {
        var s = "path = \"dz//data\"; tex = \"a/*b*/c\";";
        CppText.StripComments(s).Should().Be(s);
    }

    [Fact]
    public void StripComments_preserveLines_keeps_line_numbers_stable()
    {
        var s = "x;\n/* multi\nline\ncomment */\ny;";
        var stripped = CppText.StripComments(s, preserveLines: true);
        stripped.Split('\n').Should().HaveCount(5);
        CppText.LineOf(stripped, stripped.IndexOf('y')).Should().Be(5);
    }

    [Fact]
    public void StripComments_handles_unterminated_block_comment()
    {
        CppText.StripComments("a; /* never closed").Should().Be("a; ");
    }

    [Fact]
    public void StripComments_handles_escaped_quotes_in_strings()
    {
        var s = """x = "a\"// not a comment"; y;""";
        CppText.StripComments(s).Should().Be(s);
    }

    // --- FindMatchingBrace ---

    [Fact]
    public void FindMatchingBrace_matches_nested_braces()
    {
        var s = "{ a { b } c }";
        CppText.FindMatchingBrace(s, 0).Should().Be(s.Length - 1);
        CppText.FindMatchingBrace(s, 4).Should().Be(8);
    }

    [Fact]
    public void FindMatchingBrace_ignores_braces_in_strings()
    {
        var s = "{ x = \"}\"; }";
        CppText.FindMatchingBrace(s, 0).Should().Be(s.Length - 1);
    }

    [Fact]
    public void FindMatchingBrace_returns_minus_one_when_unbalanced()
    {
        CppText.FindMatchingBrace("{ open", 0).Should().Be(-1);
    }

    // --- ClassBlocks / FindClassBody ---

    [Fact]
    public void ClassBlocks_yields_name_base_and_body()
    {
        var s = "class Foo : Bar { x = 1; }; class Baz { };";
        var blocks = CppText.ClassBlocks(s).ToList();
        blocks.Should().HaveCount(2);
        blocks[0].Name.Should().Be("Foo");
        blocks[0].Base.Should().Be("Bar");
        blocks[0].Body.Trim().Should().Be("x = 1;");
        blocks[1].Name.Should().Be("Baz");
        blocks[1].Base.Should().Be("");
    }

    [Fact]
    public void ClassBlocks_includes_nested_classes_in_document_order()
    {
        var s = "class Outer { class Inner { }; };";
        CppText.ClassBlocks(s).Select(b => b.Name).Should().ContainInOrder("Outer", "Inner");
    }

    [Fact]
    public void ClassBlocks_skips_forward_declarations()
    {
        // `class Foo;` has no body — only Bar should appear.
        var s = "class Foo; class Bar { };";
        CppText.ClassBlocks(s).Select(b => b.Name).Should().Equal("Bar");
    }

    [Fact]
    public void FindClassBody_is_case_insensitive_and_brace_aware()
    {
        var s = "class cfgpatches { class MyMod { requiredAddons[] = {\"DZ_Data\"}; }; };";
        var body = CppText.FindClassBody(s, "CfgPatches");
        body.Should().Contain("MyMod");
        CppText.FindClassBody(body, "MyMod").Should().Contain("requiredAddons");
        CppText.FindClassBody(s, "Missing").Should().Be("");
    }

    // --- ParseArrayValues ---

    [Fact]
    public void ParseArrayValues_returns_trimmed_values()
    {
        var s = "requiredAddons[] = { \"DZ_Data\" , \"DZ_Characters\" };";
        CppText.ParseArrayValues(s, "requiredAddons").Should().Equal("DZ_Data", "DZ_Characters");
    }

    [Fact]
    public void ParseArrayValues_distinguishes_missing_from_empty()
    {
        CppText.ParseArrayValues("x = 1;", "requiredAddons").Should().BeNull();
        CppText.ParseArrayValues("requiredAddons[] = {};", "requiredAddons").Should().BeEmpty();
    }

    [Fact]
    public void ParseArrayValues_accepts_append_syntax_and_multiline()
    {
        var s = "files[] += {\n \"a\",\n \"b\"\n};";
        CppText.ParseArrayValues(s, "files").Should().Equal("a", "b");
    }

    // --- LineOf ---

    [Fact]
    public void LineOf_is_one_based()
    {
        var s = "a\nb\nc";
        CppText.LineOf(s, 0).Should().Be(1);
        CppText.LineOf(s, s.IndexOf('b')).Should().Be(2);
        CppText.LineOf(s, s.IndexOf('c')).Should().Be(3);
        CppText.LineOf(s, -1).Should().Be(0);
    }

    // --- IncludeValues / ReadWithIncludes ---

    [Fact]
    public void IncludeValues_finds_quoted_and_angled_includes()
    {
        var s = "#include \"a.hpp\"\nx;\n  #include <b\\c.hpp>\n";
        CppText.IncludeValues(s).Should().Equal("a.hpp", "b\\c.hpp");
    }

    [Fact]
    public void ReadWithIncludes_inlines_resolved_includes_recursively()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "config.cpp"), "#include \"inc.hpp\"\nclass B {};");
        File.WriteAllText(Path.Combine(dir, "inc.hpp"), "class A {};");

        var content = CppText.ReadWithIncludes(Path.Combine(dir, "config.cpp"),
            (inc, from) => Path.Combine(Path.GetDirectoryName(from)!, inc));

        content.Should().Contain("class A").And.Contain("class B");
    }

    [Fact]
    public void ReadWithIncludes_survives_include_cycles()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "a.hpp"), "#include \"b.hpp\"\nclass A {};");
        File.WriteAllText(Path.Combine(dir, "b.hpp"), "#include \"a.hpp\"\nclass B {};");

        var content = CppText.ReadWithIncludes(Path.Combine(dir, "a.hpp"),
            (inc, from) => Path.Combine(Path.GetDirectoryName(from)!, inc));

        content.Should().Contain("class A").And.Contain("class B");
    }

    [Fact]
    public void ReadWithIncludes_leaves_unresolvable_includes_in_place()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "config.cpp"), "#include \"missing.hpp\"\nclass B {};");

        var content = CppText.ReadWithIncludes(Path.Combine(dir, "config.cpp"), (_, _) => null);

        content.Should().Contain("#include").And.Contain("class B");
    }
}
