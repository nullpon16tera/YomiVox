using System.Text;
using System.Windows;
using YomiVox.Services;

namespace YomiVox;

public partial class SpeakerStylesHelpWindow : Window
{
    public SpeakerStylesHelpWindow(MainWindow main)
    {
        Owner = main;
        InitializeComponent();

        var names = main.GetVoicevoxCharacterNames();
        if (names.Count == 0)
        {
            BodyText.Text =
                "話者がまだ取得できていません。VOICEVOX を起動し、「ツール」→「オプション」→「全般」で「話者を再取得」してください。";
            return;
        }

        var sb = new StringBuilder();
        for (var ci = 0; ci < names.Count; ci++)
        {
            var name = names[ci];
            sb.AppendLine($"{ci + 1}. 【{name}】（話者は style id の若い順）");
            var styles = main.GetStylesOrderedForCharacter(name);
            for (var si = 0; si < styles.Count; si++)
                sb.AppendLine($"   {si + 1}. {styles[si].StyleName} (id:{styles[si].Id})");
            sb.AppendLine();
        }

        BodyText.Text = sb.ToString().TrimEnd();
    }
}
