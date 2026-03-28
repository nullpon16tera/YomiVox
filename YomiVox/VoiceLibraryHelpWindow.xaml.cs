using System.Windows;
using YomiVox.Services;

namespace YomiVox;

public partial class VoiceLibraryHelpWindow : Window
{
    public VoiceLibraryHelpWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        BodyText.Text = VoiceLibraryHelpContent.BuildCopyPasteDocument();
    }

    private async void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var text = BodyText.Text;
        var (ok, err) = await ClipboardHelper.TrySetTextAsync(text).ConfigureAwait(true);
        if (ok)
        {
            MessageBox.Show(this, "クリップボードにコピーしました。", "音声ライブラリの利用規約・クレジット",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, err ?? "コピーに失敗しました。", "音声ライブラリの利用規約・クレジット",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
