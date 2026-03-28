using System.IO;
using System.Text.Json;

namespace YomiVox.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YomiVox", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            if (s == null) return new AppSettings();
            s.CustomChatCommands ??= new List<CustomChatCommandEntry>();
            s.VoiceCharacterSynthOverrides ??= new List<VoiceCharacterSynthEntry>();
            s.TwitchChannelUrls ??= new List<string>();
            if (s.TwitchChannelUrls.Count == 0 && !string.IsNullOrWhiteSpace(s.ChannelUrl))
                s.TwitchChannelUrls.Add(TwitchChannelParser.ParseToLoginName(s.ChannelUrl));
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            /* ignore */
        }
    }
}
