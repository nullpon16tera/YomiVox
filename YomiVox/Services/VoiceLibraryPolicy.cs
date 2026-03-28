namespace YomiVox.Services;

/// <summary>音声ライブラリ（各話者）の利用条件に合わせ、本アプリで扱わない話者を定義する。</summary>
public static class VoiceLibraryPolicy
{
    /// <summary>
    /// VOICEVOX API の speaker <c>name</c> が、本アプリの話者一覧・合成対象から除外されるか。
    /// </summary>
    public static bool IsExcludedFromApp(string? voicevoxSpeakerName)
    {
        if (string.IsNullOrWhiteSpace(voicevoxSpeakerName)) return false;
        var n = voicevoxSpeakerName.Trim();
        return n.StartsWith("もち子", StringComparison.Ordinal);
    }
}
