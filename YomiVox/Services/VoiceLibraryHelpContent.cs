using System.Text;

namespace YomiVox.Services;

/// <summary>音声ライブラリの利用・クレジット表記（ヘルプウィンドウ・コピー用）。</summary>
public static class VoiceLibraryHelpContent
{
    /// <summary>VOICEVOX 公式の「VOICEVOX:…」形式のクレジット行（もち子さんは利用方針により本アプリでは含めない）。</summary>
    public static IReadOnlyList<string> OfficialVoicevoxCreditLines { get; } =
    [
        "VOICEVOX:四国めたん",
        "VOICEVOX:ずんだもん",
        "VOICEVOX:春日部つむぎ",
        "VOICEVOX:波音リツ",
        "VOICEVOX:雨晴はう",
        "VOICEVOX:玄野武宏",
        "VOICEVOX:白上虎太郎",
        "VOICEVOX:青山龍星",
        "VOICEVOX:冥鳴ひまり",
        "VOICEVOX:九州そら",
        "VOICEVOX:剣崎雌雄",
        "VOICEVOX:WhiteCUL",
        "VOICEVOX:後鬼",
        "VOICEVOX:No.7",
        "VOICEVOX:ちび式じい",
        "VOICEVOX:櫻歌ミコ",
        "VOICEVOX:小夜/SAYO",
        "VOICEVOX:ナースロボ＿タイプＴ",
        "VOICEVOX:†聖騎士 紅桜†",
        "VOICEVOX:雀松朱司",
        "VOICEVOX:麒ヶ島宗麟",
        "VOICEVOX:春歌ナナ",
        "VOICEVOX:猫使アル",
        "VOICEVOX:猫使ビィ",
        "VOICEVOX:中国うさぎ",
        "VOICEVOX:栗田まろん",
        "VOICEVOX:あいえるたん",
        "VOICEVOX:満別花丸",
        "VOICEVOX:琴詠ニア",
        "VOICEVOX:Voidoll(CV:丹下桜)",
        "VOICEVOX:ぞん子",
        "VOICEVOX:中部つるぎ",
        "VOICEVOX:離途",
        "VOICEVOX:黒沢冴白",
        "VOICEVOX:ユーレイちゃん(CV:神崎零)",
        "VOICEVOX:東北ずん子",
        "VOICEVOX:東北きりたん",
        "VOICEVOX:東北イタコ",
        "VOICEVOX:あんこもん"
    ];

    /// <summary>配信概要・固定コメント・動画概要欄に貼り付けやすい全文。</summary>
    public static string BuildCopyPasteDocument()
    {
        var sb = new StringBuilder();
        sb.AppendLine("━━ 音声ライブラリのクレジット表記（VOICEVOX）━━");
        sb.AppendLine();
        sb.AppendLine("利用時は「VOICEVOX:話者名」形式でクレジットします（CV 等が付く話者は下記のとおり）。");
        sb.AppendLine("VOICEVOX のバージョンで話者が増える場合は、公式サイトの各利用規約を確認してください。");
        sb.AppendLine();
        sb.AppendLine("VOICEVOX 公式: https://voicevox.hiroshiba.jp/");
        sb.AppendLine("ソフトウェア利用規約: https://voicevox.hiroshiba.jp/term/");
        sb.AppendLine();
        sb.AppendLine("──────── クレジット表記（使う声の行だけ残して貼り付け）────────");
        foreach (var line in OfficialVoicevoxCreditLines)
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("【本アプリについて】");
        sb.AppendLine("音声ライブラリの利用条件により、配信用途に適さない話者「もち子さん」は話者一覧から除外しており、上の一覧にも含めていません。");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
