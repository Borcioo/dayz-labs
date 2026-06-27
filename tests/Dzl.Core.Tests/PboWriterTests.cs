using System.Text;
using Dzl.Core.Build;
using FluentAssertions;

public class PboWriterTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Theory]
    [InlineData("config.cpp", false)]   // always kept
    [InlineData("config.bin", false)]   // always kept
    [InlineData("model.p3d", false)]
    [InlineData("data.paa", false)]
    [InlineData(".gitignore", true)]
    [InlineData("thumbs.db", true)]
    [InlineData("stale.delete", true)]
    [InlineData("$PBOPREFIX$", true)]   // user prefix file dropped (a synthetic one is injected)
    public void ShouldSkipFile_matches_the_RaG_exclude_contract(string name, bool skip)
        => PboWriter.ShouldSkipFile(name).Should().Be(skip);

    [Fact]
    public void Pack_writes_a_pbo_with_the_prefix_keeps_config_and_excludes_junk()
    {
        var src = Tmp();
        Directory.CreateDirectory(Path.Combine(src, "data"));
        File.WriteAllText(Path.Combine(src, "config.cpp"), "class CfgPatches {};");
        File.WriteAllText(Path.Combine(src, "data", "keep.txt"), "KEEP_THIS_CONTENT_XYZ");
        File.WriteAllText(Path.Combine(src, ".gitignore"), "DROP_THIS_CONTENT_QWE");
        File.WriteAllText(Path.Combine(src, "$PBOPREFIX$"), "junk-user-prefix");

        var pbo = Path.Combine(Tmp(), "Mod.pbo");
        var (ok, _) = PboWriter.Pack(src, pbo, @"Mod\Core");

        ok.Should().BeTrue();
        File.Exists(pbo).Should().BeTrue();
        new FileInfo(pbo).Length.Should().BeGreaterThan(21);   // header + data + 1-byte zero + 20-byte SHA1

        PboWriter.ReadPrefix(pbo).Should().Be(@"Mod\Core");

        var bytes = File.ReadAllBytes(pbo);
        var text = Encoding.ASCII.GetString(bytes);
        text.Should().Contain("config.cpp").And.Contain("KEEP_THIS_CONTENT_XYZ");
        text.Should().NotContain("DROP_THIS_CONTENT_QWE");          // excluded file body absent
        text.Should().Contain("$PBOPREFIX$");                       // synthetic prefix entry present
        text.Should().Contain("Mod\\Core\r\n");                     // its body = normalized prefix + CRLF
    }

    [Fact]
    public void Pack_stores_file_bytes_uncompressed_and_byte_identical()
    {
        var src = Tmp();
        var payload = new byte[5000];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7 % 251);
        File.WriteAllBytes(Path.Combine(src, "blob.bin"), payload);
        File.WriteAllText(Path.Combine(src, "config.cpp"), "x");

        var pbo = Path.Combine(Tmp(), "Out.pbo");
        PboWriter.Pack(src, pbo, "Out").ok.Should().BeTrue();

        // the raw payload must appear verbatim (stored, not compressed) inside the pbo
        var hay = File.ReadAllBytes(pbo);
        IndexOf(hay, payload).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Pack_fails_cleanly_for_a_missing_source()
    {
        var (ok, msg) = PboWriter.Pack(Path.Combine(Tmp(), "nope"), Path.Combine(Tmp(), "x.pbo"), "X");
        ok.Should().BeFalse();
        msg.Should().Contain("not a directory");
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= hay.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }
}
