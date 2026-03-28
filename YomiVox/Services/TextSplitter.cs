using System.Text;

namespace YomiVox.Services;

/// <summary>
/// VOICEVOX のタイムアウトを避けるため、「、」「。」で分割し、無い場合は最大文字数で切る。
/// </summary>
public static class TextSplitter
{
    public const int MaxChunkChars = 80;

    public static IReadOnlyList<string> Split(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var chunks = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            sb.Append(ch);
            if (ch is '、' or '。' || ch is '，' or '．')
            {
                FlushIfNonEmpty(sb, chunks);
            }
            else if (sb.Length >= MaxChunkChars)
            {
                FlushIfNonEmpty(sb, chunks);
            }
        }
        FlushIfNonEmpty(sb, chunks);
        return chunks;
    }

    private static void FlushIfNonEmpty(StringBuilder sb, List<string> chunks)
    {
        var s = sb.ToString().Trim();
        sb.Clear();
        if (s.Length > 0)
            chunks.Add(s);
    }
}
