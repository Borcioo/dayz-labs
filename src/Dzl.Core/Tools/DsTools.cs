using Dzl.Core.Procs;

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
            var r = ProcRunner.Run(exePath, new[] { name }, new RunOpts(WorkingDir: keysDir));
            // DSCreateKey can return non-zero even on success on some setups; trust the produced file.
            return new KeyResult(File.Exists(priv), priv, pub, r.AllOutput);
        }
        catch (Exception ex)
        {
            return new KeyResult(false, priv, pub, ex.Message);
        }
    }

    public static List<string> SignArgs(string privateKey, string pbo) => new() { privateKey, pbo };

    /// <summary>Sign a PBO: <c>DSSignFile &lt;key.biprivatekey&gt; &lt;file.pbo&gt;</c> writes
    /// <c>&lt;file.pbo&gt;.&lt;key&gt;.bisign</c> next to it. Run with the working dir at the PBO's folder so
    /// the signature lands there. Never throws.</summary>
    public static (bool ok, string output) Sign(string exePath, string privateKey, string pbo)
    {
        try
        {
            var r = ProcRunner.Run(exePath, SignArgs(privateKey, pbo),
                new RunOpts(TimeoutMs: 120_000, WorkingDir: Path.GetDirectoryName(pbo)));
            return (r.Ok, r.AllOutput);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
