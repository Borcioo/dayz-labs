using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray;
using FluentAssertions;
using Wpf.Ui.Appearance;

/// <summary>
/// Tier-2 smoke: instantiate and realize every WPF UserControl / Page in the Dzl.Tray assembly on one STA
/// thread, so the runtime-only XAML failures that compile clean — invalid WPF-UI <c>SymbolIcon</c> names,
/// <c>ui:*</c> styles missing <c>BasedOn</c>, and StaticResource keys a UserControl can't see (UserControls
/// don't inherit host-window resources) — surface as a red test instead of a crash in the running app.
///
/// This is the in-process xunit form of the existing <c>DZL_SMOKE_WINDOW</c> harness in App.xaml.cs. It loads
/// the REAL App.xaml resource dictionaries (Themes + Controls + Colors/Converters/Styles) so app-scope keys
/// resolve exactly as they do at runtime. Reflection-driven, so every control added later is covered for free.
/// </summary>
public class WpfRealizationSmokeTests
{
    /// <summary>Create the single Application instance and load App.xaml's merged dictionaries + dark theme.
    /// Must run on the STA thread that will realize the controls (WPF objects have thread affinity).</summary>
    private static void EnsureApp()
    {
        if (Application.Current is not null) return;
        var app = new App();
        app.InitializeComponent();   // loads App.xaml MergedDictionaries into Application.Current.Resources
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }

    /// <summary>Realizable = a concrete FrameworkElement (UserControl/Page/…) in the Tray assembly with a
    /// public parameterless ctor. Windows are excluded (they need a VM + Show, not Measure/Arrange).</summary>
    private static List<Type> RealizableControls() =>
        typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                        && typeof(FrameworkElement).IsAssignableFrom(t)
                        && !typeof(Window).IsAssignableFrom(t)
                        && !typeof(Application).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name)
            .ToList();

    [WpfFact]
    public void Every_usercontrol_realizes_without_throwing()
    {
        EnsureApp();

        var controls = RealizableControls();
        controls.Should().NotBeEmpty("reflection must find the Tray UserControls/Views to smoke-test");

        var failures = new List<string>();
        foreach (var type in controls)
        {
            try
            {
                var el = (FrameworkElement)Activator.CreateInstance(type)!;
                // Force template application + style/resource resolution without showing a window. This is
                // the point where a bad SymbolIcon / missing StaticResource / BasedOn-less style throws.
                el.Measure(new Size(1200, 900));
                el.Arrange(new Rect(0, 0, 1200, 900));
                el.UpdateLayout();
            }
            catch (Exception ex)
            {
                var b = ex.GetBaseException();
                failures.Add($"{type.FullName}: {b.Message}\n{b.StackTrace}");
            }
        }

        failures.Should().BeEmpty(
            "every UserControl/View must instantiate + realize cleanly; failures are runtime-only XAML defects:\n"
            + string.Join("\n", failures));
    }
}
