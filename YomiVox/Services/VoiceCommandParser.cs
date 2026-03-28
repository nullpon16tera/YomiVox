namespace YomiVox.Services;

/// <summary>!voice / !style / !よみ でスタイル指定するチャットコマンドを解析。</summary>
public static class VoiceCommandParser
{
    /// <summary>話者コマンドなら true。引数なしは null（ヘルプ）、「list」「一覧」はそのまま渡す。</summary>
    public static bool TryParse(string message, out string? argument)
    {
        argument = null;
        var t = message.TrimStart();
        if (t.Length < 2 || t[0] != '!') return false;

        string rest;
        if (t.StartsWith("!voice", StringComparison.OrdinalIgnoreCase))
            rest = t.Length <= 6 ? "" : t[6..].TrimStart();
        else if (t.StartsWith("!style", StringComparison.OrdinalIgnoreCase))
            rest = t.Length <= 6 ? "" : t[6..].TrimStart();
        else if (t.StartsWith("!よみ", StringComparison.Ordinal))
            rest = t.Length <= 4 ? "" : t[4..].TrimStart();
        else
            return false;

        argument = string.IsNullOrEmpty(rest) ? null : rest.Trim();
        return true;
    }
}
