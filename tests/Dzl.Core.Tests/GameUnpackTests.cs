using Dzl.Core.Tools;
using FluentAssertions;

public class GameUnpackTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void BankRevArgs_extract_to_dest_at_pbo_prefix()
        => GameUnpack.BankRevArgs(@"E:\DayZ\Addons\worlds_enoch.pbo", @"P:\")
            .Should().Equal("-f", @"P:\", "-prefix", @"E:\DayZ\Addons\worlds_enoch.pbo");

    [Fact]
    public void FindGamePbos_lists_vanilla_pbos_only_excluding_mods_and_workshop()
    {
        var game = Tmp();
        Directory.CreateDirectory(Path.Combine(game, "Addons"));
        Directory.CreateDirectory(Path.Combine(game, "sakhal", "Addons"));
        Directory.CreateDirectory(Path.Combine(game, "dta"));
        Directory.CreateDirectory(Path.Combine(game, "@CF", "addons"));
        Directory.CreateDirectory(Path.Combine(game, "!Workshop", "@SomeMod", "addons"));
        // vanilla
        File.WriteAllText(Path.Combine(game, "Addons", "worlds_enoch.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "Addons", "surfaces_bliss.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "sakhal", "Addons", "worlds_sakhal_data.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "dta", "bin.pbo"), "x");           // vanilla, NOT under Addons
        File.WriteAllText(Path.Combine(game, "Addons", "readme.txt"), "x");     // not a pbo
        // mods / workshop — must be excluded
        File.WriteAllText(Path.Combine(game, "@CF", "addons", "cf_gui.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "!Workshop", "@SomeMod", "addons", "mod.pbo"), "x");

        var pbos = GameUnpack.FindGamePbos(game);

        pbos.Select(Path.GetFileName).Should().BeEquivalentTo(
            "bin.pbo", "surfaces_bliss.pbo", "worlds_enoch.pbo", "worlds_sakhal_data.pbo");   // vanilla only, no mod PBOs
    }

    [Theory]
    [InlineData(@"Addons\worlds_enoch.pbo", false)]
    [InlineData(@"sakhal\Addons\worlds_sakhal_data.pbo", false)]
    [InlineData(@"dta\bin.pbo", false)]
    [InlineData(@"@CF\addons\cf_gui.pbo", true)]
    [InlineData(@"!Workshop\@SomeMod\addons\mod.pbo", true)]
    [InlineData(@"!dzsal\x.pbo", true)]
    public void IsUnderModFolder_flags_at_and_bang_prefixed_segments(string rel, bool isMod)
        => GameUnpack.IsUnderModFolder(@"C:\DayZ", $@"C:\DayZ\{rel}").Should().Be(isMod);

    [Fact]
    public void FindGamePbos_empty_for_missing_dir()
        => GameUnpack.FindGamePbos(Path.Combine(Tmp(), "nope")).Should().BeEmpty();

    [Fact]
    public void Stamp_changes_when_the_pbo_changes()
    {
        var f = Path.Combine(Tmp(), "a.pbo");
        File.WriteAllText(f, "one");
        var s1 = GameUnpack.Stamp(new FileInfo(f));
        File.WriteAllText(f, "one-much-longer-now");   // size + mtime change
        var s2 = GameUnpack.Stamp(new FileInfo(f));
        s2.Should().NotBe(s1);
    }

    [Fact]
    public void UnpackAll_fails_cleanly_without_BankRev()
    {
        var r = GameUnpack.UnpackAll(Path.Combine(Tmp(), "BankRev.exe"), Tmp(), Tmp(), force: false);
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("BankRev.exe not found");
    }
}
