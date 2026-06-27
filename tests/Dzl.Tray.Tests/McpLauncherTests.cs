using System.IO;
using Dzl.Tray;
using FluentAssertions;

public class McpLauncherTests
{
    private static Func<string, bool> Exists(params string[] files)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void Installed_layout_prefers_the_self_contained_exe_in_the_mcp_subfolder()
    {
        var baseDir = @"C:\Users\me\AppData\Local\DayZLabs\current";
        var exe = Path.Combine(baseDir, "mcp", "dzl-mcp.exe");

        McpLauncher.Resolve(baseDir, Exists(exe, Path.Combine(baseDir, "mcp", "Dzl.Mcp.dll")))
            .Should().Be(exe);   // run the exe directly, no "dotnet", correct mcp\ path
    }

    [Fact]
    public void Falls_back_to_dotnet_dll_in_mcp_subfolder_when_no_exe()
    {
        var baseDir = @"C:\app";
        var dll = Path.Combine(baseDir, "mcp", "Dzl.Mcp.dll");

        McpLauncher.Resolve(baseDir, Exists(dll)).Should().Be($"dotnet {dll}");
    }

    [Fact]
    public void Sibling_dll_path_with_a_space_is_quoted()
    {
        var baseDir = @"C:\Program Files\app";
        var dll = Path.Combine(baseDir, "Dzl.Mcp.dll");

        McpLauncher.Resolve(baseDir, Exists(dll)).Should().Be($"dotnet \"{dll}\"");
    }

    [Fact]
    public void Nothing_found_still_points_at_the_mcp_subfolder_dll_not_the_root()
    {
        var baseDir = @"C:\app";
        McpLauncher.Resolve(baseDir, Exists())
            .Should().Be($"dotnet {Path.Combine(baseDir, "mcp", "Dzl.Mcp.dll")}");
    }
}
