using Dzl.Core.Build;
using FluentAssertions;

public class BuildAssetsTests
{
    [Fact]
    public void HasBinarizedP3d_true_when_a_p3d_starts_with_ODOL()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(d, "model.p3d"), "ODOL"u8.ToArray());
        BuildAssets.HasBinarizedP3d(d).Should().BeTrue();
    }

    [Fact]
    public void HasBinarizedP3d_false_for_MLOD_source_p3d_or_no_p3d()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(d, "src.p3d"), "MLOD"u8.ToArray());
        File.WriteAllText(Path.Combine(d, "config.cpp"), "x");
        BuildAssets.HasBinarizedP3d(d).Should().BeFalse();
    }

    [Fact]
    public void HasBinarizedP3d_finds_a_nested_binarized_p3d()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        var sub = Path.Combine(d, "data", "models"); Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "zen_invisibleproxy.p3d"), "ODOL"u8.ToArray());
        BuildAssets.HasBinarizedP3d(d).Should().BeTrue();
    }

    [Fact]
    public void BinarizedP3ds_lists_only_the_ODOL_models()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(d, "bin.p3d"), "ODOL"u8.ToArray());
        File.WriteAllBytes(Path.Combine(d, "src.p3d"), "MLOD"u8.ToArray());
        BuildAssets.BinarizedP3ds(d).Select(Path.GetFileName).Should().Equal("bin.p3d");
    }

    [Fact]
    public void P3ds_lists_every_model_binarized_or_not_recursively()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        var sub = Path.Combine(d, "data"); Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(d, "bin.p3d"), "ODOL"u8.ToArray());
        File.WriteAllBytes(Path.Combine(sub, "src.p3d"), "MLOD"u8.ToArray());
        File.WriteAllText(Path.Combine(d, "config.cpp"), "x");
        BuildAssets.P3ds(d).Select(Path.GetFileName).Should().BeEquivalentTo("bin.p3d", "src.p3d");
    }

    [Fact]
    public void P3ds_minus_ODOL_is_what_decides_whether_binarize_runs()
    {
        // The engine binarizes only when an MLOD model exists; an ODOL-only (or model-less) mod skips it.
        var odolOnly = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(odolOnly, "m.p3d"), "ODOL"u8.ToArray());
        var odol = BuildAssets.BinarizedP3ds(odolOnly).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        BuildAssets.P3ds(odolOnly).Select(Path.GetFullPath).Any(p => !odol.Contains(p)).Should().BeFalse();

        var withMlod = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(withMlod, "m.p3d"), "MLOD"u8.ToArray());
        BuildAssets.P3ds(withMlod).Select(Path.GetFullPath)
            .Any(p => !BuildAssets.BinarizedP3ds(withMlod).Select(Path.GetFullPath).Contains(p)).Should().BeTrue();
    }
}
