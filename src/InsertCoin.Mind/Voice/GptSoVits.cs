using System.Net.Http.Json;

namespace InsertCoin.Mind;

/// <summary>
/// Talks to a GPT-SoVITS server (its <c>api_v2.py</c>, on this PC): loads a voice's weights, then turns one sentence
/// at a time into audio that streams back while it is being made. Paths are the server's: absolute, or relative to
/// its folder for its own base models.
/// </summary>
public sealed class GptSoVits(HttpClient http) : IVoiceEngine
{
    /// <summary>
    /// The base model a clip is cloned with when a voice has no weights of its own (see
    /// <see cref="VoiceProfile.CloneModels"/>): v4, the clearest (measured 2026-10-02), unless v2ProPlus is asked for.
    /// </summary>
    public static (string Gpt, string Sovits) Base(string? version) => version switch
    {
        "v2ProPlus" => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
        _ => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/gsv-v4-pretrained/s2Gv4.pth"),
    };

    private (string Gpt, string Sovits)? _loaded;

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

    /// <summary>Loads the voice's own weights, or the base model it is cloned with.</summary>
    public Task Load(VoiceProfile profile, string folder, CancellationToken cancel = default)
    {
        var (gpt, sovits) = profile.Gpt is { } g && profile.Sovits is { } s
            ? (Path.GetFullPath(Path.Combine(folder, g)), Path.GetFullPath(Path.Combine(folder, s)))
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
    }

    public void Forget() => _loaded = null;

    public IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile profile, VoiceClip clip, string clipPath, CancellationToken cancel = default) =>
        Speak(text, profile.Language, clipPath, clip, profile.Steps, cancel);

    /// <summary>
    /// Says <paramref name="text"/> in <paramref name="language"/> with the tone of <paramref name="clip"/> (whose file is
    /// at <paramref name="clipPath"/>). Audio comes back in pieces as it is made. <paramref name="steps"/> only
    /// matters to v3 and v4 models.
    /// </summary>
    public IAsyncEnumerable<VoiceAudio> Speak(string text, string language, string clipPath, VoiceClip clip, int steps, CancellationToken cancel = default)
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
        return VoiceStream.Read(http, new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(body) }, cancel);
    }

    private async Task Call(string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(path, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
    }
}
