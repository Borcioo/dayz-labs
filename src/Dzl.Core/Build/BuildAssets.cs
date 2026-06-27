namespace Dzl.Core.Build;

/// <summary>Source-asset probes the build pipeline needs to decide HOW to pack.</summary>
public static class BuildAssets
{
    /// <summary>Full paths of every <c>.p3d</c> model under <paramref name="dir"/> — binarized or not. The
    /// engine subtracts the ODOL set from this to decide whether there's anything to binarize at all (a
    /// model-less / ODOL-only mod skips Binarize entirely). Never throws.</summary>
    public static IReadOnlyList<string> P3ds(string dir)
    {
        var hits = new List<string>();
        if (!Directory.Exists(dir)) return hits;
        try { foreach (var f in Directory.EnumerateFiles(dir, "*.p3d", SearchOption.AllDirectories)) hits.Add(f); }
        catch { /* enumeration failed — treat as none */ }
        return hits;
    }

    /// <summary>True when any <c>.p3d</c> under <paramref name="dir"/> is already binarized (its first four
    /// bytes are <c>ODOL</c>). Feeding an ODOL p3d back through Binarize crashes AddonBuilder with an access
    /// violation (0xC0000005), so such a mod must be packed WITHOUT re-binarizing — the model already ships
    /// in its final form. Never throws.</summary>
    public static bool HasBinarizedP3d(string dir) => BinarizedP3ds(dir).Count > 0;

    /// <summary>Full paths of every already-binarized (ODOL) <c>.p3d</c> under <paramref name="dir"/> — the
    /// files the build engine must exclude from Binarize and copy as-is. Never throws.</summary>
    public static IReadOnlyList<string> BinarizedP3ds(string dir)
    {
        var hits = new List<string>();
        if (!Directory.Exists(dir)) return hits;
        Span<byte> magic = stackalloc byte[4];
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.p3d", SearchOption.AllDirectories))
            {
                try
                {
                    using var fs = File.OpenRead(f);
                    if (fs.Read(magic) == 4 && magic.SequenceEqual("ODOL"u8)) hits.Add(f);
                }
                catch { /* unreadable file — skip */ }
            }
        }
        catch { /* enumeration failed — treat as none */ }
        return hits;
    }
}
