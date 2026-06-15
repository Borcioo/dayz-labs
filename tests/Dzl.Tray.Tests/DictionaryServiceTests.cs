using Dzl.Core.App;
using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Tests for <see cref="DictionaryService"/>'s identifier guard — CE usage/value/tag/category names
/// are bare identifiers matched literally by the engine and written as XML attributes, so whitespace and
/// XML-reserved characters must be rejected at the source instead of silently persisted.</summary>
public class DictionaryServiceTests
{
    private static DictionaryService Svc()
    {
        var cfg = CeScaffold.Mission(("cfglimitsdefinition.xml",
            "<lists><categories/><tags/><usageflags><usage name=\"Military\"/></usageflags><valueflags/></lists>"));
        return new DictionaryService(cfg);
    }

    [Theory]
    [InlineData("Town Square")]   // whitespace never resolves against a literal flag reference
    [InlineData("a<b")]           // XML-reserved
    [InlineData("x&y")]
    [InlineData("q\"r")]
    public void AddName_rejects_invalid_identifiers(string bad) =>
        Svc().AddName(LimitsKind.Usage, bad).ok.Should().BeFalse();

    [Fact]
    public void AddName_accepts_and_trims_a_clean_identifier()
    {
        var svc = Svc();
        svc.AddName(LimitsKind.Usage, "  Police  ").ok.Should().BeTrue("a clean identifier is accepted and trimmed");
    }

    [Fact]
    public void RenameName_rejects_an_invalid_new_name()
    {
        var svc = Svc();
        svc.RenameName(LimitsKind.Usage, "Military", "Mil itary").ok.Should().BeFalse();
    }

    [Fact]
    public void RenameGroup_renames_a_combo_preserving_members()
    {
        var cfg = CeScaffold.Mission(
            ("cfglimitsdefinitionuser.xml",
             "<user_lists><usageflags><user name=\"TownVillage\"><usage name=\"Town\"/><usage name=\"Village\"/></user></usageflags><valueflags/></user_lists>"));
        var svc = new DictionaryService(cfg);

        svc.RenameGroup(LimitsKind.Usage, "TownVillage", "Settlements").ok.Should().BeTrue();

        var groups = svc.LoadGroups();
        groups.Select(g => g.Name).Should().Contain("Settlements").And.NotContain("TownVillage");
        groups.Single(g => g.Name == "Settlements").Members.Should().BeEquivalentTo("Town", "Village");
    }

    [Fact]
    public void RenameGroup_rejects_an_invalid_new_name()
    {
        var cfg = CeScaffold.Mission(
            ("cfglimitsdefinitionuser.xml", "<user_lists><usageflags><user name=\"A\"/></usageflags><valueflags/></user_lists>"));
        new DictionaryService(cfg).RenameGroup(LimitsKind.Usage, "A", "bad name").ok.Should().BeFalse();
    }
}
