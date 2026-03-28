using System.Windows;
using YomiVox.Services;

namespace YomiVox;

public partial class ChatCommandsHelpWindow : Window
{
    public ChatCommandsHelpWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        BodyText.Text = ChatCommandsHelpContent.BuildCopyPasteDocument();
    }

    private async void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var text = BodyText.Text;
        var (ok, err) = await ClipboardHelper.TrySetTextAsync(text).ConfigureAwait(true);
        if (ok)
        {
            MessageBox.Show(this, "クリップボードにコピーしました。", "チャットコマンド一覧",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, err ?? "コピーに失敗しました。", "チャットコマンド一覧",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
