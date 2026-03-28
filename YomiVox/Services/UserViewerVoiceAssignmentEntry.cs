namespace YomiVox.Services;

/// <summary>視聴者ごとの話者キャラ・スタイル（再起動後も維持）。</summary>
public sealed class UserViewerVoiceAssignmentEntry
{
    /// <summary>Twitch ログイン名（小文字で保存）。</summary>
    public string Login { get; set; } = "";

    public string CharacterName { get; set; } = "";
    public string StyleName { get; set; } = "";
}
