using System.Threading;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace YomiVox.Services;

public sealed class TwitchChatService : IDisposable
{
    private TwitchClient? _client;
    private bool _disposed;
    private volatile bool _userDisconnectRequested;

    private string? _login;
    private string? _oauth;
    /// <summary>参加中チャンネル（小文字・重複なし）。</summary>
    private readonly List<string> _joinedChannels = new();

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private int _reconnectAttempt;

    /// <summary>アプリから送った <c>!</c> 始まりの文を、IRC エコーでコマンド扱いしないための記録。</summary>
    private readonly object _sentEchoLock = new();
    private readonly List<(string Text, DateTime Utc)> _recentOwnSendsWithBang = new();

    public event EventHandler<(string Channel, string Username, string? DisplayName, string Message, bool IsEchoOwnSent)>?
        MessageReceived;
    public event EventHandler<string>? StatusChanged;

    /// <summary>IRC に接続しており、かつ 1 つ以上のチャンネルに参加しているとき true。</summary>
    public bool IsConnected => _client?.IsConnected == true && _joinedChannels.Count > 0;

    public IReadOnlyList<string> ConnectedChannels => _joinedChannels;

    private static string NormalizeChannel(string channel) =>
        channel.Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>指定チャンネルに参加済みなら true。</summary>
    public bool IsChannelJoined(string channel)
    {
        var ch = NormalizeChannel(channel);
        return _joinedChannels.Contains(ch);
    }

