namespace YomiVox.Services;

/// <summary>視聴者（チャットユーザー）ごとのデータ専用。viewer_settings.json に保存。</summary>
public sealed class ViewerSettings
{
    /// <summary>ログイン名ごとの読み上げ表記（!readname / !呼び で登録）。</summary>
    public List<UserNameReadingEntry> UserNameReadings { get; set; } = new();

    /// <summary>チャット（!voice speed 等）で保存したユーザー別の合成上書き。</summary>
    public List<UserVoiceSynthEntry> UserVoiceSynthOverrides { get; set; } = new();

    /// <summary>視聴者ごとの話者キャラ・スタイル（!voice で変更した値も含めて保持）。</summary>
    public List<UserViewerVoiceAssignmentEntry> UserViewerVoiceAssignments { get; set; } = new();
}
