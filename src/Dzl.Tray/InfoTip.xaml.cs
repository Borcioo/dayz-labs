using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray;

/// <summary>
/// A tiny inline information icon (ⓘ) that shows a wrapped explanatory tooltip.
/// Place it next to a label/control to teach what an option is and why it matters.
/// Usage: &lt;local:InfoTip Text="{x:Static local:HelpText.Mode}"/&gt;
/// </summary>
public partial class InfoTip : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(InfoTip),
            new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public InfoTip()
    {
        InitializeComponent();
    }
}
