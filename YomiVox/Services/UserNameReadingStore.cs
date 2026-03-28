using System.Linq;

namespace YomiVox.Services;

public static class UserNameReadingStore
{
    public const int MaxReadingLength = 120;

    /// <summary>テンプレートの {UserName} に入れる文字列。!readname 優先、なければ表示名、なければログイン名。</summary>
    public static string GetLabel(string twitchLogin, string? displayName, AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(twitchLogin)) return "";
        var key = twitchLogin.Trim();
        foreach (var e in s.UserNameReadings ?? Enumerable.Empty<UserNameReadingEntry>())
        {
            if (string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(e.Reading))
                    return e.Reading.Trim();
                return DefaultDisplayOrLogin(key, displayName);
            }
        }

        return DefaultDisplayOrLogin(key, displayName);
    }

    private static string DefaultDisplayOrLogin(string login, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();
        return login;
    }

    public static void SetReading(string twitchLogin, string reading)
    {
        var s = SettingsStore.Load();
        s.UserNameReadings ??= new List<UserNameReadingEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        s.UserNameReadings.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        s.UserNameReadings.Add(new UserNameReadingEntry { Login = key, Reading = reading.Trim() });
        SettingsStore.Save(s);
    }

    public static void ClearReading(string twitchLogin)
    {
        var s = SettingsStore.Load();
        s.UserNameReadings ??= new List<UserNameReadingEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        s.UserNameReadings.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(s);
    }
}
