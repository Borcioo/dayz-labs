namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    /// <summary>Backs the Economy "Types" tab (types.xml editor): grid, filters, batch ops, undo/redo,
    /// lint, dictionary suggestions. Extracted sub-VM; XAML binds via <c>TypesEditor.*</c>.</summary>
    public TypesEditorVm TypesEditor { get; }

    // --- CE Dictionaries manager ------------------------------------------
    // The Dictionaries tab edits cfglimitsdefinition.xml (the four base lists) + cfglimitsdefinitionuser.xml
    // (named combos). It shares the SAME limit names the Types editor suggests + lints against, so after any
    // dictionary edit we re-pull those (RefreshLimitsFromDisk) and re-lint — removing a value flags types that
    // used it; adding one clears false "unknown" warnings.

    private Dzl.Tray.Controls.DictionaryManagerVm? _dictionaries;

    /// <summary>Backs the Dictionaries tab. Created lazily on first access so it shares this VM's config path
    /// and can call back into the Types editor (suggestions + lint) after every dictionary edit.</summary>
    public Dzl.Tray.Controls.DictionaryManagerVm Dictionaries =>
        _dictionaries ??= new Dzl.Tray.Controls.DictionaryManagerVm(
            _configPath,
            onDictionaryChanged: TypesEditor.RefreshLimitsFromDisk,
            usageCount: TypesEditor.CountTypesUsing,
            confirm: Confirm("Dictionaries"));

    /// <summary>(Re)load the Dictionaries tab from disk. Called when the Economy page is shown.</summary>
    public void RefreshDictionaries() => Dictionaries.Reload();

    // --- CE Random Presets tab (cfgrandompresets.xml) -----------------------
    private Dzl.Tray.Controls.RandomPresetsVm? _randomPresets;

    /// <summary>Backs the Random Presets tab (cargo/attachments presets + items). Created lazily so it
    /// shares this VM's config path.</summary>
    public Dzl.Tray.Controls.RandomPresetsVm RandomPresets =>
        _randomPresets ??= new Dzl.Tray.Controls.RandomPresetsVm(_configPath, Confirm("Random Presets"));

    /// <summary>On Random Presets tab activation: load the model once (keeping undo), and refresh the
    /// cross-file reference data every time so edits made on other tabs (Spawnable Types / Types) show up.</summary>
    public void RefreshRandomPresets()
    {
        RandomPresets.EnsureLoaded();
        RandomPresets.RefreshReferences();
    }

    // --- CE Events tab (db/events.xml) -------------------------------------
    private Dzl.Tray.Controls.EventsVm? _events;

    /// <summary>Backs the Events tab (CE spawn events + children). Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.EventsVm Events =>
        _events ??= new Dzl.Tray.Controls.EventsVm(_configPath, Confirm("Events"));

    /// <summary>(Re)load the Events tab from disk. Called when its tab is activated.</summary>
    public void RefreshEvents() => Events.EnsureLoaded();

    // --- CE Globals tab (db/globals.xml) ------------------------------------
    private Dzl.Tray.Controls.GlobalsVm? _globals;

    /// <summary>Backs the Globals tab (simulation vars). Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.GlobalsVm Globals =>
        _globals ??= new Dzl.Tray.Controls.GlobalsVm(_configPath, Confirm("Globals"));

    /// <summary>(Re)load the Globals tab from disk. Called when its tab is activated.</summary>
    public void RefreshGlobals() => Globals.EnsureLoaded();

    // --- CE Spawnable Types tab (cfgspawnabletypes.xml) ---------------------
    private Dzl.Tray.Controls.SpawnableTypesVm? _spawnableTypes;

    /// <summary>Backs the Spawnable Types tab (per-type hoarder/damage + cargo/attachments blocks). Created
    /// lazily so it shares this VM's config path; its preset dropdowns read cfgrandompresets.xml.</summary>
    public Dzl.Tray.Controls.SpawnableTypesVm SpawnableTypes =>
        _spawnableTypes ??= new Dzl.Tray.Controls.SpawnableTypesVm(_configPath, Confirm("Spawnable Types"));

    /// <summary>On Spawnable Types tab activation: load the model once (keeping undo), and refresh the
    /// preset-name / classname pools every time so renames made on the Random Presets / Types tabs show up.</summary>
    public void RefreshSpawnableTypes()
    {
        SpawnableTypes.EnsureLoaded();
        SpawnableTypes.RefreshReferences();
    }

    // --- CE Player Spawns tab (cfgplayerspawnpoints.xml) --------------------
    private Dzl.Tray.Controls.PlayerSpawnsVm? _playerSpawns;

    /// <summary>Backs the Player Spawns tab (fresh/hop/travel categories, their param bags + position groups).
    /// Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.PlayerSpawnsVm PlayerSpawns =>
        _playerSpawns ??= new Dzl.Tray.Controls.PlayerSpawnsVm(_configPath, Confirm("Player Spawns"));

    /// <summary>(Re)load the Player Spawns tab from disk. Called when its tab is activated.</summary>
    public void RefreshPlayerSpawns() => PlayerSpawns.EnsureLoaded();

    // --- CE Dashboard tab (overview: stats + aggregated validation) --------
    private Dzl.Tray.Controls.CeDashboardVm? _ceDashboard;

    /// <summary>Backs the Economy Dashboard tab (first): per-file stat tiles + the aggregated
    /// validation report. Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.CeDashboardVm CeDashboard =>
        _ceDashboard ??= new Dzl.Tray.Controls.CeDashboardVm(_configPath, Confirm("Economy"));

    /// <summary>(Re)load the dashboard stats from disk and auto-run the full validation if the CE files
    /// changed since the last run. Called when the dashboard tab is shown; fired-and-forgotten — the cheap
    /// tile refresh is synchronous inside, the heavy cross-file pass runs off-thread.</summary>
    public void RefreshCeDashboard() => _ = CeDashboard.RefreshAndValidateAsync();

    /// <summary>A yes/no confirmation prompt titled for the tab that raises it, so the dialog header matches
    /// where the user actually is — each CE editor passes its own title rather than a shared generic one.</summary>
    private static Func<string, bool> Confirm(string title) => message =>
        System.Windows.MessageBox.Show(message, title,
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
        == System.Windows.MessageBoxResult.Yes;
}
