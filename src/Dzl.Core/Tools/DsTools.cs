using System.Diagnostics;

namespace Dzl.Core.Tools;

public sealed record KeyResult(bool Ok, string PrivateKey, string PublicKey, string Output);

/// <summary>Thin, never-throwing wrappers around DayZ Tools' DsUtils signing binaries. Key creation +
/// signing are CLI-wrappable; the GUI Publisher is launch-only (see <see cref="ToolLauncher"/>).</summary>
public static class DsTools
{
    /// <summary>Create a signing key pair: <c>DSCreateKey &lt;name&gt;</c> writes <c>&lt;name&gt;.biprivatekey</c>
    /// + <c>&lt;name&gt;.bikey</c> into its working directory. We run it with the working dir set to
    /// <paramref name="keysDir"/> so the pair lands there. Never throws.</summary>
    public static KeyResult CreateKey(string exePath, string keysDir, string name)
    {
        var priv = Path.Combine(keysDir, name + ".biprivatekey");
        var pub = Path.Combine(keysDir, name + ".bikey");
        try
        {
            Directory.CreateDirectory(keysDir);
            var psi = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = keysDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(name);
            using var p = Process.Start(psi)!;
            var outp = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            p.WaitForExit();
            // DSCreateKey can return non-zero even on success on some setups; trust the produced file.
            return new KeyResult(File.Exists(priv), priv, pub, outp);
        }
        catch (Exception ex)
        {
            return new KeyResult(false, priv, pub, ex.Message);
        }
    }
}
