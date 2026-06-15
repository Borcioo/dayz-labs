namespace Dzl.Tray.Help;

/// <summary>One row in a tab's field glossary: a term and its plain-English explanation.
/// Shown by the per-tab <see cref="Dzl.Tray.Controls.GlossaryButton"/> flyout. English on purpose —
/// DayZ Central-Economy field names are English, so the cheat-sheet matches the XML the modder edits.</summary>
public sealed record GlossaryEntry(string Term, string Definition);
