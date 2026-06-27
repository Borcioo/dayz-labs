using System.Security.Cryptography;
using System.Text;

namespace Dzl.Core.Build;

/// <summary>Writes a DayZ/Arma <c>.pbo</c> directly, in pure code — no FileBank/AddonBuilder. This mirrors the
/// approach of RaG-PBO-Builder (studied behaviourally): packing a folder is just a deterministic binary format,
/// so doing it ourselves removes a whole class of external-tool failures (the DayZ FileServer hanging, the PBO
/// being named after the source-folder leaf, prefix-property quirks) and makes packing unit-testable without
/// DayZ Tools installed.</summary>
/// <remarks>
/// Format (uncompressed/"stored"): a version/properties entry (packing method = <c>Vers</c> magic) carrying a
/// <c>prefix</c> property, then one header entry per file (method 0, original size == data size), a terminating
/// empty entry, the raw file bodies concatenated in the same order, then a 1-byte zero + the SHA-1 of everything
/// written before it. A synthetic <c>$PBOPREFIX$</c> file is injected so the unpacked mod keeps its prefix.
/// </remarks>
public static class PboWriter
{
    private const uint VersionMagic = 0x56657273;   // "Vers" — marks the leading version/properties entry

    private static readonly HashSet<string> ExcludeDirs =
        new(StringComparer.OrdinalIgnoreCase) { ".git", ".svn", ".vscode", ".idea", "__pycache__" };

    private static readonly HashSet<string> ExcludeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gitignore", ".gitattributes", "thumbs.db", "desktop.ini", ".ds_store",
        "$prefix$", "$pboprefix$", "$prefix$.txt", "$pboprefix$.txt",
    };

    private static readonly HashSet<string> ExcludeExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".delete" };

    private static readonly HashSet<string> PrefixFiles =
        new(StringComparer.OrdinalIgnoreCase) { "$prefix$", "$pboprefix$", "$prefix$.txt", "$pboprefix$.txt" };

    /// <summary>True when a file should NOT be packed. <c>config.cpp</c>/<c>config.bin</c> are always kept.</summary>
    public static bool ShouldSkipFile(string fileName)
    {
        if (fileName.Equals("config.cpp", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("config.bin", StringComparison.OrdinalIgnoreCase))
            return false;
        if (ExcludeFiles.Contains(fileName)) return true;
        return ExcludeExtensions.Contains(Path.GetExtension(fileName));
    }

    private sealed record Entry(string Name, string? Path, byte[]? Data, long Size);

    /// <summary>Pack <paramref name="sourceDir"/> into the PBO at <paramref name="outputPath"/> with the given
    /// <paramref name="prefix"/>. Writes to a <c>.tmp</c> sibling then atomically renames. Never throws — returns
    /// <c>(false, message)</c> on any failure.</summary>
    public static (bool ok, string output) Pack(string sourceDir, string outputPath, string prefix,
        Action<string>? onLine = null)
    {
        try
        {
            sourceDir = Path.GetFullPath(sourceDir);
            outputPath = Path.GetFullPath(outputPath);
            if (!Directory.Exists(sourceDir)) return (false, $"source is not a directory: {sourceDir}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var entries = new List<Entry>();
            foreach (var file in EnumeratePackableFiles(sourceDir))
            {
                var rel = Path.GetRelativePath(sourceDir, file).Replace('/', '\\');
                entries.Add(new Entry(rel, file, null, new FileInfo(file).Length));
            }

            var normalizedPrefix = prefix.Replace('/', '\\').Trim('\\');
            if (normalizedPrefix.Length > 0)
            {
                entries.RemoveAll(e => PrefixFiles.Contains(e.Name));
                var data = Encoding.ASCII.GetBytes(normalizedPrefix + "\r\n");
                entries.Add(new Entry("$PBOPREFIX$", null, data, data.Length));
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var header = BuildHeader(entries, normalizedPrefix);

            var tmp = outputPath + ".tmp";
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            using (var outFile = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                outFile.Write(header, 0, header.Length);
                sha.AppendData(header);
                var buffer = new byte[1024 * 1024];
                foreach (var e in entries)
                {
                    if (e.Data is not null)
                    {
                        outFile.Write(e.Data, 0, e.Data.Length);
                        sha.AppendData(e.Data);
                        continue;
                    }
                    using var inFile = File.OpenRead(e.Path!);
                    int read;
                    while ((read = inFile.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outFile.Write(buffer, 0, read);
                        sha.AppendData(buffer, 0, read);
                    }
                }
                outFile.WriteByte(0);
                outFile.Write(sha.GetHashAndReset());
            }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tmp, outputPath);
            onLine?.Invoke($"packed {entries.Count} file(s) -> {Path.GetFileName(outputPath)}");
            return (true, $"packed {entries.Count} file(s)");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static IEnumerable<string> EnumeratePackableFiles(string sourceDir)
    {
        var stack = new Stack<string>();
        stack.Push(sourceDir);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!ExcludeDirs.Contains(Path.GetFileName(sub)))
                    stack.Push(sub);
            foreach (var file in Directory.EnumerateFiles(dir))
                if (!ShouldSkipFile(Path.GetFileName(file)))
                    yield return file;
        }
    }

    private static byte[] BuildHeader(List<Entry> entries, string prefix)
    {
        using var ms = new MemoryStream();
        void U32(uint v) => ms.Write(BitConverter.GetBytes(v));   // little-endian on Windows
        void Str(string s) { ms.Write(Encoding.ASCII.GetBytes(s)); ms.WriteByte(0); }

        // Version/properties entry: empty name, packing method = Vers magic, four zero fields, then key\0value\0 pairs.
        ms.WriteByte(0);
        U32(VersionMagic);
        U32(0); U32(0); U32(0); U32(0);
        if (prefix.Length > 0) { Str("prefix"); Str(prefix); }
        ms.WriteByte(0);   // end of properties

        // One header entry per file: name, method 0 (stored), original size, reserved, timestamp, data size.
        foreach (var e in entries)
        {
            Str(e.Name);
            U32(0); U32((uint)e.Size); U32(0); U32(0); U32((uint)e.Size);
        }
        // Terminating empty entry.
        ms.WriteByte(0);
        U32(0); U32(0); U32(0); U32(0); U32(0);
        return ms.ToArray();
    }

    /// <summary>Read the <c>prefix</c> property back out of a packed PBO header (for verification). "" if absent.</summary>
    public static string ReadPrefix(string pboPath)
    {
        try
        {
            using var fs = File.OpenRead(pboPath);
            var buf = new byte[Math.Min(65536, fs.Length)];
            var n = fs.Read(buf, 0, buf.Length);
            var marker = Encoding.ASCII.GetBytes("prefix\0");
            for (var i = 0; i + marker.Length < n; i++)
            {
                var hit = true;
                for (var j = 0; j < marker.Length; j++) if (buf[i + j] != marker[j]) { hit = false; break; }
                if (!hit) continue;
                var start = i + marker.Length;
                var end = Array.IndexOf(buf, (byte)0, start);
                if (end < 0) return "";
                return Encoding.ASCII.GetString(buf, start, end - start);
            }
            return "";
        }
        catch { return ""; }
    }
}
