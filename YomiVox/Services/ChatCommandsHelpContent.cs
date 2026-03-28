using System.Text;

namespace YomiVox.Services;

/// <summary>!voice / !style / !よみ 系の説明文（!voice 応答・ヘルプウィンドウで共通）。</summary>
public static class ChatCommandsHelpContent
{
    public const string MirrorLinePrefix = "[読み上げ] ";

    /// <summary>!voice help 応答に載せる本文（行単位・プレフィックスなし）。</summary>
    public static IReadOnlyList<string> VoiceCommandHelpBodyLines { get; } =
    [
        "!voice ノーマル / !voice ささやき / !voice あまあま / !voice ツンツン など（同じキャラの別スタイルに切替）",
        "!voice 1 / !voice 2 … !voice list と同じ並びの番号でスタイル指定（1 始まり）",
        "!voice list … 自分に割当のキャラのスタイル一覧　（!style … / !よみ … も同じ）",
        "!voice speed 1.2 / pitch 0.05 / intonation 1.0 / volume 1.0 … 自分だけの合成パラメータ（オプションのキャラ設定より優先）",
        "!voice reset / リセット … 個人の合成パラメータをクリア　!voice show / 表示 … いま効いている合成値",
        "!voice help / !voice ? … チャットコマンド一覧をチャットに表示",
        "英語略: normal n, whisper w, sweet a, tsundere t, sexy s　合成: s p i v（speed pitch intonation volume）"
    ];

    public static IReadOnlyList<string> ReadNameCommandHelpBodyLines { get; } =
    [
        "!readname 〇〇 / !呼び 〇〇 … 読み上げで使う自分の名前（カナなど）",
        "!readname clear / !readname リセット … Twitch のログイン名で読むように戻す",
        "!readname help / !readname ? … このヘルプ"
    ];

    /// <summary>パネル貼り付け用の全文。</summary>
    public static string BuildCopyPasteDocument()
    {
        var sb = new StringBuilder();
        sb.AppendLine("━━ YomiVox：チャットコマンド一覧 ━━");
        sb.AppendLine();
        sb.AppendLine("【読み上げ・声の変更】（!voice / !style / !よみ は同じ）");
        foreach (var line in VoiceCommandHelpBodyLines)
            sb.AppendLine("・" + line);
        sb.AppendLine();
        sb.AppendLine("【ユーザー名の読み方】（「ツール」→「オプション」→「Twitch」で読み上げ有無・テンプレートも設定）");
        foreach (var line in ReadNameCommandHelpBodyLines)
            sb.AppendLine("・" + line);
        sb.AppendLine();
        sb.AppendLine("【補足】");
        sb.AppendLine(
            "・オプションの「キャラクター」タブでは、VOICEVOX の話者キャラごとに話速・音高・抑揚・音量の基準を設定できます。");
        sb.AppendLine(
            "・チャットの !voice speed などはログイン名ごとの上書きで、キャラ別設定より優先されます（!voice reset で個人分だけ消せます）。");
        sb.AppendLine(
            "・配信者の Twitch ログイン名・チャットボット用名は、本アプリの仕様により一部コマンドが使えません（実行時にチャットで案内します）。");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
