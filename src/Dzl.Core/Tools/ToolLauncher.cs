using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class ToolLauncher
{
    /// <summary>Launch a tool GUI (fire-and-forget). Returns false if the exe is missing or the launch
    /// fails (access denied, no association); never throws.</summary>
    public static bool Launch(ToolEntry tool)
    {
        if (!tool.Exists) return false;
        try
        {
            Process.Start(new ProcessStartInfo(tool.ExePath) { UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(tool.ExePath) ?? "" });
            return true;
        }
        catch { return false; }
    }
}
