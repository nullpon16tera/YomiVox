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

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BodyText.Text);
            MessageBox.Show(this, "クリップボードにコピーしました。", "チャットコマンド一覧",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "コピーに失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
