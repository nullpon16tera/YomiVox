using System.Globalization;
using System.Linq;

namespace YomiVox.Services;
/// <summary>!voice や定型コマンドでスタイル名を解決するときの共通ロジック。</summary>
public static class VoiceStyleKeyword
{
    /// <summary>!voice list と同じ並び（style id 昇順）での 1 始まりの番号でスタイルを選ぶ。</summary>
    public static bool TryMatchByIndex(IReadOnlyList<VoicevoxSpeakerStyle> styles, string keyword,
        out VoicevoxSpeakerStyle matched, out int oneBasedIndex)
    {
        matched = default!;
        oneBasedIndex = 0;
        var t = keyword.Trim();
        if (t.Length == 0 || !int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return false;
        if (n < 1) return false;
        var sorted = styles.OrderBy(s => s.Id).ToList();
        if (n > sorted.Count) return false;
        matched = sorted[n - 1];
        oneBasedIndex = n;
        return true;
    }

    /// <summary>英語略称などを VOICEVOX のスタイル名に近い検索語へ寄せる。</summary>
    public static string Normalize(string keyword)
    {
        var k = keyword.Trim();
        return k.ToLowerInvariant() switch
        {
            "n" or "normal" => "ノーマル",
            "w" or "whisper" => "ささやき",
            "a" or "sweet" => "あまあま",
            "t" or "tsundere" => "ツンツン",
            "s" or "sexy" => "セクシー",
            _ => k
        };
    }

    public static bool TryMatch(IReadOnlyList<VoicevoxSpeakerStyle> styles, string keyword,
        out VoicevoxSpeakerStyle matched)
    {
        matched = default!;
        keyword = Normalize(keyword);
        if (keyword.Length == 0) return false;

        VoicevoxSpeakerStyle? exact = null;
        VoicevoxSpeakerStyle? contains = null;
        foreach (var s in styles)
        {
            if (s.StyleName.Equals(keyword, StringComparison.Ordinal))
            {
                exact = s;
                break;
            }

            if (s.StyleName.Contains(keyword, StringComparison.Ordinal))
                contains ??= s;
        }

        if (exact != null)
        {
            matched = exact;
            return true;
        }

        if (contains != null)
        {
            matched = contains;
            return true;
        }

        return false;
    }
}
