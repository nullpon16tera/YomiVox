using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YomiVox.Services;

namespace YomiVox;

public partial class MainWindow : Window
{
    private readonly TwitchChatService _twitch = new();
    private readonly TwitchOAuthLoginService _oauthLogin = new();
    private readonly UserSpeakerMapper _userSpeakers = new();
    private VoicevoxClient? _voicevox;
    private SpeechPipeline? _pipeline;
    private IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> _speakerStylesByCharacter =
        new Dictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>>();
    private bool _uiReady;
    private DispatcherTimer? _saveDebounceTimer;
    private string _refreshToken = "";
    private DateTime? _oauthAccessExpiresAtUtc;
    private IReadOnlyList<CustomChatCommandEntry> _customCommandsSorted = Array.Empty<CustomChatCommandEntry>();

    private readonly ObservableCollection<ChannelRow> _channelRows = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        _twitch.MessageReceived += OnTwitchMessage;
        _twitch.StatusChanged += OnTwitchStatus;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        StreamUrlBox.Text = s.StreamUrl ?? "";
        _channelRows.Clear();
        if (s.TwitchChannelUrls.Count > 0)
        {
            foreach (var c in s.TwitchChannelUrls)
                _channelRows.Add(new ChannelRow(() => ScheduleDebouncedSave()) { Text = c });
        }
        else
            _channelRows.Add(new ChannelRow(() => ScheduleDebouncedSave()));

        ChannelRowsPanel.ItemsSource = _channelRows;
        SyncRefreshStateFromStore(s);

        _uiReady = true;
        StreamUrlBox.TextChanged += (_, _) => ScheduleDebouncedSave();
        _channelRows.CollectionChanged += (_, _) => ScheduleDebouncedSave();

