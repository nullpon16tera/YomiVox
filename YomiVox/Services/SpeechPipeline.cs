using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace YomiVox.Services;

/// <summary>
/// 合成は最大 <see cref="MaxConcurrentSynthesis"/> 並列、再生はセグメント順を厳守。
/// </summary>
public sealed class SpeechPipeline : IDisposable
{
    public const int MaxConcurrentSynthesis = 3;

    private readonly VoicevoxClient _voicevox;
    private readonly SemaphoreSlim _synthConcurrency = new(MaxConcurrentSynthesis, MaxConcurrentSynthesis);
    private readonly ConcurrentDictionary<int, byte[]> _ready = new();
    private readonly object _segmentLock = new();
    private int _nextSegmentId;
    private int _nextPlayId;
    private volatile bool _disposed;
    private Task? _playbackTask;
    private readonly CancellationTokenSource _cts = new();

    private int _playbackDeviceNumber = -1;
    private volatile float _playbackVolumeMultiplier = 2.5f;

    public SpeechPipeline(VoicevoxClient voicevox)
    {
        _voicevox = voicevox;
        _playbackTask = Task.Run(PlaybackLoopAsync);
    }

    public void SetPlaybackDeviceNumber(int deviceNumber) => _playbackDeviceNumber = deviceNumber;

    /// <summary>等倍 1.0。例: 2.5 で約 2.5 倍（クリップしやすいので 4 まで）。</summary>
    public void SetPlaybackVolumeMultiplier(float linear)
    {
        if (linear < 0.25f) linear = 0.25f;
        if (linear > 4f) linear = 4f;
        _playbackVolumeMultiplier = linear;
    }

    /// <summary>コメント1件分のテキストを分割し、順序付きで合成・再生キューに載せる。</summary>
    public void EnqueueComment(string text, int speakerId, string? characterNameForSynthParams = null,
        string? usernameForSynthParams = null)
    {
        if (_disposed) return;
        var parts = TextSplitter.Split(text);
        if (parts.Count == 0) return;

        int start;
        lock (_segmentLock)
        {
            start = _nextSegmentId;
            _nextSegmentId += parts.Count;
        }

        for (var i = 0; i < parts.Count; i++)
        {
            var segmentIndex = start + i;
            var chunk = parts[i];
            _ = Task.Run(async () =>
            {
                try
                {
                    await _synthConcurrency.WaitAsync(_cts.Token).ConfigureAwait(false);
                    try
                    {
                        var wav = await _voicevox
                            .SynthesizeAsync(chunk, speakerId, _cts.Token, characterNameForSynthParams,
                                usernameForSynthParams)
                            .ConfigureAwait(false);
                        _ready[segmentIndex] = wav;
                    }
                    finally
                    {
                        _synthConcurrency.Release();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    _ready[segmentIndex] = Array.Empty<byte>();
                }
            }, _cts.Token);
        }
    }

    private async Task PlaybackLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (_ready.TryRemove(_nextPlayId, out var wav))
                {
                    try
                    {
                        if (wav.Length > 0)
                            await PlayWavAsync(wav, _cts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        /* 再生失敗でもキューを詰まらせない */
                    }
                    finally
                    {
                        _nextPlayId++;
                    }

                    continue;
                }
                await Task.Delay(15, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PlayWavAsync(byte[] wav, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var vol = _playbackVolumeMultiplier;
            using var ms = new MemoryStream(wav, writable: false);
            using var reader = new WaveFileReader(ms);
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = vol };
            var wave16 = volumeProvider.ToWaveProvider16();
            using var waveOut = new WaveOutEvent { DeviceNumber = _playbackDeviceNumber };
            waveOut.Init(wave16);
            waveOut.Play();
            while (waveOut.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested)
                Thread.Sleep(30);
            waveOut.Stop();
        }, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _playbackTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
        _cts.Dispose();
        _synthConcurrency.Dispose();
    }
}
