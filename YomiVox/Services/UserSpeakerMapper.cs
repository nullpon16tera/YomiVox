using System.Linq;

namespace YomiVox.Services;

/// <summary>
/// ユーザー名に VOICEVOX の「キャラ」を割り当て、!voice でキャラ、!style / !よみ でスタイル変更。
/// 割り当ては <see cref="PersistToSettings"/> で viewer_settings.json に保存し、再起動後も維持。
/// </summary>
public sealed class UserSpeakerMapper
{
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly Dictionary<string, string> _userToCharacter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _userToStyleName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>保存済みの割当を読み込む（話者一覧取得後に呼ぶ。無効な行はスキップ）。</summary>
    public void ApplyPersistedAssignments(IReadOnlyList<UserViewerVoiceAssignmentEntry>? entries,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0 || entries == null || entries.Count == 0)
            return;

        lock (_lock)
        {
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Login)) continue;
                var login = NormalizeUser(e.Login);
                if (_userToCharacter.ContainsKey(login)) continue;

                var charName = e.CharacterName?.Trim() ?? "";
                var styleName = e.StyleName?.Trim() ?? "";
                if (string.IsNullOrEmpty(charName)) continue;

                if (!TryFindCharacterKey(charName, stylesByCharacter, out var canonChar)) continue;
                var styles = stylesByCharacter[canonChar];
                if (!TryResolveStyleName(styles, styleName, out var canonStyle))
                    canonStyle = FindDefaultStyleName(styles);

                _userToCharacter[login] = canonChar;
                _userToStyleName[login] = canonStyle;
            }
        }
    }

    /// <summary>現在の割当を viewer_settings.json に書き戻す（起動時の検証後に無効エントリを落とす用途でも可）。</summary>
    public void PersistToSettings()
    {
        List<UserViewerVoiceAssignmentEntry> list;
        lock (_lock)
        {
            list = new List<UserViewerVoiceAssignmentEntry>(_userToCharacter.Count);
            foreach (var kv in _userToCharacter)
            {
                _userToStyleName.TryGetValue(kv.Key, out var st);
                list.Add(new UserViewerVoiceAssignmentEntry
                {
                    Login = kv.Key.ToLowerInvariant(),
                    CharacterName = kv.Value,
                    StyleName = st ?? "ノーマル"
                });
            }
        }

        var v = ViewerSettingsStore.Load();
        v.UserViewerVoiceAssignments = list;
        ViewerSettingsStore.Save(v);
    }

    public int GetSpeakerStyleId(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0)
            throw new InvalidOperationException("利用可能な話者がありません。");

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            var key = NormalizeUser(username);
            var character = _userToCharacter[key];
            var styles = stylesByCharacter[character];
            var styleName = _userToStyleName[key];
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
            return _userToCharacter[NormalizeUser(username)];
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
            var key = NormalizeUser(username);
            var character = _userToCharacter[key];
            var styles = stylesByCharacter[character];

            if (VoiceStyleKeyword.TryMatchByIndex(styles, keyword, out var byIndex, out var idx))
            {
                _userToStyleName[key] = byIndex.StyleName;
                message = $"読み上げスタイルを「{byIndex.StyleName}」に変更しました（!style list の {idx} 番目）。";
                PersistToSettings();
                return true;
            }

            if (!VoiceStyleKeyword.TryMatch(styles, keyword, out var matched))
            {
                message = $"「{keyword}」に一致するスタイルがありません（!style list で確認）。";
                return false;
            }

            _userToStyleName[key] = matched.StyleName;
            message = $"読み上げスタイルを「{matched.StyleName}」に変更しました。";
            PersistToSettings();
            return true;
        }
    }

    /// <summary>!voice list 用。話者キャラ名を style id 若い順に番号付きで。</summary>
    public IReadOnlyList<string> GetCharacterListLines(
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0) return Array.Empty<string>();

        var names = GetOrderedCharacterNames(stylesByCharacter);
        return names.Select((n, i) => $"{i + 1}. {n}").ToList();
    }

    /// <summary>!voice 〇〇 で話者キャラを変更。スタイルは新キャラの既定にリセット。</summary>
    public bool TrySetCharacter(string username, string keyword,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        out string message)
    {
        message = "";
        if (stylesByCharacter.Count == 0)
        {
            message = "話者が取得できていません。VOICEVOX を起動してから「話者を再取得」してください。";
            return false;
        }

        var ordered = GetOrderedCharacterNames(stylesByCharacter);
        var key = NormalizeUser(username);
        var kw = keyword.Trim();

        lock (_lock)
        {
            if (!ResolveCharacterKeyword(kw, stylesByCharacter, ordered, out var canonChar))
            {
                message = $"「{kw}」に一致する話者がありません（!voice list で確認）。";
                return false;
            }

            if (_userToCharacter.TryGetValue(key, out var cur) &&
                cur.Equals(canonChar, StringComparison.OrdinalIgnoreCase))
            {
                message = $"すでに話者は「{canonChar}」です。";
                return true;
            }

            var styles = stylesByCharacter[canonChar];
            var defaultStyle = FindDefaultStyleName(styles);
            _userToCharacter[key] = canonChar;
            _userToStyleName[key] = defaultStyle;
            PersistToSettings();
            message = $"話者を「{canonChar}」に変更しました（スタイルは「{defaultStyle}」に合わせました）。";
            return true;
        }
    }

    private static bool ResolveCharacterKeyword(string kw,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        IReadOnlyList<string> orderedNames, out string canonChar)
    {
        canonChar = "";
        if (VoiceCharacterKeyword.TryMatchByIndex(orderedNames, kw, out var byIdx, out _))
        {
            canonChar = byIdx;
            return true;
        }

        return TryFindCharacterKey(kw, stylesByCharacter, out canonChar);
    }

    private static List<string> GetOrderedCharacterNames(
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        return stylesByCharacter.OrderBy(kv => kv.Value.Min(s => s.Id)).Select(kv => kv.Key).ToList();
    }

    public IReadOnlyList<string> GetStyleListLines(string username,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter)
    {
        if (stylesByCharacter.Count == 0) return Array.Empty<string>();

        lock (_lock)
        {
            EnsureAssigned(username, stylesByCharacter);
            var key = NormalizeUser(username);
            var character = _userToCharacter[key];
            var styles = stylesByCharacter[character];
            var current = _userToStyleName[key];
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
        var key = NormalizeUser(username);
        if (_userToCharacter.ContainsKey(key)) return;

        var keys = stylesByCharacter.Keys.ToList();
        var pick = keys[_random.Next(keys.Count)];
        _userToCharacter[key] = pick;
        var styles = stylesByCharacter[pick];
        _userToStyleName[key] = FindDefaultStyleName(styles);
        PersistToSettings();
    }

    private static string NormalizeUser(string username) => username.Trim().ToLowerInvariant();

    private static bool TryFindCharacterKey(string requested,
        IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>> stylesByCharacter,
        out string canonicalKey)
    {
        foreach (var kv in stylesByCharacter)
        {
            if (kv.Key.Equals(requested, StringComparison.OrdinalIgnoreCase))
            {
                canonicalKey = kv.Key;
                return true;
            }
        }

        foreach (var kv in stylesByCharacter)
        {
            if (kv.Key.Contains(requested, StringComparison.OrdinalIgnoreCase) ||
                requested.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                canonicalKey = kv.Key;
                return true;
            }
        }

        canonicalKey = "";
        return false;
    }

    private static bool TryResolveStyleName(IReadOnlyList<VoicevoxSpeakerStyle> styles, string styleName,
        out string matched)
    {
        matched = "";
        if (string.IsNullOrWhiteSpace(styleName)) return false;
        var t = styleName.Trim();
        foreach (var s in styles)
        {
            if (s.StyleName == t)
            {
                matched = s.StyleName;
                return true;
            }
        }

        foreach (var s in styles)
        {
            if (s.StyleName.Contains(t, StringComparison.Ordinal) || t.Contains(s.StyleName, StringComparison.Ordinal))
            {
                matched = s.StyleName;
                return true;
            }
        }

        return false;
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
