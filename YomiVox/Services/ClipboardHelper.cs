using System.Runtime.InteropServices;
using System.Windows;

namespace YomiVox.Services;

/// <summary>CLIPBRD_E_CANT_OPEN 等で失敗しやすい Windows クリップボードを、軽いリトライで設定する。</summary>
public static class ClipboardHelper
{
    private const int HresultClipbrdECantOpen = unchecked((int)0x800401D0);

    /// <summary>UI スレッドから呼び出すこと。</summary>
    public static async Task<(bool ok, string? error)> TrySetTextAsync(string text,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 0: SetText / 1: SetDataObject（ロックが短い）/ 2: SetText 再試行
                if (attempt == 0)
                    Clipboard.SetText(text);
                else if (attempt == 1)
                    Clipboard.SetDataObject(text, copy: false);
                else
                    Clipboard.SetText(text);

                return (true, null);
            }
            catch (Exception ex) when (IsClipbrdCantOpen(ex))
            {
                if (attempt == 2)
                    break;
                await Task.Delay(20 + attempt * 25, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        return (false,
            "クリップボードを開けませんでした。他のアプリがクリップボードを使用中の可能性があります。少し待ってから再度お試しください。");
    }

    private static bool IsClipbrdCantOpen(Exception ex)
    {
        if (ex is COMException com && com.HResult == HresultClipbrdECantOpen) return true;
        if (ex is ExternalException ext && ext.HResult == HresultClipbrdECantOpen) return true;
        return false;
    }
}
