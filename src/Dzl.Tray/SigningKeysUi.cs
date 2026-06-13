using System.IO;
using System.Windows;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>
/// Shared interactive flows for signing-key management, used by both the global Settings page
/// and the Mods module-settings modal so the guards can't drift apart. Both flows persist the
/// chosen name + keys folder to config and return the new key name + a status line (null name =
/// cancelled / refused).
/// </summary>
internal static class SigningKeysUi
{
    /// <summary>Prompt for a NEW key name and run DSCreateKey. Refuses names that already exist —
    /// a lost .biprivatekey orphans every mod signed with it (Core never overwrites either; this
    /// is the explicit UX guard).</summary>
    public static (string? name, string status) GenerateInteractive(Window owner, MainViewModel vm, string keysDirText)
    {
        var name = PromptDialog.Show(owner, "Generate signing key",
            "Name for the NEW key pair (letters/digits/underscore, e.g. your author handle):", "");
        if (string.IsNullOrWhiteSpace(name)) return (null, "");
        name = name.Trim();

        if (vm.ListSigningKeys().Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"Key '{name}' already exists in the keys folder and was NOT touched.\n\n" +
                "Existing keys are never overwritten — losing a private key would orphan every mod signed with it.\n" +
                "Pick the key from the dropdown to use it, or choose a different name to generate a new one.",
                "Generate signing key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return (null, "");
        }

        vm.ApplyConfig(vm.Cfg with { SigningKey = name, KeysDir = keysDirText.Trim() });
        return (name, vm.GenerateSigningKey());
    }

    /// <summary>Bring your own keys: copy an existing .biprivatekey (+ its sibling .bikey when
    /// present) into the keys folder and adopt the file name as the signing-key name.</summary>
    public static (string? name, string status) ImportInteractive(Window owner, MainViewModel vm, string keysDirText)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import signing key",
            Filter = "DayZ private key (*.biprivatekey)|*.biprivatekey",
        };
        if (dlg.ShowDialog(owner) != true) return (null, "");

        try
        {
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            var keysDir = vm.ResolvedKeysDir;
            Directory.CreateDirectory(keysDir);

            var destPriv = Path.Combine(keysDir, name + ".biprivatekey");
            if (File.Exists(destPriv) &&
                MessageBox.Show(
                    $"Key '{name}' already exists in {keysDir} — overwrite?", "Import signing key",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return (null, "");
            File.Copy(dlg.FileName, destPriv, overwrite: true);

            // The public half usually sits next to the private one; copy it too when present.
            var srcPub = Path.ChangeExtension(dlg.FileName, ".bikey");
            if (File.Exists(srcPub))
                File.Copy(srcPub, Path.Combine(keysDir, name + ".bikey"), overwrite: true);

            vm.ApplyConfig(vm.Cfg with { SigningKey = name, KeysDir = keysDirText.Trim() });
            var status = $"✓ imported: {destPriv}";
            if (!File.Exists(srcPub))
                status += "\nImported the private key only — no .bikey found next to it (servers can't whitelist).";
            return (name, status);
        }
        catch (Exception ex)
        {
            return (null, "✗ import failed: " + ex.Message);
        }
    }

    /// <summary>Status line for the current key choice: ✓ ready (with a missing-.bikey caveat)
    /// or not-created-yet.</summary>
    public static string Status(MainViewModel vm, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "No key name — type one or set your author handle.";
        var priv = Path.Combine(vm.ResolvedKeysDir, name + ".biprivatekey");
        var pub = Path.Combine(vm.ResolvedKeysDir, name + ".bikey");
        return File.Exists(priv)
            ? $"✓ key ready: {priv}" + (File.Exists(pub) ? "" : "  (⚠ matching .bikey missing — servers can't whitelist)")
            : $"Key '{name}' not created yet — generate it, or import existing key files.";
    }
}
