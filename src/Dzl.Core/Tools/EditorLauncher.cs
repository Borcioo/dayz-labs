using System.Diagnostics;

namespace Dzl.Core.Tools;

/// <summary>Opens a folder in the configured code editor (<c>&lt;editor&gt; &lt;folder&gt;</c>). Handles both
/// real exes (Cursor.exe/Code.exe) and PATH shims (.cmd/.bat) via ShellExecute. Never throws.</summary>
public static class EditorLauncher
{
    public static bool Open(string editorPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(editorPath) || string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var psi = new ProcessStartInfo(editorPath) { UseShellExecute = true };
            psi.ArgumentList.Add(folder);
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
