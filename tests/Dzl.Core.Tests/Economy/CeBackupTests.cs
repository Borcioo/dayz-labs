using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

public class CeBackupTests
{
    private static string TempFile(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dzl-cebackup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "types.xml");
        File.WriteAllText(f, content);
        return f;
    }

    [Fact]
    public void Snapshot_creates_a_timestamped_copy_listed_newest_first()
    {
        var f = TempFile("<types/>");
        CeBackup.Snapshot(f);
        File.WriteAllText(f, "<types><type name=\"x\"/></types>");
        CeBackup.Snapshot(f);
        var list = CeBackup.List(f);
        list.Should().HaveCount(2);
        list[0].Created.Should().BeOnOrAfter(list[1].Created);
    }

    [Fact]
    public void Restore_brings_back_the_chosen_snapshot_contents()
    {
        var f = TempFile("<types>original</types>");
        CeBackup.Snapshot(f);
        File.WriteAllText(f, "<types>changed</types>");
        var snap = CeBackup.List(f)[0];
        CeBackup.Restore(f, snap.Id).Should().BeTrue();
        File.ReadAllText(f).Should().Contain("original");
    }

    [Fact]
    public void Snapshot_prunes_to_the_retention_limit()
    {
        var f = TempFile("<types/>");
        for (int i = 0; i < 25; i++) { File.WriteAllText(f, $"<types n=\"{i}\"/>"); CeBackup.Snapshot(f); }
        CeBackup.List(f).Count.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void Restore_accepts_a_full_backup_path_from_List()
    {
        var f = TempFile("<types>original</types>");
        CeBackup.Snapshot(f);
        File.WriteAllText(f, "<types>changed</types>");
        var fullPath = CeBackup.List(f)[0].Path;
        TypesBackup.Restore(f, fullPath).Should().BeTrue();
        File.ReadAllText(f).Should().Contain("original");
    }
}
