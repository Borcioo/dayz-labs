using System.Text;

namespace Dzl.Core.Build;

/// <summary>One file entry from a PBO header. <see cref="DataSize"/> is the packed byte count;
/// <see cref="Offset"/> is the absolute position of the entry's data block in the archive.</summary>
public sealed record PboEntry(string Name, uint PackingMethod, uint OriginalSize, uint DataSize, long Offset);

/// <summary>Parsed PBO identity: header properties (notably <c>prefix</c>) + the entry table.</summary>
public sealed record PboInfo(IReadOnlyDictionary<string, string> Properties, IReadOnlyList<PboEntry> Entries)
{
    public string Prefix => Properties.TryGetValue("prefix", out var p) ? p : "";
}

/// <summary>Minimal reader for the (public, long-documented) PBO container format — enough to verify
/// what a build actually produced: the packed prefix property and the entry list. Never throws on bad
/// input; returns null instead (verification treats unreadable as "couldn't verify", not crash).</summary>
public static class PboHeader
{
    private const uint VersMagic = 0x56657273;   // "Vers" — marks the leading properties entry

    public static PboInfo? Read(string pboPath)
    {
        try
        {
            using var fs = File.OpenRead(pboPath);
            using var br = new BinaryReader(fs, Encoding.ASCII);
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<PboEntry>();

            while (true)
            {
                var name = ReadCString(br);
                if (name is null) return null;
                uint packing = br.ReadUInt32();
                uint originalSize = br.ReadUInt32();
                br.ReadUInt32();   // reserved
                br.ReadUInt32();   // timestamp
                uint dataSize = br.ReadUInt32();

                if (name.Length == 0 && packing == VersMagic)
                {
                    // Properties: zero-terminated key/value pairs until an empty key.
                    while (true)
                    {
                        var key = ReadCString(br);
                        if (string.IsNullOrEmpty(key)) break;
                        var value = ReadCString(br) ?? "";
                        properties[key] = value;
                    }
                    continue;
                }

                if (name.Length == 0) break;   // terminator entry → data blocks follow
                entries.Add(new PboEntry(name, packing, originalSize, dataSize, 0));
            }

            // Data blocks sit immediately after the terminator, in entry order.
            long offset = fs.Position;
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i] = entries[i] with { Offset = offset };
                offset += entries[i].DataSize;
            }

            return new PboInfo(properties, entries);
        }
        catch { return null; }
    }

    /// <summary>Newest <c>&lt;pbo&gt;.*.bisign</c> next to the PBO, or null.</summary>
    public static string? FindSignature(string pboPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(pboPath))!;
            return Directory.EnumerateFiles(dir, Path.GetFileName(pboPath) + ".*.bisign")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? ReadCString(BinaryReader br, int max = 1024)
    {
        var sb = new StringBuilder();
        try
        {
            for (int i = 0; i < max; i++)
            {
                int b = br.BaseStream.ReadByte();
                if (b < 0) return null;
                if (b == 0) return sb.ToString();
                sb.Append((char)b);
            }
        }
        catch { }
        return null;
    }
}
