using System.Linq;

namespace YomiVox.Services;

/// <summary>チャットで設定したユーザー別の合成パラメータ（settings.json に保存）。</summary>
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
        var s = SettingsStore.Load();
        s.UserVoiceSynthOverrides ??= new List<UserVoiceSynthEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        s.UserVoiceSynthOverrides.RemoveAll(e => string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(s);
    }

    private static void Upsert(string twitchLogin, Action<UserVoiceSynthEntry> patch)
    {
        var s = SettingsStore.Load();
        s.UserVoiceSynthOverrides ??= new List<UserVoiceSynthEntry>();
        var key = twitchLogin.Trim().ToLowerInvariant();
        var idx = s.UserVoiceSynthOverrides.FindIndex(e =>
            string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase));
        UserVoiceSynthEntry e;
        if (idx >= 0)
        {
            e = s.UserVoiceSynthOverrides[idx];
        }
        else
        {
            e = new UserVoiceSynthEntry { Login = key };
            s.UserVoiceSynthOverrides.Add(e);
        }

        patch(e);
        if (!e.HasAnyOverride)
            s.UserVoiceSynthOverrides.RemoveAll(x => string.Equals(x.Login, key, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(s);
    }
}
