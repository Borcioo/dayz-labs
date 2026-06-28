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
    public void FindGamePbos_lists_every_pbo_recursively_sorted_ignoring_other_files()
    {
        var game = Tmp();
        Directory.CreateDirectory(Path.Combine(game, "Addons"));
        Directory.CreateDirectory(Path.Combine(game, "sakhal", "Addons"));
        File.WriteAllText(Path.Combine(game, "Addons", "worlds_enoch.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "Addons", "surfaces_bliss.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "sakhal", "Addons", "worlds_sakhal_data.pbo"), "x");
        File.WriteAllText(Path.Combine(game, "Addons", "readme.txt"), "x");   // ignored

        var pbos = GameUnpack.FindGamePbos(game);

        pbos.Select(Path.GetFileName).Should().Equal(
            "surfaces_bliss.pbo", "worlds_enoch.pbo", "worlds_sakhal_data.pbo");   // sorted, .pbo only
    }

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
