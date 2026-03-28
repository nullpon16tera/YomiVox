namespace YomiVox.Services;

public sealed class AppSettings
{
    /// <summary>視聴用の配信ページ URL（メモ・ブラウザで開く用）。チャット接続とは別。</summary>
    public string StreamUrl { get; set; } = "";

    /// <summary>互換用。新規は <see cref="TwitchChannelUrls"/> のみ使用。</summary>
    public string ChannelUrl { get; set; } = "";

    /// <summary>IRC 接続・監視するチャンネル（ログイン名。複数可）。</summary>
    public List<string> TwitchChannelUrls { get; set; } = new();
    public string TwitchLogin { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string OAuthToken { get; set; } = "";
    /// <summary>ブラウザログイン時に取得。access_token 更新に使用（settings.json に保存）。</summary>
    public string RefreshToken { get; set; } = "";
    /// <summary>access_token の有効期限（UTC）。リフレッシュ判断用。</summary>
    public DateTime? OAuthAccessExpiresAtUtc { get; set; }
    public string VoicevoxUrl { get; set; } = "http://127.0.0.1:50021/";
    public int AudioDeviceNumber { get; set; } = -1;
    /// <summary>読み上げ音量ブースト（%）。100=等倍、250=2.5倍。保存用。</summary>
    public double PlaybackVolumePercent { get; set; } = 250;

    /// <summary>Twitch ログイン名と一致するユーザー（配信者）の通常チャットの話者キャラ。</summary>
    public string BroadcasterChatVoiceCharacterName { get; set; } = "四国めたん";

    /// <summary>配信者の通常チャットのスタイル名。</summary>
    public string BroadcasterChatVoiceStyleName { get; set; } = "ノーマル";

    /// <summary>チャットボットの Twitch ログイン名（カンマ区切り・複数可）。通常チャットの読み上げに固定話者を使う。</summary>
    public string ChatBotUsernamesCsv { get; set; } = "";

    /// <summary>チャットボット用の話者キャラ。</summary>
    public string ChatBotVoiceCharacterName { get; set; } = "四国めたん";

    /// <summary>チャットボット用のスタイル名。</summary>
    public string ChatBotVoiceStyleName { get; set; } = "ノーマル";

    /// <summary>!bsr・カスタムコマンド等「定型読み上げ」の話者キャラ。</summary>
    public string FixedResponseVoiceCharacterName { get; set; } = "四国めたん";

    /// <summary>定型読み上げのスタイル名。</summary>
    public string FixedResponseVoiceStyleName { get; set; } = "ノーマル";

    /// <summary>!bsr 受信時に読み上げる定型文。</summary>
    public string BeatSaberBsrResponseText { get; set; } = "リクエストありがとうございます";

    /// <summary>アプリ組み込みコマンド（!voice 等）以外のユーザー定義コマンド。先頭一致・長いコマンドを優先。</summary>
    public List<CustomChatCommandEntry> CustomChatCommands { get; set; } = new();

    /// <summary>チャット読み上げで、本文の前にユーザー名（テンプレート）を付けるか。</summary>
    public bool ReadChatUsernameAloud { get; set; } = true;

    /// <summary>ユーザー名部分のテンプレート。<c>{UserName}</c> は !readname の登録か Twitch ログイン名に置換。</summary>
    public string UsernameSpeechTemplate { get; set; } = "{UserName} さん、";

    /// <summary>ログイン名ごとの読み上げ表記（!readname / !呼び で登録）。</summary>
    public List<UserNameReadingEntry> UserNameReadings { get; set; } = new();

    /// <summary>話者キャラ名ごとの VOICEVOX 合成パラメータ（未設定の項目はエンジン既定）。</summary>
    public List<VoiceCharacterSynthEntry> VoiceCharacterSynthOverrides { get; set; } = new();

    /// <summary>チャット（!voice speed 等）で保存したユーザー別の合成上書き。</summary>
    public List<UserVoiceSynthEntry> UserVoiceSynthOverrides { get; set; } = new();
}
