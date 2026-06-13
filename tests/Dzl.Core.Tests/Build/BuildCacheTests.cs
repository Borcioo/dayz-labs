using Dzl.Core.Build;
using FluentAssertions;

public class BuildCacheTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    private static void Write(string dir, string rel, string content)
    {
        var path = Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Same_payload_and_settings_produce_the_same_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "class CfgPatches {};");
        Write(dir, @"data\a_co.paa", "texture");

        var h1 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");
        var h2 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Touching_mtime_without_content_change_keeps_the_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "class CfgPatches {};");
        var h1 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");

        File.SetLastWriteTimeUtc(Path.Combine(dir, "config.cpp"), DateTime.UtcNow.AddHours(1));
        var h2 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");
        h2.Should().Be(h1, "content didn't change — git-style mtime churn must not bust the cache");
    }

    [Fact]
    public void Content_change_changes_the_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "class CfgPatches {};");
        var h1 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");

        Write(dir, "config.cpp", "class CfgPatches { class X {}; };");
        BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s").Should().NotBe(h1);
    }

    [Fact]
    public void New_and_removed_files_change_the_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "x");
        var h1 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");

        Write(dir, @"data\new.paa", "y");
        var h2 = BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s");
        h2.Should().NotBe(h1);

        File.Delete(Path.Combine(dir, "data", "new.paa"));
        BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s").Should().Be(h1);
    }

    [Fact]
    public void Excluded_files_do_not_affect_the_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "x");
        var h1 = BuildCache.ComputeStateHash(dir, new[] { ".git" }, "s");

        Write(dir, @".git\HEAD", "ref: refs/heads/main");
        BuildCache.ComputeStateHash(dir, new[] { ".git" }, "s").Should().Be(h1);
    }

    [Fact]
    public void Settings_fingerprint_changes_the_hash()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "x");
        BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "sign=true")
            .Should().NotBe(BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "sign=false"));
    }

    [Fact]
    public void Fingerprint_reflects_file_identity()
    {
        BuildCache.Fingerprint(null).Should().Be("absent");
        BuildCache.Fingerprint(@"C:\missing\nope.exe").Should().Be("absent");

        var dir = Tmp();
        var exe = Path.Combine(dir, "tool.exe");
        File.WriteAllText(exe, "v1");
        var f1 = BuildCache.Fingerprint(exe);
        File.WriteAllText(exe, "v2");
        BuildCache.Fingerprint(exe).Should().NotBe(f1);
    }

    [Fact]
    public void Load_save_roundtrip()
    {
        var dir = Tmp();
        var cache = BuildCache.Load(dir);
        cache.Should().BeEmpty();

        cache["Foo"] = new BuildCache.Entry("abc", @"C:\x\Foo.pbo", DateTime.UtcNow);
        BuildCache.Save(dir, cache);

        var loaded = BuildCache.Load(dir);
        loaded.Should().ContainKey("foo");   // case-insensitive
        loaded["Foo"].Hash.Should().Be("abc");
    }

    [Fact]
    public void Corrupt_cache_file_loads_as_empty()
    {
        var dir = Tmp();
        File.WriteAllText(BuildCache.CachePath(dir), "{ not json !!");
        BuildCache.Load(dir).Should().BeEmpty();
    }

    [Fact]
    public void Sha1_memo_is_used_across_calls()
    {
        var dir = Tmp();
        Write(dir, "config.cpp", "x");
        var memo = new Dictionary<string, string>();
        BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s", memo);
        memo.Should().NotBeEmpty();
        var memoCount = memo.Count;
        BuildCache.ComputeStateHash(dir, Array.Empty<string>(), "s", memo);
        memo.Count.Should().Be(memoCount);
    }
}
