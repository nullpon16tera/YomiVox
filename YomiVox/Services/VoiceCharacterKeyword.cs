using System.Globalization;

namespace YomiVox.Services;

/// <summary>!voice list と同じ並び（style id 若い順のキャラ）での番号指定。</summary>
public static class VoiceCharacterKeyword
{
    public static bool TryMatchByIndex(IReadOnlyList<string> orderedCharacterNames, string keyword,
        out string matchedName, out int oneBasedIndex)
    {
        matchedName = "";
        oneBasedIndex = 0;
        var t = keyword.Trim();
        if (t.Length == 0 || !int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return false;
        if (n < 1 || n > orderedCharacterNames.Count) return false;
        matchedName = orderedCharacterNames[n - 1];
        oneBasedIndex = n;
        return true;
    }
}
