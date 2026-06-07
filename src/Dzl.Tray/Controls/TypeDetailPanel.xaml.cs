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

    private void OnStepUp(object sender, RoutedEventArgs e) => Step(sender, +1);
    private void OnStepDown(object sender, RoutedEventArgs e) => Step(sender, -1);

    private void Step(object sender, int delta)
    {
        if (DataContext is TypeRowVm row && sender is FrameworkElement { Tag: string field })
            Vm?.StepField(row, field, delta);
    }

    private void OnFlagToggled(object sender, RoutedEventArgs e)
    {
        // The IsChecked binding already wrote the new value; snapshot + re-lint as one step.
        Vm?.PushDetailEditUndo();
        Vm?.AfterDetailEdit();
    }

    private void OnChipsChanged(object sender, System.EventArgs e)
    {
        // Chip add/remove already mutated the row collection; snapshot + re-lint, and refresh grid text.
        Vm?.PushDetailEditUndo();
        Vm?.AfterDetailEdit();
    }
}
