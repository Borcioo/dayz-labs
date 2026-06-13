using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Economy;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Controls;

/// <summary>Code-behind for the Central Economy editor panel: grid selection plumbing, batch-bar
/// handlers, backups menu and per-tab refresh. All state lives on <see cref="MainViewModel"/>
/// (the inherited DataContext), so closing/reopening the host window keeps edits and undo.</summary>
public partial class EconomyPanel : UserControl
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public EconomyPanel()
    {
        InitializeComponent();
        // Same refresh the main window ran when the Economy page was shown.
        Loaded += (_, _) =>
        {
            if (DataContext is not MainViewModel) return;
            Vm.TypesEditor.LoadTypes();
            Vm.RefreshDictionaries();
            Vm.RefreshCeDashboard();   // Dashboard is the default tab
            RefreshTypesBackupsMenu();
            WireDashboardNavigation();
        };
    }

    // A dashboard tile/finding click asks to jump to that file's editor tab.
    private bool _navWired;
    private void WireDashboardNavigation()
    {
        if (_navWired) return;
        Vm.CeDashboard.NavigateRequested += SelectTabForKind;
        _navWired = true;
    }

    private void SelectTabForKind(CeKind kind, string entry)
    {
        var header = kind switch
        {
            CeKind.Dictionaries => "Dictionaries",
            CeKind.RandomPresets => "Random Presets",
            CeKind.SpawnableTypes => "Spawnable Types",
            CeKind.Globals => "Globals",
            CeKind.Events => "Events",
            CeKind.PlayerSpawns => "Player Spawns",
            _ => "Types",
        };
        foreach (var item in EconomyTabControl.Items)
            if (item is TabItem { Header: string h } ti && h == header) { EconomyTabControl.SelectedItem = ti; break; }

        // A finding click also filters the target editor's list to the offending entry.
        if (entry.Length == 0) return;
        switch (kind)
        {
            case CeKind.Types: Vm.TypesEditor.TypesFilter = entry; break;
            case CeKind.Events: Vm.Events.Filter = entry; break;
            case CeKind.Globals: Vm.Globals.Filter = entry; break;
            case CeKind.SpawnableTypes: Vm.SpawnableTypes.Filter = entry; break;
            case CeKind.RandomPresets: Vm.RandomPresets.Filter = entry; break;
        }
    }

    // Batch + remove operate on the CHECKED rows (checkbox column), NOT the grid's focused/edited row.
    // The detail form edits the single focused row (grid SelectedItem) — selection-for-edit and
    // selection-for-batch are separate concepts now.
    private System.Collections.Generic.List<TypeRowVm> CheckedTypes() =>
        Vm.TypesEditor.CheckedTypes.ToList();

    // Reload guards unsaved edits: Yes = save then reload (SaveTypes reloads on success),
    // No = discard and reload, Cancel = keep working.
    private void OnReloadTypes(object sender, RoutedEventArgs e)
    {
        if (Vm.TypesEditor.HasUnsavedChanges)
        {
            var r = System.Windows.MessageBox.Show(
                "You have unsaved changes.\n\nYes — save, then reload\nNo — discard the changes and reload\nCancel — stay as you are",
                "Reload types", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);
            if (r == System.Windows.MessageBoxResult.Cancel) return;
            if (r == System.Windows.MessageBoxResult.Yes)
            {
                Vm.TypesEditor.SaveTypes();   // reloads from disk on success; keeps edits + status on failure
                RefreshTypesBackupsMenu();
                return;
            }
        }
        Vm.TypesEditor.LoadTypes();
        RefreshTypesBackupsMenu();
    }
    private void OnSaveTypes(object sender, RoutedEventArgs e) { Vm.TypesEditor.SaveTypes(); RefreshTypesBackupsMenu(); }

    /// <summary>Reload the Dictionaries data when the user switches to the Dictionaries sub-tab of the
    /// Economy tab shell, so stale limits (e.g. after types/limits edits) are refreshed immediately.
    /// Guarded against child SelectionChanged bubbling by checking e.OriginalSource is this TabControl.</summary>
    private void OnEconomyTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only react to selection changes on the exact Economy TabControl, not bubbled events from
        // child controls (DataGrid, ComboBox, etc.) whose SelectionChanged also bubbles up.
        if (!ReferenceEquals(e.OriginalSource, EconomyTabControl)) return;
        if (EconomyTabControl.SelectedItem is TabItem { Header: "Dashboard" })
            Vm.RefreshCeDashboard();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Dictionaries" })
            Vm.RefreshDictionaries();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Random Presets" })
            Vm.RefreshRandomPresets();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Spawnable Types" })
            Vm.RefreshSpawnableTypes();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Globals" })
            Vm.RefreshGlobals();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Events" })
            Vm.RefreshEvents();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Player Spawns" })
            Vm.RefreshPlayerSpawns();
    }

    private void OnAddType(object sender, RoutedEventArgs e)
    {
        var result = NewTypeDialog.Show(System.Windows.Window.GetWindow(this)!, Vm.TypesEditor.TypesSourceFiles());
        if (result is { } r) Vm.TypesEditor.AddType(r.name, r.targetFile);
    }

    private void OnDuplicateType(object sender, RoutedEventArgs e)
    {
        if (Vm.TypesEditor.SelectedType is not { } src)
        { System.Windows.MessageBox.Show("Select a row to duplicate first.", "Duplicate", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        var result = NewTypeDialog.Show(System.Windows.Window.GetWindow(this)!, Vm.TypesEditor.TypesSourceFiles(),
            title: "Duplicate type", defaultName: src.Name + "_Copy", okLabel: "Duplicate");
        if (result is { } r) Vm.TypesEditor.DuplicateType(src, r.name, r.targetFile);
    }

    // Remove acts on the checked rows; falls back to the single focused row when nothing is checked.
    private void OnRemoveTypes(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (rows.Count == 0 && TypesGrid.SelectedItem is TypeRowVm sel) rows = new() { sel };
        Vm.TypesEditor.RemoveTypes(rows);
    }

    private void OnClearFilters(object sender, RoutedEventArgs e) => Vm.TypesEditor.ClearTypeFilters();

    // Push the grid's focused row into the VM (drives the detail form). Batch selection is the checkbox set.
    private void OnTypesSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => Vm.TypesEditor.SetSelectedTypes(
            TypesGrid.SelectedItem is TypeRowVm row ? new[] { row } : System.Array.Empty<TypeRowVm>(),
            TypesGrid.SelectedItem as TypeRowVm);

    // Jump to (and select) the first row with lint findings.
    private void OnJumpToLint(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = Vm.TypesEditor.TypesView.Cast<TypeRowVm>().FirstOrDefault(r => r.HasLint);
        if (hit is null) return;
        TypesGrid.SelectedItem = hit;
        TypesGrid.ScrollIntoView(hit);
    }

    // Batch duplicate: copy every checked row under a collision-safe _Copy name.
    private void OnBatchDuplicate(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        Vm.TypesEditor.DuplicateTypes(rows);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchSet(object sender, RoutedEventArgs e) => Batch(multiply: false);
    private void OnBatchMultiply(object sender, RoutedEventArgs e) => Batch(multiply: true);

    private void OnBatchDivide(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var field = (BatchFieldBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "nominal";
        if (!double.TryParse(BatchValueBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        { System.Windows.MessageBox.Show("Enter a numeric value.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        if (val == 0)
        { System.Windows.MessageBox.Show("Cannot divide by zero.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); return; }
        Vm.TypesEditor.BatchDivide(rows, field, val);
        TypesGrid.Items.Refresh();
    }

    private void Batch(bool multiply)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var field = (BatchFieldBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "nominal";
        if (!double.TryParse(BatchValueBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        { System.Windows.MessageBox.Show("Enter a numeric value.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        Vm.TypesEditor.BatchApply(rows, field, val, multiply);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchFlagSet(object sender, RoutedEventArgs e) => BatchFlag("set");
    private void OnBatchFlagClear(object sender, RoutedEventArgs e) => BatchFlag("clear");
    private void OnBatchFlagToggle(object sender, RoutedEventArgs e) => BatchFlag("toggle");

    private void BatchFlag(string op)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var flag = (BatchFlagBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "map";
        Vm.TypesEditor.BatchFlag(rows, flag, op);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchListAdd(object sender, RoutedEventArgs e) => BatchList(sender, add: true);
    private void OnBatchListRemove(object sender, RoutedEventArgs e) => BatchList(sender, add: false);

    // One section per CE list (usage / value / tag) — the button's Tag carries the list key and
    // each section has its own suggestion combo.
    private void BatchList(object sender, bool add)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var list = (sender as FrameworkElement)?.Tag?.ToString() ?? "usage";
        var combo = list switch { "value" => BatchTierBox, "tag" => BatchTagBox, _ => BatchUsageBox };
        var val = combo.Text?.Trim() ?? "";
        if (val.Length == 0) { System.Windows.MessageBox.Show("Enter or pick a value first.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        Vm.TypesEditor.BatchList(rows, list, val, add);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchCategorySet(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var cat = BatchCategoryBox.Text?.Trim() ?? "";
        Vm.TypesEditor.BatchCategory(rows, cat);
        TypesGrid.Items.Refresh();
    }

    private bool RequireSelection(System.Collections.Generic.IReadOnlyList<TypeRowVm> rows)
    {
        if (rows.Count > 0) return true;
        System.Windows.MessageBox.Show("Check one or more rows first (the checkbox column).", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        return false;
    }

    // Undo granularity for in-grid cell edits: snapshot the pre-edit state, commit it on edit-commit.
    private void OnTypesBeginEdit(object sender, DataGridBeginningEditEventArgs e) => Vm.TypesEditor.BeginTypeEdit();
    private void OnTypesCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit) Vm.TypesEditor.CommitTypeEdit();
        else Vm.TypesEditor.CancelTypeEdit();
    }

    private void RefreshTypesBackupsMenu()
    {
        TypesBackupsMenu.Items.Clear();
        var backups = Vm.TypesEditor.TypesBackups();
        if (backups.Count == 0)
        {
            TypesBackupsMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(no backups yet)", IsEnabled = false });
            return;
        }
        foreach (var b in backups)
        {
            var item = new System.Windows.Controls.MenuItem { Header = b.Stamp, Tag = b.Path };
            item.Click += OnRestoreTypeBackup;
            TypesBackupsMenu.Items.Add(item);
        }
    }

    private void OnRestoreTypeBackup(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path }) return;
        var ok = System.Windows.MessageBox.Show(
            $"Restore types.xml from backup {System.IO.Path.GetFileName(path)}?\n\nThe current file is snapshotted first (undoable).",
            "Restore backup", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.OK;
        if (!ok) return;
        Vm.TypesEditor.RestoreTypes(path);
        RefreshTypesBackupsMenu();
    }
}
