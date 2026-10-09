using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>A piece of her voice: mono samples (-1..1) at a sample rate (32 kHz for v2 models, 48 kHz for v4).</summary>
public readonly record struct VoiceAudio(int SampleRate, float[] Samples);

/// <summary>The voice engine could not do what was asked; the message says why, in its words.</summary>
public sealed class VoiceException(string message) : Exception(message)
{
    /// <summary>The server's own explanation from its JSON error ({"message": …, "Exception": …}), or the body as it is.</summary>
    public static VoiceException From(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var detail = root.TryGetProperty("Exception", out var e) ? e.GetString() : null;
            var reason = string.Join(": ", new[] { message, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return new VoiceException(reason.Length > 0 ? reason : body);
        }
        catch (JsonException)
        {
            return new VoiceException(body.Length > 300 ? body[..300] : body);
        }
    }
}

/// <summary>
/// Talks to a GPT-SoVITS server (its <c>api_v2.py</c>, on this PC): loads a voice's weights, then turns one sentence
/// at a time into audio that streams back while it is being made. Paths are the server's: absolute, or relative to
/// its folder for its own base models.
/// </summary>
public sealed class GptSoVits(HttpClient http)
{
    /// <summary>
    /// The base model a clip is cloned with when a voice has no weights of its own (see
    /// <see cref="VoiceProfile.CloneVersions"/>): v4, the clearest (measured 2026-10-02), unless v2ProPlus is asked for.
    /// </summary>
    public static (string Gpt, string Sovits) Base(string? version) => version switch
    {
        "v2ProPlus" => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
        _ => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/gsv-v4-pretrained/s2Gv4.pth"),
    };

    /// <summary>
    /// v1 and v2 models (v2Pro, v2ProPlus) blend the tone of a voice's other clips into the one speaking. v3 and v4
    /// take their tone from the speaking clip alone; worse, preparing extra clips overwrites the recording they read
    /// it from, so they never get any.
    /// </summary>
    public static bool Blends(string? version) => version is "v1" or "v2" or "v2Pro" or "v2ProPlus";

    /// <summary>
    /// How many clips are blended in. Each one costs time on every sentence: with Hana's v2ProPlus clone, 5 extra
    /// clips took the first audio from 0.37 to 0.50 s and made her more like herself (0.82 to 0.86 to a speaker
    /// model); 10 took 0.72 s and 20 took 1.45 s for no more likeness, and all 54 were slower than speech (2026-10-03).
    /// </summary>
    public const int BlendClips = 4;

    private (string Gpt, string Sovits)? _loaded;
    private bool _stale;

    /// <summary>The quiet dropped from the start of the last line said (see <see cref="QuietStart"/>).</summary>
    public TimeSpan LastTrimmed { get; private set; }

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

    /// <summary>Loads the voice's trained model (its files are in <paramref name="folder"/>), or the base model it is cloned with.</summary>
    public Task Load(VoiceProfile profile, string folder, CancellationToken cancel = default)
    {
        var (gpt, sovits) = profile.SpeaksTrained
            ? (Path.GetFullPath(Path.Combine(folder, profile.Gpt!)), Path.GetFullPath(Path.Combine(folder, profile.Sovits!)))
            : Base(profile.Runs);
        return Load(gpt, sovits, cancel);
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
        _stale = true;                  // the server keeps the clip it prepared for the old weights: prepare it again
    }

    /// <summary>The server was restarted: whatever it had loaded is gone.</summary>
    public void Forget() => _loaded = null;

    /// <summary>
    /// Says <paramref name="text"/> in the voice's language with the tone of <paramref name="clip"/> (blended, for v2
    /// models, with its first few clips). Audio comes back in pieces as it is made. Clips are named relative to
    /// <paramref name="folder"/>.
    /// </summary>
    public async IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile profile, string folder, VoiceClip clip,
                                                    [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var clipPath = Path.GetFullPath(Path.Combine(folder, clip.File));
        if (_stale)
        {
            await Call($"set_refer_audio?refer_audio_path={Uri.EscapeDataString(clipPath)}", cancel);
            _stale = false;
        }
        // her first few, whichever is speaking: the same set whatever the mood, so the server prepares them only once
        var blended = Blends(profile.Runs)
            ? profile.Clips.Select(c => Path.GetFullPath(Path.Combine(folder, c.File))).Where(File.Exists).Take(BlendClips).ToArray()
            : [];
        var body = new
        {
            text,
            text_lang = profile.Language,
            ref_audio_path = clipPath,
            aux_ref_audio_paths = blended,
            prompt_text = clip.Text,
            prompt_lang = clip.Language.Length > 0 ? clip.Language : profile.Language,
            text_split_method = "cut5",
            batch_size = 1,
            media_type = "wav",
            streaming_mode = 2,                 // starts sooner than a whole sentence at once, at no cost heard (measured 2026-10-02)
            parallel_infer = false,
            sample_steps = profile.Steps,       // only v3 and v4 use it
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var wav = new Wav();
        QuietStart? quiet = null;
        LastTrimmed = TimeSpan.Zero;
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
            if (wav.Feed(buffer.AsSpan(0, read)) is { Length: > 0 } samples)
            {
                quiet ??= new QuietStart(wav.SampleRate);
                var heard = quiet.Feed(samples);
                LastTrimmed = quiet.DroppedTime;
                if (heard.Length > 0)
                    yield return new VoiceAudio(wav.SampleRate, heard);
            }
    }

    private async Task Call(string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(path, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
    }
}
