using System.Text;
using Dzl.Core.Build;
using Dzl.Core.Tools;
using FluentAssertions;

public class PboHeaderTests
{
    /// <summary>Builds a minimal, format-correct PBO: Vers properties entry (+prefix), one file
    /// entry, terminator, data block, sha1 trailer (trailer content irrelevant to the reader).</summary>
    private static string WritePbo(string dir, string? prefix, params (string Name, byte[] Data)[] files)
    {
        var path = Path.Combine(dir, "test.pbo");
        using var bw = new BinaryWriter(File.Create(path), Encoding.ASCII);
        void CString(string s) { bw.Write(Encoding.ASCII.GetBytes(s)); bw.Write((byte)0); }
        void EntryHeader(string name, uint packing, uint size)
        { CString(name); bw.Write(packing); bw.Write(size); bw.Write(0u); bw.Write(0u); bw.Write(size); }

        EntryHeader("", 0x56657273, 0);   // Vers / properties
        if (prefix is not null) { CString("prefix"); CString(prefix); }
        bw.Write((byte)0);                 // properties terminator

        foreach (var (name, data) in files) EntryHeader(name, 0, (uint)data.Length);
        EntryHeader("", 0, 0);             // entry terminator

        foreach (var (_, data) in files) bw.Write(data);
        bw.Write((byte)0);
        bw.Write(new byte[20]);            // sha1 trailer
        return path;
    }

    [Fact]
    public void Reads_prefix_and_entries_with_offsets()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var pbo = WritePbo(dir, @"MyMod\Stuff",
            ("config.bin", new byte[10]),
            (@"data\x_co.paa", new byte[20]));

        var info = PboHeader.Read(pbo)!;
        info.Should().NotBeNull();
        info.Prefix.Should().Be(@"MyMod\Stuff");
        info.Entries.Should().HaveCount(2);
        info.Entries[0].Name.Should().Be("config.bin");
        info.Entries[0].DataSize.Should().Be(10);
        info.Entries[1].Offset.Should().Be(info.Entries[0].Offset + 10);
    }

    [Fact]
    public void Pbo_without_prefix_property_reads_with_empty_prefix()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var pbo = WritePbo(dir, null, ("config.bin", new byte[3]));
        PboHeader.Read(pbo)!.Prefix.Should().Be("");
    }

    [Fact]
    public void Garbage_file_returns_null_not_throw()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bad = Path.Combine(dir, "bad.pbo");
        File.WriteAllText(bad, "this is not a pbo at all");
        PboHeader.Read(bad).Should().BeNull();
        PboHeader.Read(Path.Combine(dir, "missing.pbo")).Should().BeNull();
    }

    [Fact]
    public void FindSignature_picks_the_matching_bisign()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var pbo = Path.Combine(dir, "Foo.pbo");
        File.WriteAllText(pbo, "x");
        PboHeader.FindSignature(pbo).Should().BeNull();
        File.WriteAllText(pbo + ".MyKey.bisign", "sig");
        PboHeader.FindSignature(pbo).Should().EndWith(".MyKey.bisign");
    }
}

public class PublishAtomicallyTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Moves_pbo_and_bisign_into_place()
    {
        var work = Tmp(); var final_ = Tmp();
        File.WriteAllText(Path.Combine(work, "Foo.pbo"), "new");
        File.WriteAllText(Path.Combine(work, "Foo.pbo.Key.bisign"), "sig");

        var (ok, _) = Dzl.Core.Build.ModBuild.PublishAtomically(work, final_);
        ok.Should().BeTrue();
        File.ReadAllText(Path.Combine(final_, "Foo.pbo")).Should().Be("new");
        File.Exists(Path.Combine(final_, "Foo.pbo.Key.bisign")).Should().BeTrue();
        Directory.EnumerateFiles(work).Should().BeEmpty();
    }

    [Fact]
    public void Replaces_previous_output_and_drops_backup()
    {
        var work = Tmp(); var final_ = Tmp();
        File.WriteAllText(Path.Combine(final_, "Foo.pbo"), "old");
        File.WriteAllText(Path.Combine(final_, "Foo.pbo.Old.bisign"), "oldsig");
        File.WriteAllText(Path.Combine(work, "Foo.pbo"), "new");

        var (ok, _) = Dzl.Core.Build.ModBuild.PublishAtomically(work, final_);
        ok.Should().BeTrue();
        File.ReadAllText(Path.Combine(final_, "Foo.pbo")).Should().Be("new");
        // Stale signature from the previous build must not survive (server would reject the new pbo).
        File.Exists(Path.Combine(final_, "Foo.pbo.Old.bisign")).Should().BeFalse();
        Directory.EnumerateDirectories(final_).Should().BeEmpty();   // backup dir cleaned
    }

    [Fact]
    public void Empty_work_dir_fails_cleanly()
    {
        var (ok, detail) = Dzl.Core.Build.ModBuild.PublishAtomically(Tmp(), Tmp());
        ok.Should().BeFalse();
        detail.Should().Contain("nothing to publish");
    }
}

public class AddonBuilderArgsTests
{
    [Fact]
    public void PackArgs_includes_temp_and_include_when_set()
    {
        var args = AddonBuilder.PackArgs(@"P:\Foo", @"D:\out", clear: false, packOnly: false,
            prefix: null, signKey: null, tempDir: @"D:\tmp\Foo", includeFile: @"D:\inc.lst");
        args.Should().Contain(@"-temp=D:\tmp\Foo");
        args.Should().Contain(@"-include=D:\inc.lst");
    }

    [Fact]
    public void PackArgs_omits_optional_args_when_null()
    {
        var args = AddonBuilder.PackArgs(@"P:\Foo", @"D:\out", true, true, "Foo", @"D:\k.biprivatekey");
        args.Should().NotContain(a => a.StartsWith("-temp="));
        args.Should().NotContain(a => a.StartsWith("-include="));
        args.Should().Contain("-clear").And.Contain("-packonly")
            .And.Contain("-prefix=Foo").And.Contain(@"-sign=D:\k.biprivatekey");
    }

    [Fact]
    public void WriteIncludeFile_writes_semicolon_separated_single_line()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = AddonBuilder.WriteIncludeFile(dir);
        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(1, "AddonBuilder's binarize path rejects newline-separated include lists");
        lines[0].Should().Contain("*.xml;").And.Contain("*.layout");
    }
}
