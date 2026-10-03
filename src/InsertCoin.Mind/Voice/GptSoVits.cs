using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>A piece of her voice: mono samples (-1..1) at a sample rate (32 kHz for v2 models, 48 kHz for v4).</summary>
public readonly record struct VoiceAudio(int SampleRate, float[] Samples);

/// <summary>The voice engine could not do what was asked; the message says why, in its words.</summary>
public sealed class VoiceException(string message) : Exception(message);

/// <summary>
/// Talks to a GPT-SoVITS server (its <c>api_v2.py</c>, on this PC): loads a voice's weights, then turns one sentence
/// at a time into audio that streams back while it is being made. Paths are the server's: absolute, or relative to
/// its folder for its own base models.
/// </summary>
public sealed class GptSoVits(HttpClient http)
{
    /// <summary>The base model a clip is cloned with when a voice has no weights of its own: v4, the clearest (measured 2026-10-02).</summary>
    public const string BaseGpt = "GPT_SoVITS/pretrained_models/s1v3.ckpt", BaseSovits = "GPT_SoVITS/pretrained_models/gsv-v4-pretrained/s2Gv4.pth";

    private (string Gpt, string Sovits)? _loaded;

    /// <summary>Whether the server answers.</summary>
    public async Task<bool> Alive(CancellationToken cancel = default)
    {
        try
        {
            using var response = await http.GetAsync("docs", cancel);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Loads a voice's weights, unless they are the ones loaded already (switching takes a few seconds).</summary>
    public async Task Load(string gpt, string sovits, CancellationToken cancel = default)
    {
        if (_loaded == (gpt, sovits))
            return;
        _loaded = null;
        await Call($"set_gpt_weights?weights_path={Uri.EscapeDataString(gpt)}", cancel);
        await Call($"set_sovits_weights?weights_path={Uri.EscapeDataString(sovits)}", cancel);
        _loaded = (gpt, sovits);
    }

    /// <summary>The server was restarted: whatever it had loaded is gone.</summary>
    public void Forget() => _loaded = null;

    /// <summary>
    /// Says <paramref name="text"/> in <paramref name="language"/> with the tone of <paramref name="clip"/> (whose file is
    /// at <paramref name="clipPath"/>). Audio comes back in pieces as it is made. <paramref name="steps"/> only
    /// matters to v3 and v4 models.
    /// </summary>
    public async IAsyncEnumerable<VoiceAudio> Speak(string text, string language, string clipPath, VoiceClip clip, int steps,
                                                    [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var body = new
        {
            text,
            text_lang = language,
            ref_audio_path = clipPath,
            prompt_text = clip.Text,
            prompt_lang = clip.Language.Length > 0 ? clip.Language : language,
            text_split_method = "cut5",
            batch_size = 1,
            media_type = "wav",
            streaming_mode = 2,                 // starts sooner than a whole sentence at once, at no cost heard (measured 2026-10-02)
            parallel_infer = false,
            sample_steps = steps,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
            throw new VoiceException(Reason(await response.Content.ReadAsStringAsync(cancel)));
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var wav = new Wav();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
            if (wav.Feed(buffer.AsSpan(0, read)) is { Length: > 0 } samples)
                yield return new VoiceAudio(wav.SampleRate, samples);
    }

    private async Task Call(string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(path, cancel);
        if (!response.IsSuccessStatusCode)
            throw new VoiceException(Reason(await response.Content.ReadAsStringAsync(cancel)));
    }

    /// <summary>The server's own explanation from its JSON error ({"message": …, "Exception": …}), or the body as it is.</summary>
    private static string Reason(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var detail = root.TryGetProperty("Exception", out var e) ? e.GetString() : null;
            return string.Join(": ", new[] { message, detail }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } reason ? reason : body;
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }
}
