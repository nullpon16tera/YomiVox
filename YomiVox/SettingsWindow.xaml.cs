using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using YomiVox.Services;

namespace YomiVox;

public partial class SettingsWindow : Window
{
    private readonly TwitchOAuthLoginService _oauthLogin = new();
    private readonly MainWindow _main;
    private bool _suppressVoiceCombo;
    private bool _suppressCharacterSynthSlider;
    private readonly Dictionary<string, VoiceCharacterSynthEntry> _characterSynthDraft =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _characterSynthSelected;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        Owner = main;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += SettingsWindow_Closing;
    }

    private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult != true)
            _main.NotifySettingsCancelledFromDialog();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsNavList.SelectedIndex = 0;
        UpdateSettingsNavVisibility();

        var s = SettingsStore.Load();
        ClientIdBox.Text = s.ClientId;
        ClientSecretBox.Password = s.ClientSecret;
        OAuthBox.Password = s.OAuthToken;
        TwitchLoginBox.Text = s.TwitchLogin;
        if (!string.IsNullOrWhiteSpace(s.VoicevoxUrl))
            VoicevoxUrlBox.Text = s.VoicevoxUrl;

        var vp = s.PlaybackVolumePercent;
        if (double.IsNaN(vp) || vp < 50 || vp > 400) vp = 250;
        VolumeSlider.Value = vp;
        VolumePercentLabel.Text = $"{vp:0}%";

        FillAudioDevices();
        SelectAudioDevice(s.AudioDeviceNumber);

        VolumeSlider.ValueChanged += (_, _) =>
        {
            VolumePercentLabel.Text = $"{VolumeSlider.Value:0}%";
        };

        if (_main.TryGetSpeakerCount(out var sc) && sc > 0)
            SpeakersStatus.Text = $"話者 {sc} スタイル（ユーザーにランダム割当）";
        else
            SpeakersStatus.Text = "未取得";

        BeatSaberBsrTextBox.Text = string.IsNullOrWhiteSpace(s.BeatSaberBsrResponseText)
            ? "リクエストありがとうございます"
            : s.BeatSaberBsrResponseText;

        ChatBotUsernamesBox.Text = s.ChatBotUsernamesCsv ?? "";

        ReadUsernameAloudCheck.IsChecked = s.ReadChatUsernameAloud;
        UsernameSpeechTemplateBox.Text = string.IsNullOrWhiteSpace(s.UsernameSpeechTemplate)
            ? "{UserName} さん、"
            : s.UsernameSpeechTemplate;

        RefreshAllVoiceCombosFromSettings();
        RefreshCharacterSynthTab();
    }

    private void SettingsNavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSettingsNavVisibility();

    private void UpdateSettingsNavVisibility()
    {
        var idx = SettingsNavList.SelectedIndex;
        if (idx < 0) idx = 0;
        ScrollGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollTwitch.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        ScrollBeatSaber.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        ScrollCharacter.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAllVoiceCombosFromSettings()
    {
        var s = SettingsStore.Load();
        RefreshVoiceComboPair(
            BroadcasterCharacterCombo, BroadcasterStyleCombo,
            s.BroadcasterChatVoiceCharacterName, s.BroadcasterChatVoiceStyleName);
        RefreshVoiceComboPair(
            FixedCharacterCombo, FixedStyleCombo,
            s.FixedResponseVoiceCharacterName, s.FixedResponseVoiceStyleName);
        RefreshVoiceComboPair(
            ChatBotCharacterCombo, ChatBotStyleCombo,
            s.ChatBotVoiceCharacterName, s.ChatBotVoiceStyleName);
    }

    private void RefreshCharacterSynthTab()
    {
        CommitCharacterSynthSlidersToDraft();
        var s = SettingsStore.Load();
        var fromStore = new Dictionary<string, VoiceCharacterSynthEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in s.VoiceCharacterSynthOverrides ?? Enumerable.Empty<VoiceCharacterSynthEntry>())
        {
            if (string.IsNullOrWhiteSpace(e.CharacterName)) continue;
            fromStore[e.CharacterName.Trim()] = CloneSynthEntry(e);
        }

        var names = _main.GetVoicevoxCharacterNames().ToList();
        var newDraft = new Dictionary<string, VoiceCharacterSynthEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (_characterSynthDraft.TryGetValue(name, out var mem))
                newDraft[name] = mem;
            else if (fromStore.TryGetValue(name, out var st))
                newDraft[name] = st;
            else
                newDraft[name] = VoiceCharacterSynthEntry.ForCharacter(name);
        }

        _characterSynthDraft.Clear();
        foreach (var kv in newDraft)
            _characterSynthDraft[kv.Key] = kv.Value;

        var prevSel = _characterSynthSelected;
        CharacterSynthList.Items.Clear();
        foreach (var name in names)
            CharacterSynthList.Items.Add(name);

        if (CharacterSynthList.Items.Count == 0)
        {
            _characterSynthSelected = null;
            CharacterSynthPanel.IsEnabled = false;
            CharacterSynthTitle.Text = "話者がありません（VOICEVOX を起動して「全般」から再取得）";
            return;
        }

        CharacterSynthPanel.IsEnabled = true;
        if (!string.IsNullOrEmpty(prevSel))
        {
            foreach (var item in CharacterSynthList.Items)
            {
                if (item is string str && str.Equals(prevSel, StringComparison.OrdinalIgnoreCase))
                {
                    CharacterSynthList.SelectedItem = item;
                    return;
                }
            }
        }

        CharacterSynthList.SelectedIndex = 0;
    }

    private void CharacterSynthList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitCharacterSynthSlidersToDraft();
        if (CharacterSynthList.SelectedItem is string sel)
        {
            _characterSynthSelected = sel;
            CharacterSynthPanel.IsEnabled = true;
            CharacterSynthTitle.Text = sel;
            if (_characterSynthDraft.TryGetValue(sel, out var entry))
                LoadSlidersFromEntry(entry);
        }
        else
        {
            _characterSynthSelected = null;
            CharacterSynthPanel.IsEnabled = false;
            CharacterSynthTitle.Text = "キャラを選択してください";
        }
    }

    private void CharacterSynthSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressCharacterSynthSlider) return;
        if (string.IsNullOrEmpty(_characterSynthSelected)) return;
        if (!_characterSynthDraft.TryGetValue(_characterSynthSelected, out var entry)) return;
        SyncEntryFromSliders(entry);
        UpdateSynthLabels();
    }

    private void CharacterSynthResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_characterSynthSelected)) return;
        if (!_characterSynthDraft.TryGetValue(_characterSynthSelected, out var entry)) return;
        entry.SpeedScale = null;
        entry.PitchScale = null;
        entry.IntonationScale = null;
        entry.VolumeScale = null;
        LoadSlidersFromEntry(entry);
    }

    private void CommitCharacterSynthSlidersToDraft()
    {
        if (string.IsNullOrEmpty(_characterSynthSelected)) return;
        if (!_characterSynthDraft.TryGetValue(_characterSynthSelected, out var entry)) return;
        SyncEntryFromSliders(entry);
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 1e-6;

    private void SyncEntryFromSliders(VoiceCharacterSynthEntry e)
    {
        var sp = SpeedSynthSlider.Value;
        var pi = PitchSynthSlider.Value;
        var iu = IntonationSynthSlider.Value;
        var vo = VolumeSynthSlider.Value;
        e.SpeedScale = NearlyEqual(sp, VoiceCharacterSynthDefaults.SpeedScale) ? null : sp;
        e.PitchScale = NearlyEqual(pi, VoiceCharacterSynthDefaults.PitchScale) ? null : pi;
        e.IntonationScale = NearlyEqual(iu, VoiceCharacterSynthDefaults.IntonationScale) ? null : iu;
        e.VolumeScale = NearlyEqual(vo, VoiceCharacterSynthDefaults.VolumeScale) ? null : vo;
    }

    private void LoadSlidersFromEntry(VoiceCharacterSynthEntry e)
    {
        _suppressCharacterSynthSlider = true;
        try
        {
            SpeedSynthSlider.Value = e.SpeedScale ?? VoiceCharacterSynthDefaults.SpeedScale;
            PitchSynthSlider.Value = e.PitchScale ?? VoiceCharacterSynthDefaults.PitchScale;
            IntonationSynthSlider.Value = e.IntonationScale ?? VoiceCharacterSynthDefaults.IntonationScale;
            VolumeSynthSlider.Value = e.VolumeScale ?? VoiceCharacterSynthDefaults.VolumeScale;
            UpdateSynthLabels();
        }
        finally
        {
            _suppressCharacterSynthSlider = false;
        }
    }

    private void UpdateSynthLabels()
    {
        SpeedSynthLabel.Text = $"話速 {SpeedSynthSlider.Value:0.00}";
        PitchSynthLabel.Text = $"音高 {PitchSynthSlider.Value:0.00}";
        IntonationSynthLabel.Text = $"抑揚 {IntonationSynthSlider.Value:0.00}";
        VolumeSynthLabel.Text = $"音量 {VolumeSynthSlider.Value:0.00}";
    }

    private static VoiceCharacterSynthEntry CloneSynthEntry(VoiceCharacterSynthEntry e) =>
        new()
        {
            CharacterName = e.CharacterName,
            SpeedScale = e.SpeedScale,
            PitchScale = e.PitchScale,
            IntonationScale = e.IntonationScale,
            VolumeScale = e.VolumeScale
        };

    private void RefreshVoiceComboPair(
        ComboBox charCombo,
        ComboBox styleCombo,
        string? savedChar,
        string? savedStyle)
    {
        _suppressVoiceCombo = true;
        try
        {
            charCombo.Items.Clear();
            foreach (var name in _main.GetVoicevoxCharacterNames())
                charCombo.Items.Add(name);

            MatchAndSelectCharacter(charCombo, savedChar);
            RefillStyleComboAndSelect(charCombo, styleCombo, savedStyle);
        }
        finally
        {
            _suppressVoiceCombo = false;
        }
    }

    private static void MatchAndSelectCharacter(ComboBox charCombo, string? savedChar)
    {
        if (charCombo.Items.Count == 0)
        {
            charCombo.SelectedIndex = -1;
            return;
        }

        var target = string.IsNullOrWhiteSpace(savedChar) ? "四国めたん" : savedChar.Trim();
        foreach (var item in charCombo.Items)
        {
            if (item is string s && s.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                charCombo.SelectedItem = item;
                return;
            }
        }

        foreach (var item in charCombo.Items)
        {
            if (item is string s &&
                (s.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                 target.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                charCombo.SelectedItem = item;
                return;
            }
        }

        charCombo.SelectedIndex = 0;
    }

    private void RefillStyleComboAndSelect(ComboBox charCombo, ComboBox styleCombo, string? preferredStyleName)
    {
        styleCombo.Items.Clear();
        if (charCombo.SelectedItem is not string charName)
        {
            styleCombo.SelectedIndex = -1;
            return;
        }

        foreach (var style in _main.GetStyleNamesForCharacter(charName))
            styleCombo.Items.Add(style);

        if (styleCombo.Items.Count == 0)
        {
            styleCombo.SelectedIndex = -1;
            return;
        }

        var target = string.IsNullOrWhiteSpace(preferredStyleName) ? "ノーマル" : preferredStyleName.Trim();
        foreach (var item in styleCombo.Items)
        {
            if (item is string s && s.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                styleCombo.SelectedItem = item;
                return;
            }
        }

        foreach (var item in styleCombo.Items)
        {
            if (item is string s && s.Contains(target, StringComparison.Ordinal))
            {
                styleCombo.SelectedItem = item;
                return;
            }
        }

        styleCombo.SelectedIndex = 0;
    }

    private void BroadcasterCharacterCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVoiceCombo) return;
        RefillStyleComboAndSelect(BroadcasterCharacterCombo, BroadcasterStyleCombo, "ノーマル");
    }

    private void FixedCharacterCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVoiceCombo) return;
        RefillStyleComboAndSelect(FixedCharacterCombo, FixedStyleCombo, "ノーマル");
    }

    private void ChatBotCharacterCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVoiceCombo) return;
        RefillStyleComboAndSelect(ChatBotCharacterCombo, ChatBotStyleCombo, "ノーマル");
    }

    private void FillAudioDevices()
    {
        AudioDeviceCombo.SelectionChanged -= AudioDeviceCombo_OnSelectionChanged;
        AudioDeviceCombo.Items.Clear();
        AudioDeviceCombo.Items.Add(new AudioDeviceItem(-1, "既定の再生デバイス"));
        for (var n = 0; n < WaveOut.DeviceCount; n++)
        {
            var caps = WaveOut.GetCapabilities(n);
            AudioDeviceCombo.Items.Add(new AudioDeviceItem(n, $"{n}: {caps.ProductName}"));
        }
        AudioDeviceCombo.SelectedIndex = 0;
        AudioDeviceCombo.SelectionChanged += AudioDeviceCombo_OnSelectionChanged;
    }

    private void AudioDeviceCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /* プレビューはメインのパイプラインに反映（OK 時にも保存） */
    }

    private void SelectAudioDevice(int deviceNumber)
    {
        for (var i = 0; i < AudioDeviceCombo.Items.Count; i++)
        {
            if (AudioDeviceCombo.Items[i] is AudioDeviceItem ad && ad.Number == deviceNumber)
            {
                AudioDeviceCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void SaveToStore()
    {
        CommitCharacterSynthSlidersToDraft();
        var s = SettingsStore.Load();
        s.ClientId = ClientIdBox.Text;
        s.ClientSecret = ClientSecretBox.Password;
        s.OAuthToken = OAuthBox.Password;
        s.TwitchLogin = TwitchLoginBox.Text;
        s.VoicevoxUrl = VoicevoxUrlBox.Text;
        s.AudioDeviceNumber = AudioDeviceCombo.SelectedItem is AudioDeviceItem ad ? ad.Number : -1;
        s.PlaybackVolumePercent = VolumeSlider.Value;
        s.BroadcasterChatVoiceCharacterName = BroadcasterCharacterCombo.SelectedItem as string ?? "";
        s.BroadcasterChatVoiceStyleName = BroadcasterStyleCombo.SelectedItem as string ?? "";
        s.FixedResponseVoiceCharacterName = FixedCharacterCombo.SelectedItem as string ?? "";
        s.FixedResponseVoiceStyleName = FixedStyleCombo.SelectedItem as string ?? "";
        s.ChatBotUsernamesCsv = ChatBotUsernamesBox.Text;
        s.ChatBotVoiceCharacterName = ChatBotCharacterCombo.SelectedItem as string ?? "";
        s.ChatBotVoiceStyleName = ChatBotStyleCombo.SelectedItem as string ?? "";
        s.BeatSaberBsrResponseText = BeatSaberBsrTextBox.Text.Trim();
        s.ReadChatUsernameAloud = ReadUsernameAloudCheck.IsChecked == true;
        s.UsernameSpeechTemplate = UsernameSpeechTemplateBox.Text ?? "";
        if (_characterSynthDraft.Count > 0)
        {
            s.VoiceCharacterSynthOverrides = _characterSynthDraft.Values
                .Where(e => e.HasAnyOverride)
                .Select(CloneSynthEntry)
                .ToList();
        }

        SettingsStore.Save(s);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SaveToStore();
        _main.NotifySettingsAppliedFromDialog();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void RefreshSpeakers_Click(object sender, RoutedEventArgs e)
    {
        await _main.ReloadVoicevoxFromSettingsUiAsync(
            VoicevoxUrlBox.Text,
            AudioDeviceCombo.SelectedItem is AudioDeviceItem ad ? ad.Number : -1,
            VolumeSlider.Value).ConfigureAwait(true);

        var s = SettingsStore.Load();
        _main.SyncRefreshStateFromStore(s);
        if (_main.TryGetSpeakerCount(out var count))
            SpeakersStatus.Text = $"話者 {count} スタイル（ユーザーにランダム割当）";
        else
            SpeakersStatus.Text = "取得失敗 — VOICEVOX を起動して URL を確認してください";

        RefreshAllVoiceCombosFromSettings();
        RefreshCharacterSynthTab();
    }

    private async void BrowserLogin_Click(object sender, RoutedEventArgs e)
    {
        var clientId = ClientIdBox.Text.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            MessageBox.Show("先に Client ID を入力してください。\n（dev.twitch.tv でアプリを作成し、OAuth リダイレクトに " +
                            TwitchOAuthLoginService.RedirectUri + " を登録してください。）",
                "Client ID", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BrowserLoginBtn.IsEnabled = false;
        try
        {
            var secret = ClientSecretBox.Password.Trim();
            var result = await _oauthLogin.LoginAsync(clientId,
                string.IsNullOrEmpty(secret) ? null : secret).ConfigureAwait(true);
            OAuthBox.Password = result.AccessToken;
            var st = SettingsStore.Load();
            st.OAuthToken = result.AccessToken;
            if (!string.IsNullOrEmpty(result.RefreshToken))
                st.RefreshToken = result.RefreshToken;
            st.OAuthAccessExpiresAtUtc = result.AccessTokenExpiresAtUtc;
            SettingsStore.Save(st);

            if (!string.IsNullOrEmpty(result.Login))
                TwitchLoginBox.Text = result.Login;

            _main.SyncRefreshStateFromStore(SettingsStore.Load());
            _main.AppendChatLine(string.IsNullOrEmpty(SettingsStore.Load().RefreshToken)
                ? "ブラウザログインで OAuth を取得しました（リフレッシュトークンなし。長期利用は Client Secret 付きで再ログイン推奨）。"
                : "ブラウザログインで OAuth を取得しました。リフレッシュトークンを保存したので、期限前に自動更新できます。");
        }
        catch (OperationCanceledException ex)
        {
            _main.AppendChatLine($"ログイン中止: {ex.Message}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ブラウザログイン", MessageBoxButton.OK, MessageBoxImage.Error);
            _main.AppendChatLine($"ブラウザログイン失敗: {ex.Message}");
        }
        finally
        {
            BrowserLoginBtn.IsEnabled = true;
        }
    }

    private void OpenTwitchDev_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://dev.twitch.tv/console/apps");

    private void OpenTokenHelp_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show($"ブラウザで開いてください:\n{url}", "ブラウザ", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private sealed record AudioDeviceItem(int Number, string Label)
    {
        public override string ToString() => Label;
    }
}
