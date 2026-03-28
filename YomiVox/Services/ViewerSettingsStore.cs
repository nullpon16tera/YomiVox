using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YomiVox.Services;

/// <summary>視聴者用設定（viewer_settings.json）。メインの settings.json とは別ファイル。</summary>
public static class ViewerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Sync = new();

    private static string ViewerFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YomiVox",
            "viewer_settings.json");

    private static string MainSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YomiVox", "settings.json");

    private static ViewerSettings? _cached;
    private static DateTime _cachedWriteUtc = DateTime.MinValue;

    private static readonly Dictionary<string, UserNameReadingEntry> ReadingsByLogin =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, UserVoiceSynthEntry> VoiceSynthByLogin =
        new(StringComparer.Ordinal);

    public static ViewerSettings Load()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return _cached!;
        }
    }

    /// <summary>ログイン名（大小無視）で読みエントリを取得。内部は辞書参照。</summary>
    public static bool TryGetNameReadingEntry(string twitchLogin, out UserNameReadingEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(twitchLogin)) return false;
        lock (Sync)
        {
            EnsureLoaded();
            var k = NormalizeKey(twitchLogin);
            if (k == null) return false;
            return ReadingsByLogin.TryGetValue(k, out entry);
        }
    }

    /// <summary>ログイン名（大小無視）で個人合成エントリを取得。内部は辞書参照。</summary>
    public static bool TryGetVoiceSynthEntry(string twitchLogin, out UserVoiceSynthEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(twitchLogin)) return false;
        lock (Sync)
        {
            EnsureLoaded();
            var k = NormalizeKey(twitchLogin);
            if (k == null) return false;
            return VoiceSynthByLogin.TryGetValue(k, out entry);
        }
    }

    public static void Save(ViewerSettings settings)
    {
        lock (Sync)
        {
            try
            {
                var dir = Path.GetDirectoryName(ViewerFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                CoalesceLists(settings);
                _cached = settings;
                RebuildIndexes(_cached);
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(ViewerFilePath, json);
                if (File.Exists(ViewerFilePath))
                    _cachedWriteUtc = File.GetLastWriteTimeUtc(ViewerFilePath);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void EnsureLoaded()
    {
        if (File.Exists(ViewerFilePath))
        {
            var wt = File.GetLastWriteTimeUtc(ViewerFilePath);
            if (_cached != null && wt == _cachedWriteUtc)
                return;

            try
            {
                var json = File.ReadAllText(ViewerFilePath);
                var v = JsonSerializer.Deserialize<ViewerSettings>(json, JsonOptions);
                if (v == null) v = new ViewerSettings();
                CoalesceLists(v);
                _cached = v;
                _cachedWriteUtc = wt;
                RebuildIndexes(_cached);
            }
            catch
            {
                _cached = new ViewerSettings();
                CoalesceLists(_cached);
                _cachedWriteUtc = File.Exists(ViewerFilePath)
                    ? File.GetLastWriteTimeUtc(ViewerFilePath)
                    : DateTime.MinValue;
                RebuildIndexes(_cached);
            }

            return;
        }

        var migrated = TryMigrateFromLegacyMainSettings();
        if (migrated != null)
        {
            CoalesceLists(migrated);
            Save(migrated);
            return;
        }

        _cached ??= new ViewerSettings();
        CoalesceLists(_cached);
        RebuildIndexes(_cached);
    }

    private static string? NormalizeKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().ToLowerInvariant();
    }

    /// <summary>リストの先頭優先（従来の foreach と同じ）。重複ログインは先勝ち。</summary>
    private static void RebuildIndexes(ViewerSettings v)
    {
        ReadingsByLogin.Clear();
        foreach (var e in v.UserNameReadings)
        {
            var k = NormalizeKey(e.Login);
            if (k == null) continue;
            if (!ReadingsByLogin.ContainsKey(k))
                ReadingsByLogin[k] = e;
        }

        VoiceSynthByLogin.Clear();
        foreach (var e in v.UserVoiceSynthOverrides)
        {
            var k = NormalizeKey(e.Login);
            if (k == null) continue;
            if (!VoiceSynthByLogin.ContainsKey(k))
                VoiceSynthByLogin[k] = e;
        }
    }

    private static void CoalesceLists(ViewerSettings v)
    {
        v.UserNameReadings ??= new List<UserNameReadingEntry>();
        v.UserVoiceSynthOverrides ??= new List<UserVoiceSynthEntry>();
        v.UserViewerVoiceAssignments ??= new List<UserViewerVoiceAssignmentEntry>();
    }

    /// <summary>旧 settings.json に含まれていた視聴者向け配列を移し、メイン JSON から削除する。</summary>
    private static ViewerSettings? TryMigrateFromLegacyMainSettings()
    {
        if (!File.Exists(MainSettingsPath)) return null;

        string text;
        try
        {
            text = File.ReadAllText(MainSettingsPath);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text)) return null;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return null;
        }

        if (root == null) return null;

        var viewer = new ViewerSettings();
        var any = false;

        if (TryRemoveDeserializeList(root, new[] { "UserNameReadings", "userNameReadings" },
                out List<UserNameReadingEntry>? unr) && unr != null)
        {
            viewer.UserNameReadings = unr;
            any = true;
        }

        if (TryRemoveDeserializeList(root, new[] { "UserVoiceSynthOverrides", "userVoiceSynthOverrides" },
                out List<UserVoiceSynthEntry>? uvs) && uvs != null)
        {
            viewer.UserVoiceSynthOverrides = uvs;
            any = true;
        }

        if (TryRemoveDeserializeList(root, new[] { "UserViewerVoiceAssignments", "userViewerVoiceAssignments" },
                out List<UserViewerVoiceAssignmentEntry>? uva) && uva != null)
        {
            viewer.UserViewerVoiceAssignments = uva;
            any = true;
        }

        if (!any) return null;

        try
        {
            File.WriteAllText(MainSettingsPath, root.ToJsonString(JsonOptions));
        }
        catch
        {
            /* メインが書けなくても viewer は返す */
        }

        return viewer;
    }

    private static bool TryRemoveDeserializeList<T>(JsonObject root, string[] propertyNames, out List<T>? list)
    {
        list = null;
        JsonNode? node = null;
        foreach (var k in propertyNames)
        {
            if (root.TryGetPropertyValue(k, out var n))
            {
                node = n;
                break;
            }
        }

        if (node == null) return false;

        try
        {
            var json = node.ToJsonString();
            list = JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }
        catch
        {
            return false;
        }

        foreach (var k in propertyNames)
            root.Remove(k);

        return true;
    }
}
