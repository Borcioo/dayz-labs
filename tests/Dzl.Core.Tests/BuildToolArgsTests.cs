using Dzl.Core.Tools;
using FluentAssertions;

public class BuildToolArgsTests
{
    [Fact]
    public void Binarize_args_are_source_then_dest()
        => Binarize.Args(@"P:\Mod", @"D:\out").Should().Equal(@"P:\Mod", @"D:\out");

    [Fact]
    public void FileBank_args_set_prefix_and_dest_then_source()
        => FileBank.PackArgs(@"D:\stage", @"D:\out\Addons", "Mod\\Core")
            .Should().Equal("-property", "prefix=Mod\\Core", "-dst", @"D:\out\Addons", @"D:\stage");

    [Fact]
    public void DsSign_args_are_key_then_pbo()
        => DsTools.SignArgs(@"D:\keys\me.biprivatekey", @"D:\out\Addons\Core.pbo")
            .Should().Equal(@"D:\keys\me.biprivatekey", @"D:\out\Addons\Core.pbo");
}
