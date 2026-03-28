using System.Globalization;

namespace YomiVox.Services;

public enum VoiceSynthSubKind
{
    None,
    Speed,
    Pitch,
    Intonation,
    Volume,
    Reset,
    Show
}

/// <summary>!voice speed 1.2 など、合成パラメータ用サブコマンドを解析。</summary>
public static class VoiceSynthChatCommandParser
{
    /// <summary>
    /// 先頭が speed/pitch 等なら true（スタイル名「ノーマル」等は false）。
    /// 数値が要るコマンドで数が欠ける／不正なときは value が null、invalidNumber で区別。
    /// </summary>
    public static bool TryParse(string? arg, out VoiceSynthSubKind kind, out double? value, out bool invalidNumber)
    {
        kind = VoiceSynthSubKind.None;
        value = null;
        invalidNumber = false;
        if (string.IsNullOrWhiteSpace(arg)) return false;
        var parts = arg.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (!TryMapHead(parts[0], out kind)) return false;

        if (kind is VoiceSynthSubKind.Reset or VoiceSynthSubKind.Show)
            return true;

        if (parts.Length < 2)
            return true;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            invalidNumber = true;
            return true;
        }

        value = kind switch
        {
            VoiceSynthSubKind.Speed => VoiceCharacterSynthClamp.ClampSpeed(d),
            VoiceSynthSubKind.Pitch => VoiceCharacterSynthClamp.ClampPitch(d),
            VoiceSynthSubKind.Intonation => VoiceCharacterSynthClamp.ClampIntonation(d),
            VoiceSynthSubKind.Volume => VoiceCharacterSynthClamp.ClampVolume(d),
            _ => d
        };
        return true;
    }

    private static bool TryMapHead(string head, out VoiceSynthSubKind kind)
    {
        kind = VoiceSynthSubKind.None;
        var h = head.Trim();
        if (h.Length == 0) return false;

        if (h.Equals("speed", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("話速", StringComparison.Ordinal) ||
            h.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceSynthSubKind.Speed;
            return true;
        }

        if (h.Equals("pitch", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("音高", StringComparison.Ordinal) ||
            h.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceSynthSubKind.Pitch;
            return true;
        }

        if (h.Equals("intonation", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("抑揚", StringComparison.Ordinal) ||
            h.Equals("i", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceSynthSubKind.Intonation;
            return true;
        }

        if (h.Equals("volume", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("音量", StringComparison.Ordinal) ||
            h.Equals("v", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceSynthSubKind.Volume;
            return true;
        }

        if (h.Equals("reset", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("リセット", StringComparison.Ordinal) ||
            h.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("クリア", StringComparison.Ordinal))
        {
            kind = VoiceSynthSubKind.Reset;
            return true;
        }

        if (h.Equals("show", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("status", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("表示", StringComparison.Ordinal))
        {
            kind = VoiceSynthSubKind.Show;
            return true;
        }

        return false;
    }
}
