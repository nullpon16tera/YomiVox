namespace YomiVox.Services;

/// <summary>話者キャラごとの VOICEVOX 合成パラメータ（未設定の項目はエンジン既定のまま）。</summary>
public sealed class VoiceCharacterSynthEntry
{
    public string CharacterName { get; set; } = "";

    /// <summary>話速。未設定なら既定。</summary>
    public double? SpeedScale { get; set; }

    /// <summary>音高。未設定なら既定。</summary>
    public double? PitchScale { get; set; }

    /// <summary>抑揚。未設定なら既定。</summary>
    public double? IntonationScale { get; set; }

    /// <summary>音量。未設定なら既定。</summary>
    public double? VolumeScale { get; set; }

    public bool HasAnyOverride =>
        SpeedScale.HasValue || PitchScale.HasValue || IntonationScale.HasValue || VolumeScale.HasValue;

    public static VoiceCharacterSynthEntry ForCharacter(string characterName) =>
        new() { CharacterName = characterName };
}
