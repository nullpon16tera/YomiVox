using System.Runtime.InteropServices;
using System.Windows;

namespace YomiVox.Services;

/// <summary>CLIPBRD_E_CANT_OPEN 等で失敗しやすい Windows クリップボードを、リトライ付きで設定する。</summary>
public static class ClipboardHelper
{
    private const int HresultClipbrdECantOpen = unchecked((int)0x800401D0);

    /// <summary>UI スレッドから呼び出すこと。<see cref="Task.Delay"/> で待つためウィンドウがフリーズしません。</summary>
    public static async Task<(bool ok, string? error)> TrySetTextAsync(string text,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 12;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Clipboard.SetText(text);
                return (true, null);
            }
            catch (COMException ex) when (ex.HResult == HresultClipbrdECantOpen)
            {
                await Task.Delay(40 + attempt * 15, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        return (false,
            "クリップボードを開けませんでした。他のアプリがクリップボードを使用中の可能性があります。少し待ってから再度お試しください。");
    }
}
