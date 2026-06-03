using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class ToolWrappersTests
{
    [Theory]
    [InlineData("rock_co.png", true)]
    [InlineData("rock_nohq.tga", true)]
    [InlineData("metal_smdi.png", true)]
    [InlineData("diffuse.png", false)]   // no DayZ suffix
    public void Paa_suffix_validation(string file, bool ok)
        => ImageToPaa.HasValidSuffix(file).Should().Be(ok);

    [Fact]
    public void Convert_args_are_input_then_output_paa()
    {
        var args = ImageToPaa.ConvertArgs(@"P:\t\rock_co.png");
        args.input.Should().Be(@"P:\t\rock_co.png");
        args.output.Should().Be(@"P:\t\rock_co.paa");
    }

    [Fact]
    public void Plan_folder_lists_png_and_tga_with_suffix_flags()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "rock_co.png"), "");
        File.WriteAllText(Path.Combine(dir, "bad.png"), "");
        File.WriteAllText(Path.Combine(dir, "note.txt"), "");
        var plan = ImageToPaa.PlanFolder(dir, recursive: false);
        plan.Should().HaveCount(2);                                   // only images
        plan.Should().Contain(i => i.Input.EndsWith("rock_co.png") && i.SuffixOk);
        plan.Should().Contain(i => i.Input.EndsWith("bad.png") && !i.SuffixOk);
    }

    [Fact]
    public void AddonBuilder_pack_args_assembled()
    {
        var args = AddonBuilder.PackArgs(@"P:\Mod\src", @"P:\out", clear: true, packOnly: true, prefix: "MyMod", signKey: null);
        args.Should().ContainInOrder(@"P:\Mod\src", @"P:\out", "-clear", "-packonly", "-prefix=MyMod");
        args.Should().NotContain(a => a.StartsWith("-sign"));
    }

    [Fact]
    public void AddonBuilder_pack_args_include_sign_when_key_given()
        => AddonBuilder.PackArgs(@"s", @"o", false, false, null, @"P:\keys\my.biprivatekey")
            .Should().Contain("-sign=P:\\keys\\my.biprivatekey");

    [Fact]
    public void CfgConvert_unbinarize_args()
        => CfgConvert.UnbinarizeArgs(@"P:\m\config.bin", @"P:\m\config.cpp")
            .Should().ContainInOrder("-txt", "-dst", @"P:\m\config.cpp", @"P:\m\config.bin");

    [Fact]
    public void WorkDrive_is_mounted_checks_directory()
    {
        // a path that exists stands in for a mounted drive
        var dir = Directory.CreateTempSubdirectory().FullName;
        WorkDrive.IsMounted(dir).Should().BeTrue();
        WorkDrive.IsMounted(@"X:\definitely\not\there").Should().BeFalse();
    }
}
