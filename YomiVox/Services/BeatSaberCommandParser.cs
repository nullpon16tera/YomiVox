namespace YomiVox.Services;

/// <summary>Beat Saber 連携チャットの定型読み上げ対象（例: SongRequest の !bsr）。</summary>
public static class BeatSaberCommandParser
{
    /// <summary>メッセージが !bsr で始まる（前後空白除く・大文字小文字無視）。</summary>
    public static bool IsBsrCommand(string message)
    {
        var t = message.TrimStart();
        return t.Length >= 4 && t.StartsWith("!bsr", StringComparison.OrdinalIgnoreCase);
    }
}
