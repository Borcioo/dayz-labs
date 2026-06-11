using System.Diagnostics;

namespace Dzl.Core.Tools;

/// <summary>Opens a folder in the configured code editor (<c>&lt;editor&gt; &lt;folder&gt;</c>). Handles both
/// real exes (Cursor.exe/Code.exe) and PATH shims (.cmd/.bat) via ShellExecute. Never throws.</summary>
public static class EditorLauncher
{
    public static bool Open(string editorPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(editorPath) || string.IsNullOrWhiteSpace(folder)) return false;
        return Start(editorPath, new List<string> { folder });
    }

    /// <summary>Launch the editor with args. CLI shims (.cmd/.bat — what PATH detection finds for
    /// Cursor/VS Code) go through <c>cmd /c</c> with a hidden window; ShellExecute on a shim pops
    /// a console window that stays open. Real exes keep plain ShellExecute.</summary>
    private static bool Start(string editorPath, List<string> args)
    {
        try
        {
            var ext = Path.GetExtension(editorPath).ToLowerInvariant();
            ProcessStartInfo psi;
            if (ext is ".cmd" or ".bat")
            {
                psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(editorPath);
            }
            else
            {
                psi = new ProcessStartInfo(editorPath) { UseShellExecute = true };
            }
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Arguments to open a file (optionally at a line, optionally with its project
    /// folder as the workspace). VS Code-family editors (code/cursor/codium/insiders/windsurf)
    /// take <c>[folder] --goto file:line</c> — the folder opens as the workspace and the file
    /// lands focused at the line. Everything else gets the plain file path (folder + line
    /// ignored; most plain editors would try to open the folder as a file). Pure — unit-testable.</summary>
    public static List<string> FileArgs(string editorPath, string file, int line = 0, string? folder = null)
    {
        var exe = Path.GetFileNameWithoutExtension(editorPath).ToLowerInvariant();
        var isVsCodeFamily = exe is "code" or "code-insiders" or "cursor" or "codium" or "windsurf";
        if (!isVsCodeFamily) return new List<string> { file };

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(folder)) args.Add(folder);
        if (line > 0) { args.Add("--goto"); args.Add($"{file}:{line}"); }
        else args.Add(file);
        return args;
    }

    /// <summary>Open a file in the configured editor — with its project folder as the workspace
    /// and the cursor at <paramref name="line"/> when the editor supports it. Falls back to the
    /// OS default app when no editor is set.</summary>
    public static bool OpenFile(string editorPath, string file, int line = 0, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return false;
        try
        {
            if (string.IsNullOrWhiteSpace(editorPath))
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return true;
            }
            return Start(editorPath, FileArgs(editorPath, file, line, folder));
        }
        catch { return false; }
    }
}
