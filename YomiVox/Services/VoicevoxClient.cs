using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace YomiVox.Services;

public sealed class VoicevoxSpeakerInfo
{
    public required string Name { get; init; }
    public required int Id { get; init; }
}

/// <summary>VOICEVOX の 1 キャラの 1 スタイル（合成に使うのは <see cref="Id"/>）。</summary>
public sealed record VoicevoxSpeakerStyle(string CharacterName, string StyleName, int Id);

public sealed class VoicevoxClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string?, string?, VoiceCharacterSynthEntry?>? _getSynthEntryForMerge;

    public VoicevoxClient(string baseUri = "http://127.0.0.1:50021/",
        Func<string?, string?, VoiceCharacterSynthEntry?>? getSynthEntryForMerge = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUri.TrimEnd('/') + "/") };
        _http.Timeout = TimeSpan.FromMinutes(2);
        _getSynthEntryForMerge = getSynthEntryForMerge;
    }

    public void Dispose() => _http.Dispose();

    public async Task<IReadOnlyList<VoicevoxSpeakerInfo>> GetSpeakersAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("speakers", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var list = new List<VoicevoxSpeakerInfo>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "?";
            if (VoiceLibraryPolicy.IsExcludedFromApp(name)) continue;
            foreach (var style in el.GetProperty("styles").EnumerateArray())
            {
                var id = style.GetProperty("id").GetInt32();
                var styleName = style.TryGetProperty("name", out var sn) ? sn.GetString() : null;
                var label = string.IsNullOrEmpty(styleName) ? name : $"{name} ({styleName})";
                list.Add(new VoicevoxSpeakerInfo { Name = label, Id = id });
            }
        }
        return list;
    }

    /// <summary>キャラ名ごとにスタイル一覧（同一キャラはノーマル等をまとめる）。</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<VoicevoxSpeakerStyle>>> GetSpeakerStylesGroupedAsync(
        CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("speakers", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var dict = new Dictionary<string, List<VoicevoxSpeakerStyle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "?";
            if (VoiceLibraryPolicy.IsExcludedFromApp(name)) continue;
            if (!dict.TryGetValue(name, out var list))
            {
                list = new List<VoicevoxSpeakerStyle>();
                dict[name] = list;
            }

            foreach (var style in el.GetProperty("styles").EnumerateArray())
            {
                var id = style.GetProperty("id").GetInt32();
                var styleName = style.TryGetProperty("name", out var sn) ? sn.GetString() ?? "?" : "?";
                list.Add(new VoicevoxSpeakerStyle(name, styleName, id));
            }
        }

        foreach (var list in dict.Values)
            list.Sort((a, b) => a.Id.CompareTo(b.Id));

        return dict
            .OrderBy(kv => kv.Value.Min(s => s.Id))
            .ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<VoicevoxSpeakerStyle>)kv.Value.AsReadOnly(),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<byte[]> SynthesizeAsync(string text, int speakerId, CancellationToken ct = default,
        string? characterNameForSynthParams = null,
        string? usernameForSynthParams = null)
    {
        var encoded = Uri.EscapeDataString(text);
        using var q = await _http
            .PostAsync($"audio_query?text={encoded}&speaker={speakerId}", null, ct)
            .ConfigureAwait(false);
        q.EnsureSuccessStatusCode();
        var queryJson = await q.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        VoiceCharacterSynthEntry? entry = null;
        if (_getSynthEntryForMerge != null)
            entry = _getSynthEntryForMerge(characterNameForSynthParams, usernameForSynthParams);
        queryJson = VoicevoxAudioQueryMerger.ApplyOverrides(queryJson, entry);

        using var content = new StringContent(queryJson, Encoding.UTF8, "application/json");
        using var syn = await _http
            .PostAsync($"synthesis?speaker={speakerId}", content, ct)
            .ConfigureAwait(false);
        syn.EnsureSuccessStatusCode();
        return await syn.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
