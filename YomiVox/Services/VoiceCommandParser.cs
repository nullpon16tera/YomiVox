namespace YomiVox.Services;

/// <summary>!voice は話者キャラ／合成パラメータ、!style と !よみ はスタイル変更用。</summary>
public enum VoiceCommandKind
{
    Voice,
    Style,
    Yomi
}

/// <summary>!voice / !style / !よみ のチャットコマンドを解析。</summary>
public static class VoiceCommandParser
{
    /// <summary>話者コマンドなら true。引数なしは null（ヘルプ）、「list」「一覧」はそのまま渡す。</summary>
    public static bool TryParse(string message, out string? argument, out VoiceCommandKind kind)
    {
        argument = null;
        kind = VoiceCommandKind.Voice;
        var t = message.TrimStart();
        if (t.Length < 2 || t[0] != '!') return false;

        string rest;
        if (t.StartsWith("!voice", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceCommandKind.Voice;
            rest = t.Length <= 6 ? "" : t[6..].TrimStart();
        }
        else if (t.StartsWith("!style", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceCommandKind.Style;
            rest = t.Length <= 6 ? "" : t[6..].TrimStart();
        }
        else if (t.StartsWith("!よみ", StringComparison.Ordinal))
        {
            kind = VoiceCommandKind.Yomi;
            rest = t.Length <= 4 ? "" : t[4..].TrimStart();
        }
        else
            return false;

        argument = string.IsNullOrEmpty(rest) ? null : rest.Trim();
        return true;
    }
}
