namespace YomiVox.Services;

/// <summary>設定のキャラ名・スタイル名から VOICEVOX の style id を解決。</summary>
public static class VoicevoxSpeakerResolver
{
    public static bool TryResolveStyleId(
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        string characterQuery,
        string styleKeyword,
        out int styleId,
        out string? error)
    {
        styleId = 0;
        error = null;
        if (stylesByCharacter.Count == 0)
        {
            error = "話者一覧がありません。";
            return false;
        }

        if (!TryFindCharacterKey(stylesByCharacter, characterQuery, out var key, out error))
            return false;

        var styles = stylesByCharacter[key];
        if (!VoiceStyleKeyword.TryMatch(styles, styleKeyword, out var matched))
        {
            error = $"キャラ「{key}」にスタイル「{styleKeyword}」がありません。";
            return false;
        }

        styleId = matched.Id;
        return true;
    }

    private static bool TryFindCharacterKey(
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        string query,
        out string key,
        out string? error)
    {
        key = "";
        error = null;
        query = query.Trim();
        if (query.Length == 0)
        {
            error = "キャラ名が空です。";
            return false;
        }

        foreach (var kv in stylesByCharacter)
        {
            if (kv.Key.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                key = kv.Key;
                return true;
            }
        }

        foreach (var kv in stylesByCharacter)
        {
            if (kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                query.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                key = kv.Key;
                return true;
            }
        }

        error = $"話者一覧にキャラ「{query}」がありません。";
        return false;
    }
}
