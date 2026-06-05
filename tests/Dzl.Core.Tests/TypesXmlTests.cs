using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

public class TypesXmlTests
{
    private const string Sample = """
<?xml version="1.0" encoding="UTF-8"?>
<types>
    <!-- keep this comment -->
    <type name="AKM">
        <nominal>10</nominal>
        <lifetime>7200</lifetime>
        <restock>0</restock>
        <min>5</min>
        <quantmin>-1</quantmin>
        <quantmax>-1</quantmax>
        <cost>100</cost>
        <flags count_in_cargo="0" count_in_hoarder="0" count_in_map="1" count_in_player="0" crafted="0" deloot="0"/>
        <category name="weapons"/>
        <usage name="Military"/>
        <value name="Tier3"/>
        <value name="Tier4"/>
    </type>
</types>
""";

    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Parse_reads_fields_flags_and_multi_value()
    {
        var t = TypesXml.Parse(Sample).Should().ContainSingle().Subject;
        t.Name.Should().Be("AKM");
        t.Nominal.Should().Be(10);
        t.Min.Should().Be(5);
        t.Lifetime.Should().Be(7200);
        t.Cost.Should().Be(100);
        t.Flags.CountInMap.Should().BeTrue();
        t.Flags.CountInCargo.Should().BeFalse();
        t.Category.Should().Be("weapons");
        t.Usage.Should().ContainSingle().Which.Should().Be("Military");
        t.Value.Should().BeEquivalentTo("Tier3", "Tier4");
    }

    [Fact]
    public void Upsert_updates_in_place_and_preserves_the_comment()
    {
        var doc = TypesXml.ParseDoc(Sample);
        var akm = TypesXml.ReadType(doc.Root!.Elements("type").First());
        TypesXml.Upsert(doc, akm with { Nominal = 25, Category = "weapons" }).Should().BeTrue();   // existed
        var xml = TypesXml.ToXml(doc);
        xml.Should().Contain("keep this comment");          // round-trip preserved other content
        TypesXml.Parse(xml).Single().Nominal.Should().Be(25);
    }

    [Fact]
    public void Upsert_appends_a_new_type()
    {
        var doc = TypesXml.ParseDoc(Sample);
        TypesXml.Upsert(doc, new TypeEntry { Name = "Mosin9130", Nominal = 8, Category = "weapons", Value = new[] { "Tier2" } })
            .Should().BeFalse();   // new
        var all = TypesXml.Parse(TypesXml.ToXml(doc));
        all.Select(t => t.Name).Should().Contain("AKM").And.Contain("Mosin9130");
        all.Single(t => t.Name == "Mosin9130").Value.Should().ContainSingle().Which.Should().Be("Tier2");
    }

    [Fact]
    public void Remove_drops_the_type()
    {
        var doc = TypesXml.ParseDoc(Sample);
        TypesXml.Remove(doc, "akm").Should().BeTrue();    // case-insensitive
        TypesXml.Parse(TypesXml.ToXml(doc)).Should().BeEmpty();
        TypesXml.Remove(doc, "Nope").Should().BeFalse();
    }

    // --- backups ---

    [Fact]
    public void Backup_snapshots_lists_newest_first_and_restores()
    {
        var dir = Tmp();
        var f = Path.Combine(dir, "types.xml");
        File.WriteAllText(f, "<types><type name=\"A\"/></types>");

        TypesBackup.Snapshot(f, "20260101-100000").Should().EndWith("types.20260101-100000.xml");
        File.WriteAllText(f, "<types><type name=\"B\"/></types>");
        var b2 = TypesBackup.Snapshot(f, "20260101-110000");

        var list = TypesBackup.List(f);
        list.Should().HaveCount(2);
        list[0].Stamp.Should().Be("20260101-110000");   // newest first

        // restore the first snapshot (state "A"); current ("B") is snapshotted first → 3 backups
        var first = list.Single(x => x.Stamp == "20260101-100000");
        TypesBackup.Restore(f, first.Path).Should().BeTrue();
        File.ReadAllText(f).Should().Contain("name=\"A\"");
        TypesBackup.List(f).Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Backup_prunes_to_keep_newest()
    {
        var dir = Tmp();
        var f = Path.Combine(dir, "types.xml");
        File.WriteAllText(f, "<types/>");
        for (var i = 0; i < TypesBackup.Keep + 5; i++)
            TypesBackup.Snapshot(f, $"20260101-{i:000000}");
        TypesBackup.List(f).Count.Should().Be(TypesBackup.Keep);
    }

    // --- TypesService (locate + edit the active instance mission) ---

    private static string ServiceConfig(string sampleXml)
    {
        var dir = Tmp();
        var configPath = Path.Combine(dir, "config.json");
        var instDir = Path.Combine(dir, "servers", "test");
        Directory.CreateDirectory(instDir);
        var cfgFile = Path.Combine(instDir, "serverDZ.cfg");
        File.WriteAllText(cfgFile, "");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = dir }, configPath);
        Profiles.Save(DzlConfig.Default() with { ConfigName = cfgFile }, "test", configPath);
        Profiles.SetActive("test", configPath);
        var db = Path.Combine(instDir, "mpmissions", "dayzOffline.chernarusplus", "db");
        Directory.CreateDirectory(db);
        File.WriteAllText(Path.Combine(db, "types.xml"), sampleXml);
        return configPath;
    }

    [Fact]
    public void Service_locates_and_lists_and_sets()
    {
        var configPath = ServiceConfig(Sample);
        var svc = new TypesService(configPath);
        svc.TypesFile().Should().EndWith("types.xml");
        svc.List().Should().ContainSingle(t => t.Name == "AKM");

        svc.Set("AKM", nominal: 25).Ok.Should().BeTrue();
        svc.List().Single(t => t.Name == "AKM").Nominal.Should().Be(25);

        svc.Set("Mosin9130", nominal: 8).Message.Should().Contain("added");
        svc.List().Should().HaveCount(2);

        svc.Remove("AKM").Ok.Should().BeTrue();
        svc.List().Select(t => t.Name).Should().NotContain("AKM");
    }

    [Fact]
    public void Service_returns_null_when_no_mission_types()
    {
        var dir = Tmp();
        var configPath = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = dir }, configPath);
        new TypesService(configPath).TypesFile().Should().BeNull();
    }
}
