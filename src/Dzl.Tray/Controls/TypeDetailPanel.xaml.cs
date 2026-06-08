using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Controls;

/// <summary>The master–detail editor surface for one <see cref="TypeRowVm"/> (its DataContext = the selected
/// row, set by the host through a binding). Numeric +/- steppers, field commits, flag toggles and chip edits
/// route through the owning <see cref="MainViewModel"/> (the <see cref="Vm"/> DP) so each change is one undo
/// step and re-lints live. The limits suggestion lists are passed in as DPs (from the VM's LimitsXxx).</summary>
public partial class TypeDetailPanel : UserControl
{
    public TypeDetailPanel()
    {
        InitializeComponent();
        // Reset _editPending when the row changes so a pending snapshot from the previous row
        // can't suppress the next row's first pre-edit capture.
        DataContextChanged += (_, _) => { _editPending = false; HideRegister(); };
        // Subscribe BeforeChange on each chip control so we snapshot BEFORE the collection mutates.
        // Changed is already wired in XAML for the post-mutation re-lint step.
        Loaded += (_, _) =>
        {
            UsageChips.BeforeChange += OnChipsBeforeChange;
            ValueChips.BeforeChange += OnChipsBeforeChange;
            TagChips.BeforeChange += OnChipsBeforeChange;
        };
    }

    /// <summary>The owning view-model (for undo snapshots + re-lint after edits).</summary>
    public static readonly DependencyProperty VmProperty = DependencyProperty.Register(
        nameof(Vm), typeof(MainViewModel), typeof(TypeDetailPanel), new PropertyMetadata(null));

    public MainViewModel? Vm
    {
        get => (MainViewModel?)GetValue(VmProperty);
        set => SetValue(VmProperty, value);
    }

    public static readonly DependencyProperty UsageSuggestionsProperty = DependencyProperty.Register(
        nameof(UsageSuggestions), typeof(IEnumerable), typeof(TypeDetailPanel), new PropertyMetadata(null));
    public IEnumerable? UsageSuggestions
    {
        get => (IEnumerable?)GetValue(UsageSuggestionsProperty);
        set => SetValue(UsageSuggestionsProperty, value);
    }

    public static readonly DependencyProperty ValueSuggestionsProperty = DependencyProperty.Register(
        nameof(ValueSuggestions), typeof(IEnumerable), typeof(TypeDetailPanel), new PropertyMetadata(null));
    public IEnumerable? ValueSuggestions
    {
        get => (IEnumerable?)GetValue(ValueSuggestionsProperty);
        set => SetValue(ValueSuggestionsProperty, value);
    }

