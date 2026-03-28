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

    /// <summary>チャットで保存したユーザー別合成（viewer_settings。辞書参照）。</summary>
    public static VoiceCharacterSynthEntry? GetForUser(string? twitchLogin)
    {
        if (string.IsNullOrWhiteSpace(twitchLogin)) return null;
        if (!ViewerSettingsStore.TryGetVoiceSynthEntry(twitchLogin, out var e) || e == null) return null;
        var ent = new VoiceCharacterSynthEntry();
        ent.SpeedScale = e.SpeedScale;
        ent.PitchScale = e.PitchScale;
        ent.IntonationScale = e.IntonationScale;
        ent.VolumeScale = e.VolumeScale;
        return ent.HasAnyOverride ? ent : null;
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

    public static VoiceCharacterSynthEntry? GetMergedForSynth(AppSettings app, string? characterName,
        string? twitchLogin)
    {
        var c = GetForCharacter(app, characterName);
        var u = GetForUser(twitchLogin);
        return Merge(c, u);
    }
}
