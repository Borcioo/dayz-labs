using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class ToolLauncher
{
    // Launch a tool GUI (fire-and-forget). Returns false if the exe is missing.
    public static bool Launch(ToolEntry tool)
    {
        if (!tool.Exists) return false;
        Process.Start(new ProcessStartInfo(tool.ExePath) { UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(tool.ExePath)! });
        return true;
    }
}
