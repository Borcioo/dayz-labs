using System.Text;
using System.Text.Json;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

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
            // MobileApp platform: its access token works against the Steam Web API (Workshop Subscribe) AND its
            // refresh token can mint new access tokens via GenerateAccessTokenForApp. As of 2025-04, that renewal
            // ONLY works for MobileApp tokens — WebBrowser/SteamClient refresh tokens fail with AccessDenied.
            var session = await _client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
            }).ConfigureAwait(false);
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
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,  // only MobileApp refresh tokens can renew (see QR note)
            }).ConfigureAwait(false);
            var poll = await session.PollingWaitForResultAsync(ct).ConfigureAwait(false);
            return new(true, poll.AccountName, poll.RefreshToken, poll.AccessToken ?? "", "");
        }
        catch (Exception ex) { return new(false, "", "", "", ex.Message); }
        finally { Cleanup(); }
    }

    /// <summary>Mint a fresh access token from a stored refresh token (no re-login). Returns (token, error).</summary>
    public async Task<(string? token, string error)> RenewAccessTokenAsync(string refreshToken)
    {
        try
        {
            var steamId = SteamIdFromToken(refreshToken);
            if (steamId is null) return (null, "couldn't read the SteamID from the saved session");
            if (!await ConnectAsync(CancellationToken.None)) return (null, "couldn't connect to Steam");
            var r = await _client.Authentication.GenerateAccessTokenForAppAsync(steamId, refreshToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(r.AccessToken) ? (null, "Steam returned no access token") : (r.AccessToken, "");
        }
        catch (Exception ex) { return (null, ex.Message); }
        finally { Cleanup(); }
    }

    /// <summary>Extract the SteamID from a Steam JWT (the <c>sub</c> claim is the steamid64). Pure.</summary>
    public static SteamID? SteamIdFromToken(string jwt)
    {
        var sub = Claim(jwt, "sub");
        return sub is not null && ulong.TryParse(sub, out var id) ? new SteamID(id) : null;
    }

    /// <summary>True when a Steam JWT access token is still valid for at least <paramref name="margin"/> (default
    /// 10 min) — reads the <c>exp</c> claim. A token with no readable expiry is treated as expired. Pure.</summary>
    public static bool TokenStillValid(string jwt, TimeSpan? margin = null)
    {
        var exp = Claim(jwt, "exp");
        if (exp is null || !long.TryParse(exp, out var unix)) return false;
        return DateTimeOffset.FromUnixTimeSeconds(unix) - DateTimeOffset.UtcNow > (margin ?? TimeSpan.FromMinutes(10));
    }

    /// <summary>Read a string claim from a Steam JWT payload (the middle, base64url segment). Pure; null on any error.</summary>
    private static string? Claim(string jwt, string name)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            if (!doc.RootElement.TryGetProperty(name, out var v)) return null;
            return v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
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
