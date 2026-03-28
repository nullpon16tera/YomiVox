namespace YomiVox.Services;

public static class UserNameReadingStore
{
    public const int MaxReadingLength = 120;

    /// <summary>テンプレートの {UserName} に入れる文字列。!readname 優先、なければ表示名、なければログイン名。</summary>
    public static string GetLabel(string twitchLogin, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(twitchLogin)) return "";
        var key = twitchLogin.Trim();
        if (ViewerSettingsStore.TryGetNameReadingEntry(key, out var e) && e != null)
        {
            if (!string.IsNullOrWhiteSpace(e.Reading))
                return e.Reading.Trim();
            return DefaultDisplayOrLogin(key, displayName);
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
        var v = ViewerSettingsStore.Load();
        v.UserNameReadings ??= new List<UserNameReadingEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        v.UserNameReadings.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        v.UserNameReadings.Add(new UserNameReadingEntry { Login = key, Reading = reading.Trim() });
        ViewerSettingsStore.Save(v);
    }

    public static void ClearReading(string twitchLogin)
    {
        var v = ViewerSettingsStore.Load();
        v.UserNameReadings ??= new List<UserNameReadingEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        v.UserNameReadings.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        ViewerSettingsStore.Save(v);
    }
}
