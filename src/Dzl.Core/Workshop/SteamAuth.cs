using System.Text;
using System.Text.Json;
using SteamKit2;
using SteamKit2.Authentication;

namespace Dzl.Core.Workshop;

/// <summary>Outcome of a Steam sign-in.</summary>
public sealed record SteamLoginResult(bool Ok, string AccountName, string RefreshToken, string AccessToken, string Error);

/// <summary>
/// Steam sign-in via SteamKit2 — QR (scan with the Steam mobile app) or username/password (+ Steam Guard via
/// an <see cref="IAuthenticator"/>). Returns a long-lived <b>refresh token</b> (store encrypted) + a short
/// <b>access token</b>; <see cref="RenewAccessTokenAsync"/> mints a fresh access token from the refresh token
/// without re-login. The access token is what the Workshop Subscribe API needs. Never throws.
/// </summary>
public sealed class SteamAuth : IDisposable
{
    private readonly SteamClient _client = new();
    private readonly CallbackManager _mgr;
    private CancellationTokenSource? _pump;

    public SteamAuth() => _mgr = new CallbackManager(_client);

    private async Task<bool> ConnectAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        _mgr.Subscribe<SteamClient.ConnectedCallback>(_ => tcs.TrySetResult(true));
        _mgr.Subscribe<SteamClient.DisconnectedCallback>(_ => tcs.TrySetResult(false));
        _pump = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _pump.Token;
        _ = Task.Run(() => { while (!token.IsCancellationRequested) _mgr.RunWaitCallbacks(TimeSpan.FromMilliseconds(100)); });
        _client.Connect();
        using (ct.Register(() => tcs.TrySetResult(false)))
            return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>QR sign-in: <paramref name="onChallengeUrl"/> receives the URL to render as a QR (and again
    /// whenever it refreshes). Resolves when the user approves on their phone.</summary>
    public async Task<SteamLoginResult> LoginViaQrAsync(Action<string> onChallengeUrl, CancellationToken ct)
    {
        try
        {
            if (!await ConnectAsync(ct)) return new(false, "", "", "", "could not connect to Steam");
            var session = await _client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails()).ConfigureAwait(false);
            onChallengeUrl(session.ChallengeURL);
            session.ChallengeURLChanged = () => onChallengeUrl(session.ChallengeURL);
            var poll = await session.PollingWaitForResultAsync(ct).ConfigureAwait(false);
            return new(true, poll.AccountName, poll.RefreshToken, poll.AccessToken ?? "", "");
        }
        catch (Exception ex) { return new(false, "", "", "", ex.Message); }
        finally { Cleanup(); }
    }

    /// <summary>Username/password sign-in; <paramref name="authenticator"/> supplies any Steam Guard code/confirmation.</summary>
    public async Task<SteamLoginResult> LoginViaCredentialsAsync(string username, string password, IAuthenticator authenticator, CancellationToken ct)
    {
        try
        {
            if (!await ConnectAsync(ct)) return new(false, "", "", "", "could not connect to Steam");
            var session = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = username,
                Password = password,
                IsPersistentSession = true,
                Authenticator = authenticator,
            }).ConfigureAwait(false);
            var poll = await session.PollingWaitForResultAsync(ct).ConfigureAwait(false);
            return new(true, poll.AccountName, poll.RefreshToken, poll.AccessToken ?? "", "");
        }
        catch (Exception ex) { return new(false, "", "", "", ex.Message); }
        finally { Cleanup(); }
    }

    /// <summary>Mint a fresh access token from a stored refresh token (no re-login). Null on failure.</summary>
    public async Task<string?> RenewAccessTokenAsync(string refreshToken)
    {
        try
        {
            var steamId = SteamIdFromToken(refreshToken);
            if (steamId is null) return null;
            if (!await ConnectAsync(CancellationToken.None)) return null;
            var r = await _client.Authentication.GenerateAccessTokenForAppAsync(steamId, refreshToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(r.AccessToken) ? null : r.AccessToken;
        }
        catch { return null; }
        finally { Cleanup(); }
    }

    /// <summary>Extract the SteamID from a Steam JWT (the <c>sub</c> claim is the steamid64). Pure.</summary>
    public static SteamID? SteamIdFromToken(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            return doc.RootElement.TryGetProperty("sub", out var sub) && ulong.TryParse(sub.GetString(), out var id)
                ? new SteamID(id) : null;
        }
        catch { return null; }
    }

    private void Cleanup()
    {
        try { _pump?.Cancel(); } catch { }
        try { _client.Disconnect(); } catch { }
    }

    public void Dispose() => Cleanup();
}
