namespace YomiVox.Services;

/// <summary>設定のカンマ区切りボット名と Twitch ユーザー名の照合。</summary>
public static class ChatBotUsernameList
{
    public static bool ContainsUsername(string? csv, string username)
    {
        if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(username))
            return false;
        var u = username.Trim();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0 && string.Equals(u, part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
