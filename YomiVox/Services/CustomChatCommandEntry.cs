namespace YomiVox.Services;

/// <summary>ユーザー定義チャットコマンド（先頭一致）と定型読み上げ文。話者は設定の「定型チャットコマンド」。</summary>
public sealed class CustomChatCommandEntry
{
    public string CommandTrigger { get; set; } = "";
    public string ResponseText { get; set; } = "";
}
