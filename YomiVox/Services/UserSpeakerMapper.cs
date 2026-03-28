using System.Linq;

namespace YomiVox.Services;

/// <summary>
/// ユーザー名に VOICEVOX の「キャラ」を割り当て（アプリ終了まで固定）、
/// スタイルは既定でノーマル。<see cref="TrySetStyle"/> で変更。
/// </summary>
public sealed class UserSpeakerMapper
{
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly Dictionary<string, string> _userToCharacter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _userToStyleName = new(StringComparer.OrdinalIgnoreCase);

    public int GetSpeakerStyleId(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0)
            throw new InvalidOperationException("利用可能な話者がありません。");

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            var character = _userToCharacter[username];
            var styles = stylesByCharacter[character];
            var styleName = _userToStyleName[username];
            return ResolveStyleId(styles, styleName);
        }
    }

    /// <summary>割り当て済みの話者キャラ名（合成パラメータのキー用）。</summary>
    public string GetCharacterName(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0)
            throw new InvalidOperationException("利用可能な話者がありません。");

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            return _userToCharacter[username];
        }
    }

    public bool TrySetStyle(string username, string keyword,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        out string message)
    {
        message = "";
        if (stylesByCharacter.Count == 0) return false;

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            var character = _userToCharacter[username];
            var styles = stylesByCharacter[character];

            if (VoiceStyleKeyword.TryMatchByIndex(styles, keyword, out var byIndex, out var idx))
            {
                _userToStyleName[username] = byIndex.StyleName;
                message = $"読み上げスタイルを「{byIndex.StyleName}」に変更しました（!voice list の {idx} 番目）。";
                return true;
            }

            if (!VoiceStyleKeyword.TryMatch(styles, keyword, out var matched))
            {
                message = $"「{keyword}」に一致するスタイルがありません（!voice list で確認）。";
                return false;
            }

            _userToStyleName[username] = matched.StyleName;
            message = $"読み上げスタイルを「{matched.StyleName}」に変更しました。";
            return true;
        }
    }

    public IReadOnlyList<string> GetStyleListLines(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0) return Array.Empty<string>();

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            var character = _userToCharacter[username];
            var styles = stylesByCharacter[character];
            var current = _userToStyleName[username];
            var sorted = styles.OrderBy(s => s.Id).ToList();
            return sorted.Select((s, i) =>
                    $"{i + 1}. {s.StyleName} (id:{s.Id})" + (s.StyleName == current ? " ←現在" : ""))
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _userToCharacter.Clear();
            _userToStyleName.Clear();
        }
    }

    private void EnsureAssigned(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (_userToCharacter.ContainsKey(username)) return;

        var keys = stylesByCharacter.Keys.ToList();
        var pick = keys[_random.Next(keys.Count)];
        _userToCharacter[username] = pick;
        var styles = stylesByCharacter[pick];
        _userToStyleName[username] = FindDefaultStyleName(styles);
    }

    private static string FindDefaultStyleName(IReadOnlyList<VoicevoxSpeakerStyle> styles)
    {
        foreach (var s in styles)
        {
            if (s.StyleName.Contains("ノーマル", StringComparison.Ordinal))
                return s.StyleName;
        }

        return styles[0].StyleName;
    }

    private static int ResolveStyleId(IReadOnlyList<VoicevoxSpeakerStyle> styles, string styleName)
    {
        foreach (var s in styles)
        {
            if (s.StyleName == styleName)
                return s.Id;
        }

        return styles[0].Id;
    }

}
