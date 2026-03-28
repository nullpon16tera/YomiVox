namespace YomiVox.Services;

/// <summary>Twitch チャンネル URL または名前からログイン名（小文字想定で比較）を取り出す。</summary>
public static class TwitchChannelParser
{
    public static string ParseToLoginName(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw)) return raw;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var u) &&
            u.Host.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            var seg = u.AbsolutePath.Trim('/');
            var slash = seg.IndexOf('/');
            if (slash >= 0) seg = seg[..slash];
            return seg;
        }

        return raw.TrimStart('#');
    }
}
