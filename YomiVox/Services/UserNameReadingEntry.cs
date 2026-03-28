namespace YomiVox.Services;

/// <summary>チャット読み上げで使う「ログイン名 → 読み」の対応（viewer_settings.json に保存）。</summary>
public sealed class UserNameReadingEntry
{
    /// <summary>小文字に正規化した Twitch ログイン名。</summary>
    public string Login { get; set; } = "";

    /// <summary>読み上げテキスト（カナなど）。</summary>
    public string Reading { get; set; } = "";
}
