using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

public class TypesServiceMultiFileTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    private const string VanillaTypes = """
<?xml version="1.0" encoding="UTF-8"?>
<types>
    <!-- vanilla keep-me comment -->
    <type name="Apple">
        <nominal>20</nominal>
        <min>10</min>
        <cost>100</cost>
        <usage name="Farm"/>
    </type>
</types>
""";

    private const string ModTypes = """
<?xml version="1.0" encoding="UTF-8"?>
<types>
    <type name="AKM">
        <nominal>5</nominal>
        <min>2</min>
        <cost>100</cost>
        <usage name="Military"/>
    </type>
    <type name="BadUsageGun">
        <nominal>3</nominal>
        <min>1</min>
        <cost>100</cost>
        <usage name="TotallyNotAUsage"/>
    </type>
</types>
""";

    private const string EconomyCoreXml = """
<?xml version="1.0" encoding="UTF-8"?>
<economycore>
    <classes/>
    <defaults/>
    <ce folder="ce/MyMod">
        <file name="mymod_types.xml" type="types"/>
    </ce>
</economycore>
""";

    private const string LimitsXmlText = """
<?xml version="1.0" encoding="UTF-8"?>
<lists>
    <usageflags>
        <usage name="Military"/>
        <usage name="Farm"/>
    </usageflags>
    <valueflags>
        <value name="Tier1"/>
    </valueflags>
    <tags>
        <tag name="floor"/>
    </tags>
    <categories>
        <category name="weapons"/>
    </categories>
</lists>
""";

    /// <summary>Build a temp mission with a vanilla db/types.xml, a cfgeconomycore.xml referencing a mod
    /// types file, that mod file, and cfglimitsdefinition.xml. Returns (configPath, missionDir).
    /// Set <paramref name="withEconomyCore"/> false to omit cfgeconomycore.xml.</summary>
    private static (string configPath, string missionDir) Scaffold(bool withEconomyCore = true, bool withLimits = true)
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

        var missionDir = Path.Combine(instDir, "mpmissions", "dayzOffline.chernarusplus");
        var db = Path.Combine(missionDir, "db");
        Directory.CreateDirectory(db);
        File.WriteAllText(Path.Combine(db, "types.xml"), VanillaTypes);

        var modDir = Path.Combine(missionDir, "ce", "MyMod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mymod_types.xml"), ModTypes);

        if (withEconomyCore)
            File.WriteAllText(Path.Combine(missionDir, "cfgeconomycore.xml"), EconomyCoreXml);
        if (withLimits)
            File.WriteAllText(Path.Combine(missionDir, "cfglimitsdefinition.xml"), LimitsXmlText);

        return (configPath, missionDir);
    }

    [Fact]
    public void List_unions_entries_from_all_referenced_files_with_source()
    {
        var (configPath, _) = Scaffold();
        var svc = new TypesService(configPath);
        var list = svc.List();

        var apple = list.Should().ContainSingle(t => t.Name == "Apple").Subject;
        var akm = list.Should().ContainSingle(t => t.Name == "AKM").Subject;

        akm.SourceFile.Should().EndWith("mymod_types.xml");
        apple.SourceFile.Replace('/', '\\').Should().EndWith(Path.Combine("db", "types.xml"));
    }

    [Fact]
    public void Rows_carry_origin_per_source_file()
    {
        var (configPath, _) = Scaffold();
        var rows = new TypesService(configPath).Rows();

        var apple = rows.Should().ContainSingle(r => r.Entry.Name == "Apple").Subject;
        apple.Origin.Should().Be(CeOrigin.Vanilla);

        var akm = rows.Should().ContainSingle(r => r.Entry.Name == "AKM").Subject;
        akm.Origin.Should().Be(CeOrigin.Mod);
        akm.ModSource.Should().Be("MyMod");
    }

    [Fact]
    public void SaveAll_writes_each_entry_back_to_its_own_file_only()
    {
        var (configPath, missionDir) = Scaffold();
        var svc = new TypesService(configPath);

        var entries = svc.List().Select(e => e.Name == "AKM" ? e with { Nominal = 42 } : e).ToList();
        svc.SaveAll(entries).Ok.Should().BeTrue();

        var reloaded = new TypesService(configPath).List();
        reloaded.Single(t => t.Name == "AKM").Nominal.Should().Be(42);

        // vanilla file round-trip: comment preserved, AKM never bled into it.
        var vanilla = File.ReadAllText(Path.Combine(missionDir, "db", "types.xml"));
        vanilla.Should().Contain("vanilla keep-me comment");
        vanilla.Should().NotContain("AKM");
    }

    [Fact]
    public void Lint_reports_unknown_usage_against_cfglimitsdefinition()
    {
        var (configPath, _) = Scaffold();
        new TypesService(configPath).Lint()
            .Should().Contain(f => f.Code == "unknown-usage");
    }

    [Fact]
    public void List_falls_back_to_vanilla_when_no_cfgeconomycore()
    {
        var (configPath, _) = Scaffold(withEconomyCore: false);
        var list = new TypesService(configPath).List();
        list.Should().ContainSingle(t => t.Name == "Apple");
        list.Should().NotContain(t => t.Name == "AKM");
    }

    [Fact]
    public void Limits_reads_cfglimitsdefinition()
    {
        var (configPath, _) = Scaffold();
        var limits = new TypesService(configPath).Limits();
        limits.Usage.Should().Contain("Military");
        limits.Category.Should().Contain("weapons");
    }

    /// <summary>M3 — a brand-new entry with empty SourceFile must land in the primary (vanilla) file,
    /// not create an orphan path, and must NOT appear in the mod file.</summary>
    [Fact]
    public void SaveAll_new_entry_with_empty_SourceFile_lands_in_primary()
    {
        var (configPath, missionDir) = Scaffold();
        var svc = new TypesService(configPath);

        var entries = svc.List();
        var newGun = new TypeEntry { Name = "NewGun", SourceFile = "", Nominal = 3 };
        var allEntries = entries.Append(newGun).ToList();

        svc.SaveAll(allEntries).Ok.Should().BeTrue();

        // Reload and verify NewGun exists
        var reloaded = new TypesService(configPath).List();
        var found = reloaded.Should().ContainSingle(t => t.Name == "NewGun").Subject;

        // Its SourceFile after reload should be the vanilla db\types.xml (the primary)
        var vanillaPath = Path.Combine(missionDir, "db", "types.xml");
        found.SourceFile.Replace('/', '\\').Should().Be(vanillaPath.Replace('/', '\\'),
            because: "a new entry with empty SourceFile must be routed to the primary file");

        // The mod file must NOT have gained NewGun
        var modFile = File.ReadAllText(Path.Combine(missionDir, "ce", "MyMod", "mymod_types.xml"));
        modFile.Should().NotContain("NewGun");
    }

    /// <summary>M4 — Remove("AKM") touches only the mod file; the vanilla file is left completely
    /// unchanged (Apple survives, keep-me comment survives).</summary>
    [Fact]
    public void Remove_touches_only_the_file_containing_the_named_type()
    {
        var (configPath, missionDir) = Scaffold();
        var svc = new TypesService(configPath);

        var result = svc.Remove("AKM");
        result.Ok.Should().BeTrue();

        // Reload: AKM gone, Apple present
        var reloaded = new TypesService(configPath).List();
        reloaded.Should().NotContain(t => t.Name == "AKM");
        reloaded.Should().ContainSingle(t => t.Name == "Apple");

        // Vanilla file is byte-for-byte untouched — comment must still be there, AKM never mentioned
        var vanilla = File.ReadAllText(Path.Combine(missionDir, "db", "types.xml"));
        vanilla.Should().Contain("vanilla keep-me comment");
        vanilla.Should().NotContain("AKM");
    }
}
