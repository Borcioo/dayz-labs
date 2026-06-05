using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Dzl.Tray.ViewModels;
using QRCoder;
using SteamKit2.Authentication;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Steam sign-in dialog: QR (scan with the mobile app) or username/password (+ Steam Guard via a
/// dialog-driven <see cref="IAuthenticator"/>). On success the VM stores the refresh token; the dialog closes.</summary>
public partial class SteamLoginWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<string>? _guard;

    public bool SignedIn { get; private set; }

    public SteamLoginWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Loaded += async (_, _) => await StartQrAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task StartQrAsync()
    {
        QrStatus.Text = "Connecting to Steam…";
        var r = await _vm.SteamLoginQrAsync(
            url => Dispatcher.Invoke(() => { QrImage.Source = MakeQr(url); QrStatus.Text = "Scan with the Steam mobile app, then approve."; }),
            _cts.Token);
        OnResult(r);
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        if (user.Length == 0 || PassBox.Password.Length == 0) { PassStatus.Text = "Enter username + password."; return; }
        SignInBtn.IsEnabled = false;
        PassStatus.Text = "Signing in…";
        var r = await _vm.SteamLoginCredentialsAsync(user, PassBox.Password, new DialogAuthenticator(this), _cts.Token);
        SignInBtn.IsEnabled = true;
        OnResult(r, password: true);
    }

    private void OnResult(Dzl.Core.Workshop.SteamLoginResult r, bool password = false)
    {
        if (r.Ok)
        {
            SignedIn = true;
            DialogResult = true;
            Close();
            return;
        }
        var msg = "✗ " + (string.IsNullOrWhiteSpace(r.Error) ? "sign-in failed" : r.Error);
        if (password) PassStatus.Text = msg; else QrStatus.Text = msg;
    }

    // --- Steam Guard prompt (driven by the IAuthenticator below) ---
    private Task<string> PromptGuardAsync(string prompt)
    {
        _guard = new TaskCompletionSource<string>();
        Dispatcher.Invoke(() =>
        {
            GuardPrompt.Text = prompt;
            GuardPanel.Visibility = Visibility.Visible;
            GuardBox.Text = "";
            GuardBox.Focus();
        });
        return _guard.Task;
    }

    private void OnGuardOk(object sender, RoutedEventArgs e)
    {
        var code = GuardBox.Text.Trim();
        GuardPanel.Visibility = Visibility.Collapsed;
        _guard?.TrySetResult(code);
    }

    private static BitmapImage MakeQr(string url)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(png);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // Prefer phone approval; fall back to prompting for an email/device code.
    private sealed class DialogAuthenticator : IAuthenticator
    {
        private readonly SteamLoginWindow _w;
        public DialogAuthenticator(SteamLoginWindow w) => _w = w;
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
            => _w.PromptGuardAsync($"Enter your Steam Guard (mobile authenticator) code{(previousCodeWasIncorrect ? " — last one was wrong" : "")}:");
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
            => _w.PromptGuardAsync($"Enter the Steam Guard code emailed to {email}{(previousCodeWasIncorrect ? " — last one was wrong" : "")}:");
        public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(true);   // wait for the phone tap
    }
}
