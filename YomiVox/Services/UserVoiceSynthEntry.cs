namespace YomiVox.Services;

/// <summary>Twitch ログイン名ごとの VOICEVOX 合成パラメータ（チャットで上書き）。</summary>
public sealed class UserVoiceSynthEntry
{
    public string Login { get; set; } = "";

    public double? SpeedScale { get; set; }
    public double? PitchScale { get; set; }
    public double? IntonationScale { get; set; }
    public double? VolumeScale { get; set; }

    public bool HasAnyOverride =>
        SpeedScale.HasValue || PitchScale.HasValue || IntonationScale.HasValue || VolumeScale.HasValue;
}
