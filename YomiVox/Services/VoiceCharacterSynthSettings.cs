using System.Linq;

namespace YomiVox.Services;

public static class VoiceCharacterSynthSettings
{
    public static VoiceCharacterSynthEntry? GetForCharacter(AppSettings s, string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return null;
        foreach (var e in s.VoiceCharacterSynthOverrides ?? Enumerable.Empty<VoiceCharacterSynthEntry>())
        {
            if (string.Equals(e.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                return e;
        }

        return null;
    }

    public static VoiceCharacterSynthEntry? GetForUser(AppSettings s, string? twitchLogin)
    {
        if (string.IsNullOrWhiteSpace(twitchLogin)) return null;
        var key = twitchLogin.Trim();
        foreach (var e in s.UserVoiceSynthOverrides ?? Enumerable.Empty<UserVoiceSynthEntry>())
        {
            if (!string.Equals(e.Login, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = new VoiceCharacterSynthEntry();
            v.SpeedScale = e.SpeedScale;
            v.PitchScale = e.PitchScale;
            v.IntonationScale = e.IntonationScale;
            v.VolumeScale = e.VolumeScale;
            return v.HasAnyOverride ? v : null;
        }

        return null;
    }

    /// <summary>キャラ別オプションとユーザー別チャット設定をマージ（ユーザー指定があればその項目を優先）。</summary>
    public static VoiceCharacterSynthEntry? Merge(VoiceCharacterSynthEntry? character, VoiceCharacterSynthEntry? user)
    {
        if (user == null || !user.HasAnyOverride)
            return character;
        if (character == null || !character.HasAnyOverride)
        {
            return new VoiceCharacterSynthEntry
            {
                CharacterName = character?.CharacterName ?? "",
                SpeedScale = user.SpeedScale,
                PitchScale = user.PitchScale,
                IntonationScale = user.IntonationScale,
                VolumeScale = user.VolumeScale
            };
        }

        return new VoiceCharacterSynthEntry
        {
            CharacterName = character.CharacterName,
            SpeedScale = user.SpeedScale ?? character.SpeedScale,
            PitchScale = user.PitchScale ?? character.PitchScale,
            IntonationScale = user.IntonationScale ?? character.IntonationScale,
            VolumeScale = user.VolumeScale ?? character.VolumeScale
        };
    }

    public static VoiceCharacterSynthEntry? GetMergedForSynth(AppSettings s, string? characterName, string? twitchLogin)
    {
        var c = GetForCharacter(s, characterName);
        var u = GetForUser(s, twitchLogin);
        return Merge(c, u);
    }
}
