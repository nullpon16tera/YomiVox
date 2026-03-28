namespace YomiVox.Services;

/// <summary>オプション UI・チャットコマンド共通の許容範囲。</summary>
public static class VoiceCharacterSynthClamp
{
    public static double ClampSpeed(double v) => Math.Clamp(v, 0.5, 2.0);
    public static double ClampPitch(double v) => Math.Clamp(v, -0.15, 0.15);
    public static double ClampIntonation(double v) => Math.Clamp(v, 0.0, 2.0);
    public static double ClampVolume(double v) => Math.Clamp(v, 0.0, 2.0);
}
