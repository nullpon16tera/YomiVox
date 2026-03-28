namespace YomiVox.Services;

/// <summary>チャットで設定したユーザー別の合成パラメータ（viewer_settings.json に保存）。</summary>
public static class UserVoiceSynthStore
{
    public static void SetSpeed(string twitchLogin, double value)
    {
        value = VoiceCharacterSynthClamp.ClampSpeed(value);
        Upsert(twitchLogin, e => e.SpeedScale = value);
    }

    public static void SetPitch(string twitchLogin, double value)
    {
        value = VoiceCharacterSynthClamp.ClampPitch(value);
        Upsert(twitchLogin, e => e.PitchScale = value);
    }

    public static void SetIntonation(string twitchLogin, double value)
    {
        value = VoiceCharacterSynthClamp.ClampIntonation(value);
        Upsert(twitchLogin, e => e.IntonationScale = value);
    }

    public static void SetVolume(string twitchLogin, double value)
    {
        value = VoiceCharacterSynthClamp.ClampVolume(value);
        Upsert(twitchLogin, e => e.VolumeScale = value);
    }

    public static void Clear(string twitchLogin)
    {
        var v = ViewerSettingsStore.Load();
        v.UserVoiceSynthOverrides ??= new List<UserVoiceSynthEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        v.UserVoiceSynthOverrides.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        ViewerSettingsStore.Save(v);
    }

    private static void Upsert(string twitchLogin, Action<UserVoiceSynthEntry> patch)
    {
        var v = ViewerSettingsStore.Load();
        v.UserVoiceSynthOverrides ??= new List<UserVoiceSynthEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        UserVoiceSynthEntry e;
        if (ViewerSettingsStore.TryGetVoiceSynthEntry(twitchLogin, out var existing) && existing != null)
        {
            e = existing;
        }
        else
        {
            e = new UserVoiceSynthEntry { Login = key };
            v.UserVoiceSynthOverrides.Add(e);
        }

        patch(e);
        if (!e.HasAnyOverride)
            v.UserVoiceSynthOverrides.RemoveAll(x => string.Equals(x.Login, key, StringComparison.OrdinalIgnoreCase));
        ViewerSettingsStore.Save(v);
    }
}
