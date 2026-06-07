using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;
using FluentAssertions;
using Xunit;

public class LintTests
{
    private static LimitsDef Limits => new(
        new HashSet<string>{"Military"}, new HashSet<string>{"Tier1"},
        new HashSet<string>{"floor"}, new HashSet<string>{"weapons"});

    [Fact]
    public void Unknown_usage_value_tag_category_are_warnings()
    {
        var set = new CeFileSet(new[]
        {
            new TypeEntry { Name = "X", SourceFile="f", Usage = new[]{"Nope"}, Category = "ghost" }
        });
        var findings = new LintEngine().Run(set, Limits);
        findings.Should().Contain(f => f.Code == "unknown-usage" && f.Severity == LintSeverity.Warning);
        findings.Should().Contain(f => f.Code == "unknown-category");
    }

    [Fact]
    public void Duplicate_name_across_files_is_an_error()
    {
        var set = new CeFileSet(new[]
        {
            new TypeEntry { Name = "Dup", SourceFile="a" },
            new TypeEntry { Name = "Dup", SourceFile="b" },
        });
        new LintEngine().Run(set, Limits)
            .Should().Contain(f => f.Code == "duplicate-name" && f.Severity == LintSeverity.Error);
    }

    [Fact]
    public void Min_greater_than_nominal_is_a_warning()
    {
        var set = new CeFileSet(new[] { new TypeEntry { Name="Y", SourceFile="f", Nominal=2, Min=5 } });
        new LintEngine().Run(set, Limits).Should().Contain(f => f.Code == "min-gt-nominal");
    }

    [Fact]
    public void Empty_name_is_an_error()
    {
        var set = new CeFileSet(new[] { new TypeEntry { Name = "", SourceFile = "f" } });
        new LintEngine().Run(set, Limits)
            .Should().Contain(f => f.Code == "empty-name" && f.Severity == LintSeverity.Error);
    }

    [Fact]
    public void Unknown_value_and_tag_are_warnings()
    {
        var set = new CeFileSet(new[]
        {
            new TypeEntry { Name = "X", SourceFile = "f", Value = new[] { "BadTier" }, Tag = new[] { "BadTag" } }
        });
        var findings = new LintEngine().Run(set, Limits);
        findings.Should().Contain(f => f.Code == "unknown-value" && f.Severity == LintSeverity.Warning);
        findings.Should().Contain(f => f.Code == "unknown-tag" && f.Severity == LintSeverity.Warning);
    }

    [Fact]
    public void Quantmin_greater_than_quantmax_is_a_warning()
    {
        var set = new CeFileSet(new[] { new TypeEntry { Name = "Y", SourceFile = "f", QuantMin = 10, QuantMax = 2 } });
        new LintEngine().Run(set, Limits)
            .Should().Contain(f => f.Code == "quant-min-gt-max");

        // defaults (QuantMin=-1, QuantMax=-1) must not produce a quant finding
        var clean = new CeFileSet(new[] { new TypeEntry { Name = "Y", SourceFile = "f" } });
        new LintEngine().Run(clean, Limits)
            .Should().NotContain(f => f.Code == "quant-min-gt-max");
    }

    [Fact]
    public void Clean_entry_with_known_values_produces_no_findings()
    {
        var set = new CeFileSet(new[]
        {
            new TypeEntry { Name="Z", SourceFile="f", Nominal=5, Min=1, Usage=new[]{"Military"}, Category="weapons" }
        });
        new LintEngine().Run(set, Limits).Should().BeEmpty();
    }
}
