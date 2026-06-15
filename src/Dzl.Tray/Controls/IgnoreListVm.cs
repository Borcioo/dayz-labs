using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="IgnoreListEditor"/> control (Economy "Ignore list" tab): edits the mission's
/// <c>cfgignorelist.xml</c> — a flat list of item classnames the Central Economy ignores. Add (validated
/// identifier) / remove / filter. Per-tab undo/redo + status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class IgnoreListVm : RawXmlEditorVm
{
    private readonly IgnoreListService _svc;

    public IgnoreListVm(string configPath, Func<string, bool> confirm)
        : this(new IgnoreListService(configPath), confirm) { }

    private IgnoreListVm(IgnoreListService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.IgnoreListPath,
               "(no cfgignorelist.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
    }

    private readonly List<string> _all = new();

    /// <summary>The filtered classnames shown in the list.</summary>
    public ObservableCollection<string> Items { get; } = new();

    [ObservableProperty] private string _filter = "";
    partial void OnFilterChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _newName = "";

    public bool HasItems => Items.Count > 0;

    protected override void ReloadView()
    {
        _all.Clear();
        _all.AddRange(_svc.Load());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Items.Clear();
        foreach (var n in _all)
            if (f.Length == 0 || n.Contains(f, StringComparison.OrdinalIgnoreCase)) Items.Add(n);
        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>Add the classname from the add box (validated bare identifier).</summary>
    public void AddName()
    {
        var name = (NewName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ classname must not be empty"; return; }
        if (!IsValidClassname(name)) { Status = "✗ classname must be a bare identifier (no spaces or < > & \" ')"; return; }
        if (_all.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ \"{name}\" is already ignored"; return; }
        PushUndo();
        if (Report(_svc.Add(name)))
        {
            NewName = "";
            ReloadView();
        }
    }

    public void RemoveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        PushUndo();
        if (Report(_svc.Remove(name))) ReloadView();
    }

    // A classname is matched literally by the engine and written as an XML attribute — reject whitespace and
    // XML-reserved characters (same guard as the dictionary identifiers).
    private static bool IsValidClassname(string name) =>
        !name.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '&' or '"' or '\'');
}
