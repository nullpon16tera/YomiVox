using System.Text.Json.Nodes;

namespace YomiVox.Services;

/// <summary>audio_query の JSON に対し、キャラごとのスケールを上書きする。</summary>
public static class VoicevoxAudioQueryMerger
{
    public static string ApplyOverrides(string audioQueryJson, VoiceCharacterSynthEntry? entry)
    {
        if (entry == null || !entry.HasAnyOverride)
            return audioQueryJson;

        JsonObject root;
        try
        {
            var node = JsonNode.Parse(audioQueryJson);
            root = node as JsonObject ?? throw new InvalidOperationException("audio_query がオブジェクトではありません。");
        }
        catch
        {
            return audioQueryJson;
        }

        if (entry.SpeedScale.HasValue)
            root["speedScale"] = JsonValue.Create(entry.SpeedScale.Value);
        if (entry.PitchScale.HasValue)
            root["pitchScale"] = JsonValue.Create(entry.PitchScale.Value);
        if (entry.IntonationScale.HasValue)
            root["intonationScale"] = JsonValue.Create(entry.IntonationScale.Value);
        if (entry.VolumeScale.HasValue)
            root["volumeScale"] = JsonValue.Create(entry.VolumeScale.Value);

        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
