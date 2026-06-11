namespace Dzl.Core.Build;

/// <summary>One diagnosed likely cause: what the matched symptom means and what to do about it.</summary>
public sealed record Diagnosis(string Title, string Cause, string Fix);

/// <summary>
/// Failure triage: pattern-match a build log (or a DayZ client/server log tail) against known
/// symptoms and emit cause→fix entries. Pure string analysis — call it with whatever log text
/// is at hand; it never throws and unknown text simply yields no diagnoses.
/// </summary>
public static class BuildDiagnostics
{
    private sealed record Pattern(string[] Needles, Diagnosis Diagnosis);

    private static readonly Pattern[] BuildPatterns =
    {
        new(new[] { "cannot include file", "preprocessor failed" }, new Diagnosis(
            "Config include could not be resolved",
            "A #include points at a file the tool can't find from its working path.",
            "Check the #include path in config.cpp and that the .hpp exists where the config expects it.")),

        new(new[] { "0xc0000005", "access violation" }, new Diagnosis(
            "Binarize crashed with an access violation",
            "The classic trigger is an already-binarized ODOL .p3d fed back into Binarize (or corrupt source data).",
            "Run 'dzl preflight' — it flags ODOL models. Keep them out of binarization or rebuild from MLOD source.")),

        new(new[] { "error 3 while parsing", "config : some input after end of file", "cfgconvert failed",
                "cfgconvert returned error", "error reading config file", "encountered instead of" }, new Diagnosis(
            "Config syntax error",
            "CfgConvert/Binarize could not parse a config — the error can come from an included .hpp, not just config.cpp.",
            "Fix the reported line; 'dzl preflight' runs the same parser up front when DayZ Tools is configured.")),

        new(new[] { "dssignfile failed", "no .bisign", "private key" }, new Diagnosis(
            "Signing failed",
            "DSSignFile didn't produce a signature — bad/missing .biprivatekey, write permissions, or antivirus locking the fresh PBO.",
            "Check the key path ('dzl key new' creates one), output-folder permissions, and AV exclusions.")),

        new(new[] { "imagetopaa failed", "unsupported texture", "texture conversion failed" }, new Diagnosis(
            "Texture conversion failed",
            "ImageToPAA rejected a source image (unsupported format/size — dimensions must be powers of two).",
            "Open the texture named above and re-export as a clean PNG/TGA with power-of-two dimensions.")),

        new(new[] { "cannot open p:", "work drive", "path not found" }, new Diagnosis(
            "Work drive path problem",
            "A tool tried to read through P:\\ and the path wasn't there — unmounted drive or a dangling junction.",
            "Run 'dzl status' / mount the work drive; dzl recreates its junctions on the next build.")),
    };

    /// <summary>DayZ client/server verification-kick codes (wiki: DayZ Error Codes) that map
    /// directly to mod-packaging causes — the bridge from "client can't connect" to "what's
    /// wrong with the build".</summary>
    private static readonly Pattern[] KickPatterns =
    {
        new(new[] { "ve_missing_bisign", "0x0004007e" }, new Diagnosis(
            "Client kicked: a PBO has no signature (VE_MISSING_BISIGN)",
            "The server verifies signatures and at least one shipped .pbo has no matching .bisign.",
            "Rebuild with signing on ('dzl build <Mod> --sign') and ship the Keys\\*.bikey with the server.")),

        new(new[] { "ve_patched_pbo", "0x0004007c" }, new Diagnosis(
            "Client kicked: patched PBO (VE_PATCHED_PBO)",
            "The client's PBO bytes differ from the signed original — usually an edited or half-updated mod.",
            "Rebuild and redistribute; make sure client and server run the same build of the mod.")),

        new(new[] { "ve_um_client_updated", "0x00040079", "ve_um_server_updated", "0x0004007a" }, new Diagnosis(
            "Client kicked: mod version skew",
            "Client and server run different versions of the same mod (one side updated, the other didn't).",
            "Update both sides to the same build — for local testing rebuild once and restart both via dzl.")),

        new(new[] { "ve_missing_mod", "0x00040073" }, new Diagnosis(
            "Client kicked: missing a server-side mod (VE_MISSING_MOD)",
            "The server loads a mod the client doesn't have.",
            "Add the mod to the client's -mod chain (dzl mods/side settings) or remove it server-side.")),

        new(new[] { "ve_extra_mod", "0x00040074", "ve_unexpected_mod_pbo" }, new Diagnosis(
            "Client kicked: extra client-side mod (VE_EXTRA_MOD)",
            "The client loads a mod the server doesn't run (and the server rejects extras).",
            "Match the client's mod list to the server's, or relax equalModRequired on the test server.")),

        new(new[] { "0x00020005", "filepatching" }, new Diagnosis(
            "Client kicked: filePatching mismatch (0x00020005)",
            "The client runs -filePatching but serverDZ.cfg lacks allowFilePatching = 1;.",
            "Add allowFilePatching = 1; to the server config (dzl's test-server scaffold bakes it in).")),
    };

    /// <summary>Diagnose a build/tool log. Multiple distinct symptoms yield multiple entries.</summary>
    public static List<Diagnosis> Diagnose(string logText) =>
        Match(logText, BuildPatterns);

    /// <summary>Diagnose a DayZ client/server log tail for verification kicks.</summary>
    public static List<Diagnosis> DiagnoseKick(string logText) =>
        Match(logText, KickPatterns);

    private static List<Diagnosis> Match(string logText, Pattern[] patterns)
    {
        var result = new List<Diagnosis>();
        if (string.IsNullOrEmpty(logText)) return result;
        var lower = logText.ToLowerInvariant();
        foreach (var p in patterns)
            if (p.Needles.Any(lower.Contains) && !result.Contains(p.Diagnosis))
                result.Add(p.Diagnosis);
        return result;
    }

    /// <summary>Tool-output digest: error/warning/missing-reference line counts so a UI can show
    /// "3 errors, 12 missing" instead of the whole wall.</summary>
    public static (int Errors, int Warnings, int Missing) Summarize(string logText)
    {
        int e = 0, w = 0, m = 0;
        foreach (var raw in (logText ?? "").Split('\n'))
        {
            var line = raw.ToLowerInvariant();
            if (line.Contains("error") || line.Contains("failed") || line.Contains("cannot ")) e++;
            if (line.Contains("warning")) w++;
            if (line.Contains("missing") || line.Contains("cannot open") || line.Contains("cannot load")) m++;
        }
        return (e, w, m);
    }

    /// <summary>Render diagnoses as a short, plain-text block for CLI/tray output.</summary>
    public static string Format(IReadOnlyList<Diagnosis> diagnoses)
    {
        if (diagnoses.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("— diagnostics —");
        for (int i = 0; i < diagnoses.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {diagnoses[i].Title}");
            sb.AppendLine($"   why: {diagnoses[i].Cause}");
            sb.AppendLine($"   fix: {diagnoses[i].Fix}");
        }
        return sb.ToString().TrimEnd();
    }
}
