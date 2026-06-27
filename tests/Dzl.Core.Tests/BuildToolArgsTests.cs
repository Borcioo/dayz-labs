using Dzl.Core.Tools;
using FluentAssertions;

public class BuildToolArgsTests
{
    [Fact]
    public void Binarize_args_carry_project_context_with_source_and_dest_last()
    {
        var a = Binarize.Args(@"D:\stage", @"D:\out", @"P:\", new[] { @"P:\", @"P:\DZ" }, @"D:\tex", 8);
        a.Should().Equal(
            "-targetBonesInterval=56", "-maxProcesses=8", "-always", "-silent",
            @"-addon=P:\", @"-addon=P:\DZ", @"-textures=D:\tex", @"-binpath=P:\",
            @"D:\stage", @"D:\out");
    }

    [Fact]
    public void Binarize_args_normalize_a_bare_drive_addon_and_binpath_to_have_a_trailing_separator()
    {
        var a = Binarize.Args(@"D:\stage", @"D:\out", "P:", new[] { "P:" }, @"D:\tex", 1);
        a.Should().Contain(@"-addon=P:\").And.Contain(@"-binpath=P:\");
    }

    [Fact]
    public void FileBank_args_set_prefix_and_dest_then_source()
        => FileBank.PackArgs(@"D:\stage", @"D:\out\Addons", "Mod\\Core")
            .Should().Equal("-property", "prefix=Mod\\Core", "-dst", @"D:\out\Addons", @"D:\stage");

    [Fact]
    public void DsSign_args_are_key_then_pbo()
        => DsTools.SignArgs(@"D:\keys\me.biprivatekey", @"D:\out\Addons\Core.pbo")
            .Should().Equal(@"D:\keys\me.biprivatekey", @"D:\out\Addons\Core.pbo");
}
