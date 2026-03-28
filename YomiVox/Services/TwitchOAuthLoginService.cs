using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace YomiVox.Services;

/// <summary>
/// ブラウザで Twitch にログインし、PKCE + ローカル HTTPS コールバックでアクセストークンを取得する。
/// </summary>
public sealed class TwitchOAuthLoginService
{
    /// <summary>Twitch Developer Console の「OAuth リダイレクト URL」に登録する URI（HTTPS・末尾スラッシュ固定）。localhost と 127.0.0.1 は別扱いのため 127.0.0.1 に固定。</summary>
    public const string RedirectUri = "https://127.0.0.1:17563/";
    private const int CallbackPort = 17563;
    /// <summary>読み取り＋送信（IRC の PRIVMSG / TwitchLib の SendMessageAsync に必須）。</summary>
    private const string Scope = "chat:read chat:edit";

    private static readonly HttpClient Http = new();

    public sealed record OAuthResult(string AccessToken, string? Login, string? RefreshToken, DateTime? AccessTokenExpiresAtUtc);

    public sealed record TokenRefreshResult(string AccessToken, string? RefreshToken, DateTime? AccessTokenExpiresAtUtc);

    /// <summary>コールバック用に使うポート番号。</summary>
    public static int Port => CallbackPort;

    /// <param name="clientSecret">Twitch アプリの Client Secret。コンソールで「機密」扱いの場合はトークン交換に必須。</param>
    public async Task<OAuthResult> LoginAsync(string clientId, string? clientSecret = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client ID を入力してください。", nameof(clientId));

        var verifier = GenerateCodeVerifier();
        var challenge = GenerateCodeChallenge(verifier);
        var state = GenerateState();
        var codeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = CreateLocalHttpsApp(state, codeTcs);

        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"HTTPS でポート {CallbackPort} を待ち受けられませんでした。他アプリが使用中か、開発用 HTTPS 証明書が未設定の可能性があります。\n" +
                "PowerShell で「dotnet dev-certs https --trust」を実行してから再度お試しください。\n" +
                $"({ex.Message})",
                ex);
        }
        try
        {
            var authUrl = BuildAuthorizeUrl(clientId.Trim(), challenge, state);
            OpenBrowser(authUrl);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            var codeTask = codeTcs.Task;
            var finished = await Task.WhenAny(codeTask, Task.Delay(Timeout.Infinite, linked.Token))
                .ConfigureAwait(false);
            if (finished != codeTask)
                throw new OperationCanceledException("認証がタイムアウトしたか、キャンセルされました。");

            var code = await codeTask.ConfigureAwait(false);
            var token = await ExchangeCodeAsync(clientId.Trim(), clientSecret, code, verifier, ct).ConfigureAwait(false);
            var login = await TryGetLoginAsync(clientId.Trim(), token.AccessToken, ct).ConfigureAwait(false);
            return new OAuthResult(token.AccessToken, login, token.RefreshToken, token.ExpiresAtUtc);
        }
        finally
        {
            try
            {
                await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static WebApplication CreateLocalHttpsApp(string expectedState, TaskCompletionSource<string> codeTcs)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, CallbackPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                listenOptions.UseHttps();
            });
        });

        var app = builder.Build();
        app.MapGet("/", async (HttpContext ctx) =>
        {
            var q = ParseQuery(ctx.Request.Query);
            const string htmlHead =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>YomiVox</title></head><body>";
            const string htmlFoot = "</body></html>";

            if (q.TryGetValue("error", out var err))
            {
                var desc = q.TryGetValue("error_description", out var d) ? d : err;
                var body = "<p>認証できませんでした。このタブを閉じてアプリに戻ってください。</p><p>" +
                           WebUtility.HtmlEncode(desc) + "</p>";
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(htmlHead + body + htmlFoot, Encoding.UTF8).ConfigureAwait(false);
                codeTcs.TrySetException(new InvalidOperationException("Twitch が認証を完了しませんでした: " + desc));
                return;
            }

            if (!q.TryGetValue("state", out var st) || st != expectedState)
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response
                    .WriteAsync(htmlHead + "<p>無効なリクエストです。</p>" + htmlFoot, Encoding.UTF8)
                    .ConfigureAwait(false);
                return;
            }

            if (!q.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response
                    .WriteAsync(htmlHead + "<p>認証コードがありません。</p>" + htmlFoot, Encoding.UTF8)
                    .ConfigureAwait(false);
                return;
            }

            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response
                .WriteAsync(
                    htmlHead + "<p>認証が完了しました。このタブを閉じてアプリに戻ってください。</p>" + htmlFoot,
                    Encoding.UTF8)
                .ConfigureAwait(false);
            codeTcs.TrySetResult(code);
        });

        app.MapGet("/favicon.ico", () => Results.NotFound());

        return app;
    }

    private static string BuildAuthorizeUrl(string clientId, string challenge, string state)
    {
        var ub = new UriBuilder("https://id.twitch.tv/oauth2/authorize");
        var q = new List<string>
        {
            "client_id=" + Uri.EscapeDataString(clientId),
            "redirect_uri=" + Uri.EscapeDataString(RedirectUri),
            "response_type=code",
            "scope=" + Uri.EscapeDataString(Scope),
            "state=" + Uri.EscapeDataString(state),
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "code_challenge_method=S256"
        };
        ub.Query = string.Join("&", q);
        return ub.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQuery(IQueryCollection query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
            d[kv.Key] = kv.Value.ToString();
        return d;
    }

    private sealed record TokenExchangeResponse(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc);

    private static async Task<TokenExchangeResponse> ExchangeCodeAsync(
        string clientId,
        string? clientSecret,
        string code,
        string verifier,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
            form["client_secret"] = clientSecret.Trim();

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
        req.Content = new FormUrlEncodedContent(form);

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"トークン取得に失敗しました: {(int)resp.StatusCode} {json}");

        return ParseTokenResponse(json);
    }

    /// <summary>保存済みの refresh_token で access_token を更新。Client Secret 必須（機密クライアント）。</summary>
    public async Task<TokenRefreshResult> RefreshAccessTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client ID が空です。", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new ArgumentException("リフレッシュには Client Secret が必要です。", nameof(clientSecret));
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh Token が空です。", nameof(refreshToken));

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["client_secret"] = clientSecret.Trim(),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken.Trim()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
        req.Content = new FormUrlEncodedContent(form);

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"トークン更新に失敗しました: {(int)resp.StatusCode} {json}");

        var parsed = ParseTokenResponse(json);
        return new TokenRefreshResult(parsed.AccessToken, parsed.RefreshToken, parsed.ExpiresAtUtc);
    }

    private static TokenExchangeResponse ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var at))
            throw new InvalidOperationException("レスポンスに access_token がありません: " + json);
        var access = at.GetString() ?? throw new InvalidOperationException("access_token が空です。");
        string? refresh = null;
        if (root.TryGetProperty("refresh_token", out var rt))
            refresh = rt.GetString();
        DateTime? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var exp))
            expiresAt = DateTime.UtcNow.AddSeconds(exp.GetInt32());
        return new TokenExchangeResponse(access, refresh, expiresAt);
    }

    private static async Task<string?> TryGetLoginAsync(string clientId, string accessToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.TryAddWithoutValidation("Client-Id", clientId);

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;
            var first = data[0];
            if (first.TryGetProperty("login", out var login))
                return login.GetString();
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static string GenerateCodeVerifier()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        var rng = Random.Shared;
        var sb = new StringBuilder(64);
        for (var i = 0; i < 64; i++)
            sb.Append(chars[rng.Next(chars.Length)]);
        return sb.ToString();
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateState()
    {
        var b = new byte[16];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b);
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