        ReloadCustomCommandsCache();
        await ReloadVoicevoxAsync().ConfigureAwait(true);
        UpdateChatSendUi();
    }

    /// <summary>設定ウィンドウの OK 後、または起動時と同じ基準で VOICEVOX を再構築。</summary>
    public async Task ReloadVoicevoxAsync()
    {
        var s = SettingsStore.Load();
        await ReloadVoicevoxCoreAsync(s.VoicevoxUrl, s.AudioDeviceNumber, s.PlaybackVolumePercent).ConfigureAwait(true);
    }

    /// <summary>設定画面の「話者を再取得」用。未保存の入力値で試す。</summary>
    public async Task ReloadVoicevoxFromSettingsUiAsync(string voicevoxUrl, int audioDeviceNumber, double playbackVolumePercent)
    {
        await ReloadVoicevoxCoreAsync(voicevoxUrl, audioDeviceNumber, playbackVolumePercent).ConfigureAwait(true);
    }

    private async Task ReloadVoicevoxCoreAsync(string? voicevoxUrl, int audioDeviceNumber, double playbackVolumePercent)
    {
        try
        {
            _pipeline?.Dispose();
            _voicevox?.Dispose();

            var baseUrl = string.IsNullOrWhiteSpace(voicevoxUrl)
                ? "http://127.0.0.1:50021/"
                : voicevoxUrl.Trim();
            _voicevox = new VoicevoxClient(baseUrl,
                (cn, user) => VoiceCharacterSynthSettings.GetMergedForSynth(SettingsStore.Load(), cn, user));
            _pipeline = new SpeechPipeline(_voicevox);

            if (audioDeviceNumber >= 0)
                _pipeline.SetPlaybackDeviceNumber(audioDeviceNumber);
            else
                _pipeline.SetPlaybackDeviceNumber(-1);

            var vp = double.IsNaN(playbackVolumePercent) || playbackVolumePercent < 50 || playbackVolumePercent > 400
                ? 250
                : playbackVolumePercent;
            _pipeline.SetPlaybackVolumeMultiplier((float)(vp / 100.0));

            var grouped = await _voicevox.GetSpeakerStylesGroupedAsync().ConfigureAwait(true);
            _speakerStylesByCharacter = grouped;
            _userSpeakers.Clear();
            if (grouped.Count > 0)
            {
                _userSpeakers.ApplyPersistedAssignments(ViewerSettingsStore.Load().UserViewerVoiceAssignments, grouped);
                _userSpeakers.PersistToSettings();
            }

            AppendChatLine(
                $"VOICEVOX: 話者キャラ {grouped.Count} 種を取得しました（視聴者のキャラ・スタイルは保存され、次回起動後も継続。初回のみランダム。!voice でキャラ、!style でスタイル）。");
            if (_uiReady) SaveChannelOnly();
        }
        catch (Exception ex)
        {
            _speakerStylesByCharacter = new Dictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>>();
            _userSpeakers.Clear();
            AppendChatLine($"VOICEVOX エラー: {ex.Message}");
        }
    }

    public void SyncRefreshStateFromStore(AppSettings s)
    {
        _refreshToken = s.RefreshToken ?? "";
        _oauthAccessExpiresAtUtc = s.OAuthAccessExpiresAtUtc;
    }

    public bool TryGetSpeakerCount(out int count)
    {
        count = _speakerStylesByCharacter.Count;
        return count > 0;
    }

    /// <summary>設定のプルダウン用。VOICEVOX のキャラ名（話者 style id の若い順）。</summary>
    public IReadOnlyList<string> GetVoicevoxCharacterNames() =>
        _speakerStylesByCharacter
            .OrderBy(kv => kv.Value.Min(s => s.Id))
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>指定キャラのスタイル名一覧（style id 昇順）。</summary>
    public IReadOnlyList<string> GetStyleNamesForCharacter(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return Array.Empty<string>();
        var q = characterName.Trim();
        foreach (var kv in _speakerStylesByCharacter)
        {
            if (kv.Key.Equals(q, StringComparison.OrdinalIgnoreCase))
                return kv.Value.OrderBy(s => s.Id).Select(s => s.StyleName).Distinct().ToList();
        }

        foreach (var kv in _speakerStylesByCharacter)
        {
            if (kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                return kv.Value.OrderBy(s => s.Id).Select(s => s.StyleName).Distinct().ToList();
        }

        return Array.Empty<string>();
    }

    /// <summary>ヘルプ表示用。指定キャラのスタイルを id 昇順で。</summary>
    public IReadOnlyList<VoicevoxSpeakerStyle> GetStylesOrderedForCharacter(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return Array.Empty<VoicevoxSpeakerStyle>();
        var q = characterName.Trim();
        foreach (var kv in _speakerStylesByCharacter)
        {
            if (kv.Key.Equals(q, StringComparison.OrdinalIgnoreCase))
                return kv.Value.OrderBy(s => s.Id).ToList();
        }

        foreach (var kv in _speakerStylesByCharacter)
        {
            if (kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                return kv.Value.OrderBy(s => s.Id).ToList();
        }

        return Array.Empty<VoicevoxSpeakerStyle>();
    }

    private static bool IsBroadcasterUsername(string username)
    {
        var login = SettingsStore.Load().TwitchLogin.Trim();
        return !string.IsNullOrEmpty(login) &&
               string.Equals(username.Trim(), login, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChatBotUsername(string username)
    {
        var s = SettingsStore.Load();
        return ChatBotUsernameList.ContainsUsername(s.ChatBotUsernamesCsv, username);
    }

    public void NotifySettingsAppliedFromDialog()
    {
        var s = SettingsStore.Load();
        SyncRefreshStateFromStore(s);
        _ = ReloadVoicevoxAsync();
    }

    /// <summary>設定をキャンセルしたとき、試聴用に変えた VOICEVOX 状態を保存済み設定に戻す。</summary>
    public void NotifySettingsCancelledFromDialog()
    {
        _ = ReloadVoicevoxAsync();
    }

    public void AppendChatLine(string line)
    {
        var t = DateTime.Now.ToString("HH:mm:ss");
        ChatList.Items.Add($"[{t}] {line}");
        ChatList.ScrollIntoView(ChatList.Items[^1]);
    }

    private static string FormatTwitchSendFailure(string? err) =>
        $"[Twitch送信失敗] {err ?? "不明"}（チャット送信には OAuth の chat:edit が必要です。「ツール」→「オプション」で「ブラウザでログイン」し直してください。）";

    /// <summary>アプリ内ログに出しつつ、接続中なら同じ文を Twitch チャットにも送る（!voice などの応答用）。</summary>
    private void AppendChatLineMirrorTwitch(string line)
    {
        AppendChatLine(line);
        if (!_twitch.IsConnected) return;
        var targetCh = Dispatcher.Invoke(GetSendTargetChannelName);
        if (string.IsNullOrEmpty(targetCh)) return;
        _ = Task.Run(async () =>
        {
            var (ok, err) = await _twitch.SendChatMessageAsync(line, targetCh).ConfigureAwait(false);
            if (!ok)
                _ = Dispatcher.BeginInvoke(() => AppendChatLine(FormatTwitchSendFailure(err)));
        });
    }

    private string? GetSendTargetChannelName()
    {
        if (SendChannelCombo.SelectedItem is string sel && !string.IsNullOrEmpty(sel))
            return sel;
        var list = GetNormalizedChannelsForConnect();
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>各行の入力から重複を除いた接続用チャンネル名（小文字）。</summary>
    private List<string> GetNormalizedChannelsForConnect()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var row in _channelRows)
        {
            var n = TwitchChannelParser.ParseToLoginName(row.Text.Trim());
            if (string.IsNullOrEmpty(n)) continue;
            var lower = n.ToLowerInvariant();
            if (seen.Add(lower))
                list.Add(lower);
        }

        return list;
    }

    private void ScheduleDebouncedSave()
    {
        if (!_uiReady) return;
        _saveDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Tick -= DebounceSaveTick;
        _saveDebounceTimer.Tick += DebounceSaveTick;
        _saveDebounceTimer.Start();
    }

    private void DebounceSaveTick(object? sender, EventArgs e)
    {
        _saveDebounceTimer?.Stop();
        SaveChannelOnly();
    }

    private void SaveChannelOnly()
    {
        var s = SettingsStore.Load();
        s.StreamUrl = StreamUrlBox.Text;
        s.TwitchChannelUrls = GetNormalizedChannelsForConnect();
        s.ChannelUrl = s.TwitchChannelUrls.Count > 0 ? s.TwitchChannelUrls[0] : "";
        SettingsStore.Save(s);
    }

    private void OpenStreamUrl_Click(object sender, RoutedEventArgs e)
    {
        var raw = StreamUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        var url = raw.Contains("://", StringComparison.Ordinal) ? raw : "https://" + raw;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
            (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("有効な http(s) URL を入力してください。", "配信 URL", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = u.ToString(), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ブラウザを開けませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExitApplication_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(this);
        dlg.ShowDialog();
    }

    private void OpenSpeakerStylesHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SpeakerStylesHelpWindow(this);
        dlg.ShowDialog();
    }

    private void OpenChatCommandsHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ChatCommandsHelpWindow(this);
        dlg.ShowDialog();
    }

    private void OpenVoiceLibraryHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VoiceLibraryHelpWindow(this);
        dlg.ShowDialog();
    }

    private void ShowAboutVersion_Click(object sender, RoutedEventArgs e)
    {
        var asm = typeof(MainWindow).Assembly;
        var name = asm.GetName().Name ?? "YomiVox";
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var assemblyVersion = asm.GetName().Version?.ToString() ?? "?";
        var versionLine = !string.IsNullOrWhiteSpace(informational)
            ? informational.Trim()
            : assemblyVersion;
        MessageBox.Show(
            $"{name}\r\n\r\nバージョン: {versionLine}",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenCustomCommands_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomCommandsSettingsWindow(this);
        if (dlg.ShowDialog() == true)
            ReloadCustomCommandsCache();
    }

    private void ReloadCustomCommandsCache()
    {
        var s = SettingsStore.Load();
        s.CustomChatCommands ??= new List<CustomChatCommandEntry>();
        _customCommandsSorted = s.CustomChatCommands
            .Where(e => !string.IsNullOrWhiteSpace(e.CommandTrigger) && !string.IsNullOrWhiteSpace(e.ResponseText))
            .Select(e => new CustomChatCommandEntry
            {
                CommandTrigger = e.CommandTrigger.TrimStart(),
                ResponseText = e.ResponseText.Trim()
            })
            .OrderByDescending(e => e.CommandTrigger.Length)
            .ToList();
    }

    private void OnTwitchMessage(object? sender,
        (string Channel, string Username, string? DisplayName, string Message, bool IsEchoOwnSent) x)
    {
        if (x.IsEchoOwnSent)
        {
            Dispatcher.Invoke(() => AppendChatLine(FormatChatLine(x.Channel, x.Username, x.Message)));
            return;
        }

        if (_pipeline == null || _speakerStylesByCharacter.Count == 0) return;
        if (TryHandleVoiceCommand(x.Username, x.Message)) return;
        if (TryHandleReadNameCommand(x.Username, x.Message)) return;
        if (TryHandleCustomChatCommands(x.Username, x.Message, x.Channel)) return;
        if (TryHandleBeatSaberCommand(x.Username, x.Message, x.Channel)) return;

        int speakerId;
        string? characterForSynth;
        if (IsBroadcasterUsername(x.Username))
        {
            var s = SettingsStore.Load();
            var cn = string.IsNullOrWhiteSpace(s.BroadcasterChatVoiceCharacterName)
                ? "四国めたん"
                : s.BroadcasterChatVoiceCharacterName.Trim();
            characterForSynth = cn;
            var sn = string.IsNullOrWhiteSpace(s.BroadcasterChatVoiceStyleName)
                ? "ノーマル"
                : s.BroadcasterChatVoiceStyleName.Trim();
            if (!VoicevoxSpeakerResolver.TryResolveStyleId(_speakerStylesByCharacter, cn, sn, out speakerId,
                    out var err))
            {
                Dispatcher.Invoke(() => AppendChatLine($"[配信者] {err}"));
                return;
            }
        }
        else if (IsChatBotUsername(x.Username))
        {
            var s = SettingsStore.Load();
            var cn = string.IsNullOrWhiteSpace(s.ChatBotVoiceCharacterName)
                ? "四国めたん"
                : s.ChatBotVoiceCharacterName.Trim();
            characterForSynth = cn;
            var sn = string.IsNullOrWhiteSpace(s.ChatBotVoiceStyleName)
                ? "ノーマル"
                : s.ChatBotVoiceStyleName.Trim();
            if (!VoicevoxSpeakerResolver.TryResolveStyleId(_speakerStylesByCharacter, cn, sn, out speakerId,
                    out var err))
            {
                Dispatcher.Invoke(() => AppendChatLine($"[ボット] {err}"));
                return;
            }
        }
        else
        {
            speakerId = _userSpeakers.GetSpeakerStyleId(x.Username, _speakerStylesByCharacter);
            characterForSynth = _userSpeakers.GetCharacterName(x.Username, _speakerStylesByCharacter);
        }

        var speechSettings = SettingsStore.Load();
        var usernameForSynth = IsChatBotUsername(x.Username) ? null : x.Username;
        _pipeline.EnqueueComment(BuildChatSpeechText(x.Username, x.DisplayName, x.Message, speechSettings), speakerId,
            characterForSynth, usernameForSynth);
        Dispatcher.Invoke(() => AppendChatLine(FormatChatLine(x.Channel, x.Username, x.Message)));
    }

    private static string FormatChatLine(string? channel, string username, string message)
    {
        var prefix = string.IsNullOrEmpty(channel) ? "" : $"[#{channel}] ";
        return $"{prefix}<{username}> {message}";
    }

    private static string BuildChatSpeechText(string username, string? displayName, string message, AppSettings s)
    {
        if (!s.ReadChatUsernameAloud)
            return message;
        var label = UserNameReadingStore.GetLabel(username, displayName);
        var tpl = string.IsNullOrWhiteSpace(s.UsernameSpeechTemplate)
            ? "{UserName} さん、"
            : s.UsernameSpeechTemplate.Trim();
        var prefix = tpl.Replace("{UserName}", label, StringComparison.Ordinal);
        return prefix + message;
    }

    /// <summary>合成パラメータのマージ用。配信者・ボットはオプションのキャラ名、視聴者は割当キャラ名。</summary>
    private string GetCharacterNameForSynthMerge(string username)
    {
        if (IsBroadcasterUsername(username))
        {
            var s = SettingsStore.Load();
            return string.IsNullOrWhiteSpace(s.BroadcasterChatVoiceCharacterName)
                ? "四国めたん"
                : s.BroadcasterChatVoiceCharacterName.Trim();
        }

        if (IsChatBotUsername(username))
        {
            var s = SettingsStore.Load();
            return string.IsNullOrWhiteSpace(s.ChatBotVoiceCharacterName)
                ? "四国めたん"
                : s.ChatBotVoiceCharacterName.Trim();
        }

        return _userSpeakers.GetCharacterName(username, _speakerStylesByCharacter);
    }

    private void HandleVoiceSynthCommand(string username, VoiceSynthSubKind kind, double? value, bool invalidNumber,
        bool dryRun = false)
    {
        if (_speakerStylesByCharacter.Count == 0)
        {
            AppendChatLineMirrorTwitch($"[{username}] VOICEVOX が未準備です。");
            return;
        }

        if (invalidNumber)
        {
            AppendChatLineMirrorTwitch($"[{username}] 数値として解釈できませんでした。");
            return;
        }

        string TagIfDry(string line) => dryRun ? $"{line} ※試行のみ（保存されません）" : line;

        switch (kind)
        {
            case VoiceSynthSubKind.Speed:
                if (!value.HasValue)
                {
                    AppendChatLineMirrorTwitch(
                        $"[{username}] 使い方: !voice speed 1.2（話速 0.5〜2。!style / !よみ も同じ）");
                    return;
                }

                if (!dryRun)
                    UserVoiceSynthStore.SetSpeed(username, value.Value);
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 話速を {value.Value:0.##} にしました（個人設定。オプションのキャラ設定より優先）。"));
                return;

            case VoiceSynthSubKind.Pitch:
                if (!value.HasValue)
                {
                    AppendChatLineMirrorTwitch(
                        $"[{username}] 使い方: !voice pitch 0.05（音高 -0.15〜0.15）");
                    return;
                }

                if (!dryRun)
                    UserVoiceSynthStore.SetPitch(username, value.Value);
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 音高を {value.Value:0.##} にしました（個人設定。オプションのキャラ設定より優先）。"));
                return;

            case VoiceSynthSubKind.Intonation:
                if (!value.HasValue)
                {
                    AppendChatLineMirrorTwitch(
                        $"[{username}] 使い方: !voice intonation 1.2（抑揚 0〜2）");
                    return;
                }

                if (!dryRun)
                    UserVoiceSynthStore.SetIntonation(username, value.Value);
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 抑揚を {value.Value:0.##} にしました（個人設定。オプションのキャラ設定より優先）。"));
                return;

            case VoiceSynthSubKind.Volume:
                if (!value.HasValue)
                {
                    AppendChatLineMirrorTwitch(
                        $"[{username}] 使い方: !voice volume 1.2（音量 0〜2）");
                    return;
                }

                if (!dryRun)
                    UserVoiceSynthStore.SetVolume(username, value.Value);
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 音量を {value.Value:0.##} にしました（個人設定。オプションのキャラ設定より優先）。"));
                return;

            case VoiceSynthSubKind.Reset:
                if (!dryRun)
                    UserVoiceSynthStore.Clear(username);
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 個人の合成パラメータをリセットしました（キャラのオプション設定のみ反映）。"));
                return;

            case VoiceSynthSubKind.Show:
            {
                var s = SettingsStore.Load();
                var cn = GetCharacterNameForSynthMerge(username);
                var m = VoiceCharacterSynthSettings.GetMergedForSynth(s, cn, username);
                var sp = m?.SpeedScale ?? VoiceCharacterSynthDefaults.SpeedScale;
                var pi = m?.PitchScale ?? VoiceCharacterSynthDefaults.PitchScale;
                var iu = m?.IntonationScale ?? VoiceCharacterSynthDefaults.IntonationScale;
                var vo = m?.VolumeScale ?? VoiceCharacterSynthDefaults.VolumeScale;
                AppendChatLineMirrorTwitch(TagIfDry(
                    $"[{username}] 話速{sp:0.##} 音高{pi:0.##} 抑揚{iu:0.##} 音量{vo:0.##}（キャラのオプションと個人の上書きを反映した値）"));
                return;
            }

            default:
                return;
        }
    }

    private bool TryHandleVoiceCommand(string username, string message)
    {
        if (!VoiceCommandParser.TryParse(message, out var arg, out var kind)) return false;

        if (kind == VoiceCommandKind.Voice &&
            VoiceSynthChatCommandParser.TryParse(arg, out var synthKind, out var synthValue, out var synthBadNum))
        {
            if (IsChatBotUsername(username))
            {
                Dispatcher.Invoke(() =>
                    AppendChatLineMirrorTwitch(
                        "[ボット] 合成パラメータはチャットから変更できません（オプションの「チャットボットの声」で固定しています）。"));
                return true;
            }

            Dispatcher.Invoke(() =>
                HandleVoiceSynthCommand(username, synthKind, synthValue, synthBadNum,
                    IsBroadcasterUsername(username)));
            return true;
        }

        if (IsChatBotUsername(username))
        {
            Dispatcher.Invoke(() =>
                AppendChatLineMirrorTwitch(
                    "[ボット] 読み上げ声は「ツール」→「オプション」→「Twitch」→「チャットボットの声」で固定しています（!voice / !style は使えません）。"));
            return true;
        }

        var dryRun = IsBroadcasterUsername(username);

        Dispatcher.Invoke(() =>
        {
            if (arg == null || arg.Equals("help", StringComparison.OrdinalIgnoreCase) || arg.Trim() == "?")
            {
                if (dryRun)
                {
                    AppendChatLineMirrorTwitch(
                        "[配信者] チャットの応答は視聴者と同じ形式で試せます（試行のため保存されません。配信の声はオプションの「Twitch」で選びます）。");
                }

                AppendVoiceCommandHelp();
                return;
            }

            var firstToken = arg.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (firstToken.Equals("list", StringComparison.OrdinalIgnoreCase) || firstToken == "一覧")
            {
                if (kind == VoiceCommandKind.Voice)
                {
                    foreach (var line in _userSpeakers.GetCharacterListLines(_speakerStylesByCharacter))
                        AppendChatLineMirrorTwitch($"[{username}] {line}");
                }
                else
                {
                    var styleLines = _userSpeakers.GetStyleListLines(username, _speakerStylesByCharacter, dryRun);
                    if (dryRun && styleLines.Count == 0)
                    {
                        AppendChatLineMirrorTwitch(
                            $"[{username}] 視聴者としての声の割当がまだないため、!style list は空です。読み上げが走ると割当が作られます。");
                    }
                    else
                    {
                        foreach (var line in styleLines)
                            AppendChatLineMirrorTwitch($"[{username}] {line}");
                    }
                }

                return;
            }

            static string TagOk(bool dr) => dr ? " ※試行のみ（保存されません）" : "";

            if (kind == VoiceCommandKind.Voice)
            {
                if (_userSpeakers.TrySetCharacter(username, arg, _speakerStylesByCharacter, dryRun, out var msg))
                    AppendChatLineMirrorTwitch($"[{username}] {msg}{TagOk(dryRun)}");
                else
                    AppendChatLineMirrorTwitch($"[{username}] {msg}");
            }
            else
            {
                if (_userSpeakers.TrySetStyle(username, arg, _speakerStylesByCharacter, dryRun, out var msg))
                    AppendChatLineMirrorTwitch($"[{username}] {msg}{TagOk(dryRun)}");
                else
                    AppendChatLineMirrorTwitch($"[{username}] {msg}");
            }
        });
        return true;
    }

    private void AppendVoiceCommandHelp()
    {
        foreach (var line in ChatCommandsHelpContent.VoiceCommandHelpBodyLines)
            AppendChatLineMirrorTwitch(ChatCommandsHelpContent.MirrorLinePrefix + line);
        foreach (var line in ChatCommandsHelpContent.ReadNameCommandHelpBodyLines)
            AppendChatLineMirrorTwitch(ChatCommandsHelpContent.MirrorLinePrefix + line);
    }

    private void AppendReadNameCommandHelp()
    {
        foreach (var line in ChatCommandsHelpContent.ReadNameCommandHelpBodyLines)
            AppendChatLineMirrorTwitch(ChatCommandsHelpContent.MirrorLinePrefix + line);
    }

    private bool TryHandleReadNameCommand(string username, string message)
    {
        if (!ReadNameCommandParser.TryParse(message, out var arg)) return false;

        if (IsChatBotUsername(username))
        {
            Dispatcher.Invoke(() =>
                AppendChatLineMirrorTwitch(
                    "[ボット] 読み上げる名前の変更はできません（チャットボット用アカウントです）。"));
            return true;
        }

        Dispatcher.Invoke(() =>
        {
            if (arg == null || arg.Equals("help", StringComparison.OrdinalIgnoreCase) || arg.Trim() == "?")
            {
                AppendReadNameCommandHelp();
                return;
            }

            var firstToken = arg.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (firstToken.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                firstToken == "リセット" || firstToken == "クリア")
            {
                UserNameReadingStore.ClearReading(username);
                AppendChatLineMirrorTwitch($"[{username}] 読み上げる名前を Twitch のログイン名に戻しました。");
                return;
            }

            var reading = arg.Trim();
            if (reading.Length > UserNameReadingStore.MaxReadingLength)
            {
                AppendChatLineMirrorTwitch(
                    $"[{username}] 読みは {UserNameReadingStore.MaxReadingLength} 文字以内にしてください。");
                return;
            }

            UserNameReadingStore.SetReading(username, reading);
            AppendChatLineMirrorTwitch($"[{username}] 読み上げる名前を「{reading}」に設定しました。");
        });
        return true;
    }

    /// <summary>定型応答（カスタム・!bsr）を「定型チャットコマンド」で選んだ話者・スタイルで読み上げ。</summary>
    private bool TryEnqueueSpecialVoiceResponse(string text, out string? error)
    {
        error = null;
        if (_pipeline == null || _speakerStylesByCharacter.Count == 0)
        {
            error = "VOICEVOX が未準備です。";
            return false;
        }

        var s = SettingsStore.Load();
        var charName = string.IsNullOrWhiteSpace(s.FixedResponseVoiceCharacterName)
            ? "四国めたん"
            : s.FixedResponseVoiceCharacterName.Trim();
        var styleName = string.IsNullOrWhiteSpace(s.FixedResponseVoiceStyleName)
            ? "ノーマル"
            : s.FixedResponseVoiceStyleName.Trim();

        if (!VoicevoxSpeakerResolver.TryResolveStyleId(_speakerStylesByCharacter, charName, styleName,
                out var speakerId, out var err))
        {
            error = err;
            return false;
        }

        _pipeline.EnqueueComment(text, speakerId, charName, usernameForSynthParams: null);
        return true;
    }

    private bool TryHandleCustomChatCommands(string username, string message, string? channel)
    {
        if (_customCommandsSorted.Count == 0) return false;

        var t = message.TrimStart();
        foreach (var entry in _customCommandsSorted)
        {
            var trig = entry.CommandTrigger;
            if (trig.Length == 0) continue;
            if (!t.StartsWith(trig, StringComparison.OrdinalIgnoreCase)) continue;

            if (!TryEnqueueSpecialVoiceResponse(entry.ResponseText, out var err))
            {
                Dispatcher.Invoke(() => AppendChatLine($"[カスタム] {err}"));
                return true;
            }

            Dispatcher.Invoke(() => AppendChatLine(FormatChatLine(channel, username, message)));
            return true;
        }

        return false;
    }

    /// <summary>!bsr: 定型文のみ読み上げ（話者は設定の「定型チャットコマンド」）。</summary>
    private bool TryHandleBeatSaberCommand(string username, string message, string? channel)
    {
        if (!BeatSaberCommandParser.IsBsrCommand(message)) return false;

        var s = SettingsStore.Load();
        var text = string.IsNullOrWhiteSpace(s.BeatSaberBsrResponseText)
            ? "リクエストありがとうございます"
            : s.BeatSaberBsrResponseText.Trim();

        if (!TryEnqueueSpecialVoiceResponse(text, out var err))
        {
            Dispatcher.Invoke(() => AppendChatLine($"[BeatSaber] {err}"));
            return true;
        }

        Dispatcher.Invoke(() => AppendChatLine(FormatChatLine(channel, username, message)));
        return true;
    }

    private void OnTwitchStatus(object? sender, string msg) =>
        Dispatcher.Invoke(() => AppendChatLine($"[Twitch] {msg}"));

    private async Task TryRefreshOAuthIfNeededAsync()
    {
        if (string.IsNullOrWhiteSpace(_refreshToken)) return;

        var s = SettingsStore.Load();
        var cid = s.ClientId.Trim();
        var secret = s.ClientSecret.Trim();
        if (string.IsNullOrEmpty(cid) || string.IsNullOrEmpty(secret)) return;

        if (_oauthAccessExpiresAtUtc.HasValue && _oauthAccessExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(60))
            return;

        try
        {
            var r = await _oauthLogin.RefreshAccessTokenAsync(cid, secret, _refreshToken).ConfigureAwait(true);
            s.OAuthToken = r.AccessToken;
            if (!string.IsNullOrEmpty(r.RefreshToken))
                s.RefreshToken = r.RefreshToken;
            s.OAuthAccessExpiresAtUtc = r.AccessTokenExpiresAtUtc;
            SettingsStore.Save(s);
            SyncRefreshStateFromStore(s);
            AppendChatLine("OAuth access_token をリフレッシュしました（期限更新）。");
        }
        catch (Exception ex)
        {
            AppendChatLine($"OAuth リフレッシュ失敗: {ex.Message}（保存済みトークンで接続を試みます）");
        }
    }

    private void AddChannelRow_Click(object sender, RoutedEventArgs e)
    {
        _channelRows.Add(new ChannelRow(() => ScheduleDebouncedSave()));
        ScheduleDebouncedSave();
    }

    private async void RemoveChannelRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChannelRow row }) return;
        var ch = TwitchChannelParser.ParseToLoginName(row.Text.Trim());
        if (row.IsConnected && !string.IsNullOrEmpty(ch))
        {
            row.IsBusy = true;
            try
            {
                await _twitch.LeaveChannelAsync(ch).ConfigureAwait(true);
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                row.IsBusy = false;
            }

            row.IsConnected = false;
            FillSendChannelCombo();
            UpdateChatSendUi();
        }

        if (_channelRows.Count <= 1)
        {
            row.Text = "";
            row.IsConnected = false;
            ScheduleDebouncedSave();
            return;
        }

        _channelRows.Remove(row);
        ScheduleDebouncedSave();
    }

    private void FillSendChannelCombo()
    {
        SendChannelCombo.Items.Clear();
        foreach (var row in _channelRows)
        {
            if (!row.IsConnected) continue;
            var n = TwitchChannelParser.ParseToLoginName(row.Text.Trim());
            if (!string.IsNullOrEmpty(n))
                SendChannelCombo.Items.Add(n);
        }

        if (SendChannelCombo.Items.Count > 0)
            SendChannelCombo.SelectedIndex = 0;
    }

    private void UpdateChatSendUi()
    {
        var any = _channelRows.Any(r => r.IsConnected);
        SendChatBtn.IsEnabled = any;
        ChatSendBox.IsEnabled = any;
        SendChannelCombo.IsEnabled = any;
    }

    private async void ConnectChannelRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChannelRow row }) return;
        var ch = TwitchChannelParser.ParseToLoginName(row.Text.Trim());
        if (string.IsNullOrEmpty(ch))
        {
            MessageBox.Show("チャンネル名または URL を入力してください。", "チャンネル", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var s = SettingsStore.Load();
        var login = s.TwitchLogin.Trim();
        var oauth = s.OAuthToken;
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(oauth))
        {
            MessageBox.Show(
                "ログイン名・OAuth を「ツール」→「オプション」で入力してください。",
                "入力不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_speakerStylesByCharacter.Count == 0)
        {
            MessageBox.Show("先に「ツール」→「オプション」→「全般」で VOICEVOX から話者を取得できているか確認してください。", "VOICEVOX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveChannelOnly();
        row.IsBusy = true;
        try
        {
            await TryRefreshOAuthIfNeededAsync().ConfigureAwait(true);
            s = SettingsStore.Load();
            oauth = s.OAuthToken;
            await _twitch.JoinChannelAsync(login, oauth, ch).ConfigureAwait(true);
            row.IsConnected = true;
            FillSendChannelCombo();
            UpdateChatSendUi();
            AppendChatLine($"[Twitch] #{ch} のチャットに参加しました。");
            SaveChannelOnly();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "接続失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async void DisconnectChannelRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChannelRow row }) return;
        var ch = TwitchChannelParser.ParseToLoginName(row.Text.Trim());
        if (string.IsNullOrEmpty(ch))
        {
            row.IsConnected = false;
            FillSendChannelCombo();
            UpdateChatSendUi();
            return;
        }

        row.IsBusy = true;
        try
        {
            await _twitch.LeaveChannelAsync(ch).ConfigureAwait(true);
            row.IsConnected = false;
            FillSendChannelCombo();
            UpdateChatSendUi();
            AppendChatLine($"[Twitch] #{ch} のチャットから退出しました。");
        }
        catch (Exception ex)
        {
            AppendChatLine($"[Twitch] 切断エラー: {ex.Message}");
            row.IsConnected = _twitch.IsChannelJoined(ch);
            FillSendChannelCombo();
            UpdateChatSendUi();
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async void DisconnectAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _twitch.DisconnectAsync().ConfigureAwait(true);
            foreach (var r in _channelRows)
                r.IsConnected = false;
            SendChannelCombo.Items.Clear();
            UpdateChatSendUi();
            SaveChannelOnly();
        }
        catch (Exception ex)
        {
            AppendChatLine($"[Twitch] 切断エラー: {ex.Message}");
        }
    }

    private async void SendChat_Click(object sender, RoutedEventArgs e) => await SendChatAsync().ConfigureAwait(true);

    private void ChatSendBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        var text = ChatSendBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var targetCh = GetSendTargetChannelName();
        var (ok, err) = await _twitch.SendChatMessageAsync(text, targetCh).ConfigureAwait(true);
        if (!ok)
        {
            AppendChatLine(FormatTwitchSendFailure(err));
            return;
        }

        ChatSendBox.Clear();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveChannelOnly();
        _twitch.MessageReceived -= OnTwitchMessage;
        _twitch.StatusChanged -= OnTwitchStatus;
        _ = _twitch.DisconnectAsync();
        _twitch.Dispose();
        _pipeline?.Dispose();
        _voicevox?.Dispose();
    }
}