    public static readonly DependencyProperty TagSuggestionsProperty = DependencyProperty.Register(
        nameof(TagSuggestions), typeof(IEnumerable), typeof(TypeDetailPanel), new PropertyMetadata(null));
    public IEnumerable? TagSuggestions
    {
        get => (IEnumerable?)GetValue(TagSuggestionsProperty);
        set => SetValue(TagSuggestionsProperty, value);
    }

    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories), typeof(IEnumerable), typeof(TypeDetailPanel), new PropertyMetadata(null));
    public IEnumerable? Categories
    {
        get => (IEnumerable?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    // A field gaining keyboard focus snapshots the pre-edit state once (so typing + commit = one undo step).
    private bool _editPending;

    private void OnFieldFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editPending) return;
        Vm?.PushDetailEditUndo();
        _editPending = true;
    }

    private void OnFieldCommit(object sender, RoutedEventArgs e)
    {
        _editPending = false;
        Vm?.AfterDetailEdit();
    }

    // --- Slider undo: snapshot once at the start of a drag gesture, re-lint when it ends ---

    // True once we've snapshotted the pre-drag state for the current slider gesture. Reset on commit.
    private bool _sliderSnapshotPending;

    /// <summary>Fires on PreviewMouseLeftButtonDown over a Slider — before the thumb moves. Snapshot the
    /// pre-drag state once so the whole drag collapses to a single undo step.</summary>
    private void OnSliderPreviewMouse(object sender, MouseButtonEventArgs e)
    {
        if (_sliderSnapshotPending) return;
        Vm?.PushDetailEditUndo();
        _sliderSnapshotPending = true;
    }

    /// <summary>Fires when a slider drag completes (or it loses mouse capture). Re-lint + reset the guard.
    /// If no snapshot was taken (e.g. value unchanged), this is a cheap no-op re-lint.</summary>
    private void OnSliderCommit(object sender, RoutedEventArgs e)
    {
        _sliderSnapshotPending = false;
        Vm?.AfterDetailEdit();
    }

    // --- Flag toggle undo: snapshot BEFORE the value flips ---

    // True once we have snapshotted the pre-toggle state for the current user gesture (mouse click or
    // Space/Enter key). Reset by OnFlagToggled (the post-flip Click handler) so the next gesture can
    // capture again.
    private bool _flagSnapshotPending;

    /// <summary>Called on PreviewMouseLeftButtonDown for each flag ToggleSwitch — fires BEFORE the
    /// IsChecked binding flips, so this is the right moment to capture the pre-change state.</summary>
    private void OnFlagPreviewMouse(object sender, MouseButtonEventArgs e)
    {
        if (_flagSnapshotPending) return;
        Vm?.PushDetailEditUndo();
        _flagSnapshotPending = true;
    }

    /// <summary>Called on PreviewKeyDown for each flag ToggleSwitch (Space/Enter activate the toggle).
    /// Fires before the checked state changes.</summary>
    private void OnFlagPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter) return;
        if (_flagSnapshotPending) return;
        Vm?.PushDetailEditUndo();
        _flagSnapshotPending = true;
    }

    /// <summary>Click fires AFTER IsChecked has already flipped. We no longer push the undo snapshot
    /// here (that was the bug — it was post-change). We only call AfterDetailEdit (re-lint) and reset
    /// the per-gesture snapshot guard so the next toggle can snapshot again.</summary>
    private void OnFlagToggled(object sender, RoutedEventArgs e)
    {
        _flagSnapshotPending = false;
        Vm?.AfterDetailEdit();
    }

    // --- Chip undo: snapshot via BeforeChange (pre-mutation), re-lint via Changed (post-mutation) ---

    /// <summary>Wires up both BeforeChange and Changed on a chip control. Called from XAML via
    /// the Loaded event pattern — but since the controls are created in XAML we subscribe in code-behind
    /// via the Loaded event on the UserControl itself.</summary>
    private void OnChipsBeforeChange(object? sender, System.EventArgs e)
    {
        // Raised by ChipMultiSelect immediately BEFORE the collection mutation — perfect snapshot moment.
        Vm?.PushDetailEditUndo();
    }

    private void OnChipsChanged(object? sender, System.EventArgs e)
    {
        // Collection already mutated; re-lint and refresh grid text. No undo push here — that was
        // the original bug (snapshot was taken post-change). BeforeChange handles it above.
        Vm?.AfterDetailEdit();
        MaybeOfferRegister(sender as ChipMultiSelect);
    }

    // --- Free-add → dictionary affordance ---------------------------------
    // When a chip add introduces a value not in the live dictionary, surface a one-tap "register it"
    // bar so the value becomes game-honored (cfglimitsdefinition.xml), not just present on this type.

    private Dzl.Core.Economy.LimitsKind _pendingKind;
    private string? _pendingValue;

    private void MaybeOfferRegister(ChipMultiSelect? chip)
    {
        HideRegister();
        if (chip is null || Vm is null) return;
        var val = chip.LastAdded;
        if (string.IsNullOrWhiteSpace(val)) return;
        if (!TryKind(chip.Kind, out var kind)) return;
        if (!Vm.IsUnknownLimit(kind, val)) return;

        _pendingKind = kind;
        _pendingValue = val;
        RegisterText.Text = $"'{val}' isn't in the {chip.Kind} dictionary yet.";
        RegisterBar.Visibility = Visibility.Visible;
    }

    private static bool TryKind(string? s, out Dzl.Core.Economy.LimitsKind kind)
    {
        switch ((s ?? "").ToLowerInvariant())
        {
            case "usage": kind = Dzl.Core.Economy.LimitsKind.Usage; return true;
            case "value": kind = Dzl.Core.Economy.LimitsKind.Value; return true;
            case "tag": kind = Dzl.Core.Economy.LimitsKind.Tag; return true;
            case "category": kind = Dzl.Core.Economy.LimitsKind.Category; return true;
            default: kind = Dzl.Core.Economy.LimitsKind.Usage; return false;
        }
    }

    private void HideRegister()
    {
        RegisterBar.Visibility = Visibility.Collapsed;
        _pendingValue = null;
    }

    private void OnDismissRegister(object sender, RoutedEventArgs e) => HideRegister();

    private void OnRegisterToDictionary(object sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrWhiteSpace(_pendingValue)) { HideRegister(); return; }
        Vm.AddToDictionary(_pendingKind, _pendingValue);   // refreshes suggestions + re-lints on success
        HideRegister();
    }
}
