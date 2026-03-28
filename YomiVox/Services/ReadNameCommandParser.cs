namespace YomiVox.Services;

/// <summary>!readname / !呼び でユーザー名の読みを設定するコマンドを解析。</summary>
public static class ReadNameCommandParser
{
    /// <summary>該当コマンドなら true。引数なしは null（ヘルプ）。</summary>
    public static bool TryParse(string message, out string? argument)
    {
        argument = null;
        var t = message.TrimStart();
        if (t.Length < 2 || t[0] != '!') return false;

        if (t.StartsWith("!readname", StringComparison.OrdinalIgnoreCase))
        {
            var rest = t.Length <= 9 ? "" : t[9..].TrimStart();
            argument = string.IsNullOrEmpty(rest) ? null : rest.Trim();
            return true;
        }

        if (t.StartsWith("!呼び", StringComparison.Ordinal))
        {
            var rest = t.Length <= 3 ? "" : t[3..].TrimStart();
            argument = string.IsNullOrEmpty(rest) ? null : rest.Trim();
            return true;
        }

        return false;
    }
}
