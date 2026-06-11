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

    /// <summary>Arguments to open one file (optionally at a line) in the configured editor.
    /// VS Code-family editors (code/cursor/codium/insiders) take <c>--goto file:line</c>;
    /// everything else gets the plain file path (line ignored). Pure — unit-testable.</summary>
    public static List<string> FileArgs(string editorPath, string file, int line = 0)
    {
        var exe = Path.GetFileNameWithoutExtension(editorPath).ToLowerInvariant();
        var isVsCodeFamily = exe is "code" or "code-insiders" or "cursor" or "codium" or "windsurf";
        if (line > 0 && isVsCodeFamily) return new List<string> { "--goto", $"{file}:{line}" };
        return new List<string> { file };
    }

    /// <summary>Open a single file in the configured editor, jumping to <paramref name="line"/>
    /// when the editor supports it. Falls back to the OS default app when no editor is set.</summary>
    public static bool OpenFile(string editorPath, string file, int line = 0)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return false;
        try
        {
            if (string.IsNullOrWhiteSpace(editorPath))
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return true;
            }
            var psi = new ProcessStartInfo(editorPath) { UseShellExecute = true };
            foreach (var a in FileArgs(editorPath, file, line)) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