    /// <summary>IRC に接続し、指定チャンネルに参加する（既に参加なら何もしない）。</summary>
    public async Task JoinChannelAsync(string twitchLogin, string oauthToken, string channelName,
        CancellationToken ct = default)
    {
        var ch = NormalizeChannel(channelName);
        if (string.IsNullOrEmpty(ch))
            throw new ArgumentException("チャンネル名が空です。");

        var token = oauthToken.Trim();
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("OAuth トークンがありません。");

        var login = twitchLogin.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login))
            throw new ArgumentException("Twitch ログイン名がありません。");

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed)
                throw new ObjectDisposedException(nameof(TwitchChatService));

            _userDisconnectRequested = false;

            if (!token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
                token = "oauth:" + token;

            _login = login;
            _oauth = oauthToken.Trim();

            if (_client?.IsConnected == true)
            {
                if (_joinedChannels.Contains(ch))
                    return;
            }
            else
            {
                _joinedChannels.Clear();
                await CreateClientAndConnectAsync(login, token).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            await _client!.JoinChannelAsync(ch).ConfigureAwait(false);
            if (!_joinedChannels.Contains(ch))
                _joinedChannels.Add(ch);
            Interlocked.Exchange(ref _reconnectAttempt, 0);
            RaiseStatus($"参加しました: #{ch}");
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>指定チャンネルから抜ける。最後の 1 つなら IRC も切断する。</summary>
    public async Task LeaveChannelAsync(string channelName, CancellationToken ct = default)
    {
        var ch = NormalizeChannel(channelName);
        if (string.IsNullOrEmpty(ch)) return;

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client == null || !_joinedChannels.Contains(ch))
                return;

            await _client.LeaveChannelAsync(ch).ConfigureAwait(false);
            _joinedChannels.Remove(ch);

            if (_joinedChannels.Count == 0)
            {
                await DisconnectInternalAsync().ConfigureAwait(false);
                RaiseStatus($"切断しました: #{ch}（他に参加中のチャンネルはありません）");
            }
            else
            {
                RaiseStatus($"退出しました: #{ch}");
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task CreateClientAndConnectAsync(string login, string oauthWithPrefix)
    {
        await DisconnectInternalAsync().ConfigureAwait(false);

        var creds = new ConnectionCredentials(login, oauthWithPrefix);
        var client = new TwitchClient();
        client.Initialize(creds, new List<string>());

        client.OnConnected += OnConnectedAsync;
        client.OnDisconnected += OnDisconnectedAsync;
        client.OnReconnected += OnReconnectedAsync;
        client.OnMessageReceived += OnMessageReceivedAsync;
        client.OnConnectionError += OnConnectionErrorAsync;

        _client = client;
        await client.ConnectAsync().ConfigureAwait(false);
    }

    private async Task OnConnectedAsync(object? sender, EventArgs e)
    {
        if (_joinedChannels.Count > 0)
            RaiseStatus("接続しました: " + string.Join(", ", _joinedChannels.Select(c => "#" + c)));
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task OnReconnectedAsync(object? sender, EventArgs e)
    {
        RaiseStatus("再接続に成功しました");
        Interlocked.Exchange(ref _reconnectAttempt, 0);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task OnDisconnectedAsync(object? sender, EventArgs e)
    {
        if (_userDisconnectRequested || _disposed)
        {
            RaiseStatus("切断しました");
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        var chCopy = _joinedChannels.ToList();
        if (chCopy.Count == 0)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        RaiseStatus("チャット接続が切れました。自動で再接続します…");
        var attempt = Interlocked.Increment(ref _reconnectAttempt);
        var delayMs = Math.Min(1500 + attempt * 1500, 60000);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (_userDisconnectRequested || _disposed || _login == null || _oauth == null || chCopy.Count == 0)
                    return;
                RaiseStatus($"再接続を試行中 ({attempt} 回目)…");
                await ReconnectChannelsAsync(chCopy, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseStatus($"再接続に失敗: {ex.Message}");
            }
        });
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>ネット切断後、同じチャンネル一覧で再接続する。</summary>
    private async Task ReconnectChannelsAsync(IReadOnlyList<string> channelsToRestore, CancellationToken ct)
    {
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_userDisconnectRequested || _disposed || _login == null || _oauth == null ||
                channelsToRestore.Count == 0)
                return;

            var token = _oauth.Trim();
            if (!token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
                token = "oauth:" + token;

            _joinedChannels.Clear();
            await CreateClientAndConnectAsync(_login, token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            foreach (var ch in channelsToRestore)
            {
                ct.ThrowIfCancellationRequested();
                await _client!.JoinChannelAsync(ch).ConfigureAwait(false);
                if (!_joinedChannels.Contains(ch))
                    _joinedChannels.Add(ch);
            }

            RaiseStatus("再接続しました: " + string.Join(", ", _joinedChannels.Select(c => "#" + c)));
            Interlocked.Exchange(ref _reconnectAttempt, 0);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task OnMessageReceivedAsync(object? sender, OnMessageReceivedArgs e)
    {
        var msg = e.ChatMessage.Message;
        if (!string.IsNullOrWhiteSpace(msg))
        {
            var ch = e.ChatMessage.Channel?.TrimStart('#').ToLowerInvariant() ?? "";
            var echo = TryConsumeOwnBangEcho(e.ChatMessage.Username, msg);
            var displayName = string.IsNullOrWhiteSpace(e.ChatMessage.DisplayName)
                ? null
                : e.ChatMessage.DisplayName.Trim();
            MessageReceived?.Invoke(this, (ch, e.ChatMessage.Username, displayName, msg, echo));
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void RegisterOwnBangSend(string trimmedMessage)
    {
        if (!trimmedMessage.StartsWith('!')) return;
        lock (_sentEchoLock)
        {
            PruneOwnBangSends(DateTime.UtcNow);
            _recentOwnSendsWithBang.Add((trimmedMessage, DateTime.UtcNow));
        }
    }

    private static void PruneOwnBangSends(DateTime utcNow, List<(string Text, DateTime Utc)> list)
    {
        list.RemoveAll(x => (utcNow - x.Utc).TotalSeconds > 30);
    }

    private void PruneOwnBangSends(DateTime utcNow)
    {
        PruneOwnBangSends(utcNow, _recentOwnSendsWithBang);
    }

    /// <summary>自分が送った <c>!</c> 付き文のエコーなら true（キューから 1 件消費）。</summary>
    private bool TryConsumeOwnBangEcho(string username, string message)
    {
        if (_login == null) return false;
        if (!string.Equals(username.Trim(), _login, StringComparison.OrdinalIgnoreCase)) return false;
        var m = message.Trim();
        if (!m.StartsWith('!')) return false;
        lock (_sentEchoLock)
        {
            var now = DateTime.UtcNow;
            PruneOwnBangSends(now);
            for (var i = 0; i < _recentOwnSendsWithBang.Count; i++)
            {
                if (string.Equals(_recentOwnSendsWithBang[i].Text, m, StringComparison.Ordinal))
                {
                    _recentOwnSendsWithBang.RemoveAt(i);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>チャンネルにチャットを送る。<paramref name="channel"/> が null のときは参加中の先頭。</summary>
    public async Task<(bool Ok, string? Error)> SendChatMessageAsync(string text, string? channel = null,
        CancellationToken ct = default)
    {
        var t = text.Trim();
        if (string.IsNullOrEmpty(t))
            return (false, "メッセージが空です。");

        var c = _client;
        var list = _joinedChannels;
        if (c == null || !c.IsConnected || list.Count == 0)
            return (false, "チャットに接続していません。");

        var ch = channel?.Trim().TrimStart('#').ToLowerInvariant();
        if (string.IsNullOrEmpty(ch))
            ch = list[0];
        else if (!list.Contains(ch))
            return (false, $"接続していないチャンネルです: #{ch}");

        ct.ThrowIfCancellationRequested();
        try
        {
            await c.SendMessageAsync(ch, t, false).ConfigureAwait(false);
            RegisterOwnBangSend(t);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task OnConnectionErrorAsync(object? sender, OnConnectionErrorArgs e)
    {
        RaiseStatus($"接続エラー: {e.Error.Message}");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void RaiseStatus(string s) => StatusChanged?.Invoke(this, s);

    /// <summary>すべてのチャンネルから抜け、自動再接続しない状態にする。</summary>
    public async Task DisconnectAsync()
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _userDisconnectRequested = true;
            _joinedChannels.Clear();
            await DisconnectInternalAsync().ConfigureAwait(false);
            lock (_sentEchoLock)
                _recentOwnSendsWithBang.Clear();
            _login = null;
            _oauth = null;
            Interlocked.Exchange(ref _reconnectAttempt, 0);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        var c = _client;
        _client = null;
        if (c == null) return;
        try
        {
            c.OnConnected -= OnConnectedAsync;
            c.OnDisconnected -= OnDisconnectedAsync;
            c.OnReconnected -= OnReconnectedAsync;
            c.OnMessageReceived -= OnMessageReceivedAsync;
            c.OnConnectionError -= OnConnectionErrorAsync;
            if (c.IsConnected)
                await c.DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _userDisconnectRequested = true;
        _joinedChannels.Clear();
        _ = DisconnectInternalAsync();
        _connectGate.Dispose();
    }
}
