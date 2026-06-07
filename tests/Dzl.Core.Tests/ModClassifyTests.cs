using Dzl.Core.Config;
using Dzl.Core.Mods;
using FluentAssertions;
using Xunit;

public class ModClassifyTests
{
    private static readonly DzlConfig Cfg = new() { ProjectsRoot = @"D:\P", WorkshopDir = "" };

    [Theory]
    [InlineData(@"D:\P\mods\MyMod", ModKind.Source)]
    [InlineData(@"D:\P\build\@MyMod", ModKind.Build)]
    [InlineData(@"P:\Mods\@MyMod", ModKind.Build)]          // build link area
    [InlineData(@"P:\MyMod", ModKind.Source)]               // source work-drive link
    [InlineData(@"D:/P/workshop/123", ModKind.Downloaded)]  // forward slashes tolerated
    [InlineData(@"C:\somewhere\else", ModKind.External)]
    [InlineData("", ModKind.External)]
    public void Classify_buckets_by_path(string path, ModKind expected)
        => ModClassify.Classify(path, Cfg).Should().Be(expected);

    [Fact]
    public void Classify_honours_workshop_dir_override()
    {
        var cfg = new DzlConfig { ProjectsRoot = @"D:\P", WorkshopDir = @"E:\dl" };
        ModClassify.Classify(@"E:\dl\999", cfg).Should().Be(ModKind.Downloaded);
        ModClassify.Classify(@"D:\P\workshop\999", cfg).Should().Be(ModKind.External);   // override moved it
    }
}
