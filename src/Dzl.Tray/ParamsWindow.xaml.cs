using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Dzl.Tray;

/// <summary>
/// Modal editor for a per-mode params list (one flag per line). On OK exposes the
/// trimmed, non-empty lines via <see cref="Result"/> and sets <c>DialogResult=true</c>.
/// </summary>
public partial class ParamsWindow : Window
{
    private readonly List<string> _defaults;

    /// <summary>The edited params, populated when the dialog closes with OK.</summary>
    public List<string> Result { get; private set; } = new();

    public ParamsWindow(string title, List<string> current, List<string> defaults)
    {
        InitializeComponent();
        Title = title;
        _defaults = defaults;
        ParamsBox.Text = string.Join("\n", current);
    }

    private static List<string> Parse(string text) =>
        text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private void OnReset(object sender, RoutedEventArgs e) =>
        ParamsBox.Text = string.Join("\n", _defaults);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = Parse(ParamsBox.Text);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
