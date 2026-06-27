using System.IO;

namespace Dzl.Tray;

/// <summary>Resolves how to launch the bundled Dzl.Mcp stdio server, for the "claude mcp add" command shown
/// on the MCP page. The installer ships the MCP isolated in an <c>mcp\</c> subfolder (its .NET 10 deps would
/// poison the net8 Tray if merged), with a self-contained <c>dzl-mcp.exe</c> apphost — so prefer that over a
/// <c>dotnet &lt;dll&gt;</c> invocation, and never point at the Tray root. Pure; the filesystem is injected
/// via <paramref name="exists"/> so it's unit-testable.</summary>
public static class McpLauncher
{
    public static string Resolve(string baseDir, Func<string, bool> exists)
    {
        // Self-contained exe first (no dotnet runtime dependency). Installed: mcp\dzl-mcp.exe.
        foreach (var exe in new[]
                 {
                     Path.Combine(baseDir, "mcp", "dzl-mcp.exe"),
                     Path.Combine(baseDir, "mcp", "Dzl.Mcp.exe"),
                     Path.Combine(baseDir, "Dzl.Mcp.exe"),
                 })
            if (exists(exe)) return Quote(exe);

        foreach (var dll in new[]
                 {
                     Path.Combine(baseDir, "mcp", "Dzl.Mcp.dll"),
                     Path.Combine(baseDir, "Dzl.Mcp.dll"),
                 })
            if (exists(dll)) return $"dotnet {Quote(dll)}";

        // Best-effort placeholder pointing at the installed layout (must be built/published first).
        return $"dotnet {Quote(Path.Combine(baseDir, "mcp", "Dzl.Mcp.dll"))}";
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
