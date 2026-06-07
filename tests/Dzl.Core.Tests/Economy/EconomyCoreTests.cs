using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

public class EconomyCoreTests
{
    private const string Xml = """
    <economycore>
      <ce folder="ce/MyMod">
        <file name="mymod_types.xml" type="types" />
        <file name="mymod_spawnabletypes.xml" type="spawnabletypes" />
      </ce>
      <ce folder="custom">
        <file name="extra_types.xml" type="types" />
      </ce>
    </economycore>
    """;

    [Fact]
    public void Parse_returns_each_ce_file_with_kind_origin_and_modsource()
    {
        var refs = EconomyCore.Parse(Xml, @"C:\mission");
        refs.Should().HaveCount(3);

        var modTypes = refs.Single(r => r.Path.EndsWith("mymod_types.xml"));
        modTypes.Kind.Should().Be(CeKind.Types);
        modTypes.Origin.Should().Be(CeOrigin.Mod);
        modTypes.ModSource.Should().Be("MyMod");
        modTypes.Path.Should().Be(Path.GetFullPath(Path.Combine(@"C:\mission", "ce", "MyMod", "mymod_types.xml")));

        refs.Single(r => r.Path.EndsWith("mymod_spawnabletypes.xml")).Kind.Should().Be(CeKind.SpawnableTypes);
    }

    [Fact]
    public void Parse_classifies_non_ce_folder_as_custom()
    {
        var refs = EconomyCore.Parse(Xml, @"C:\mission");
        refs.Single(r => r.Path.EndsWith("extra_types.xml")).Origin.Should().Be(CeOrigin.Custom);
    }

    [Fact]
    public void Parse_throws_on_malformed_xml()
    {
        var act = () => EconomyCore.Parse("<economycore", @"C:\mission");
        act.Should().Throw<System.Exception>();
    }
}
