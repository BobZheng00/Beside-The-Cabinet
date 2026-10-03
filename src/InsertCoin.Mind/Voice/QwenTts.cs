using System.Net.Http.Json;

namespace InsertCoin.Mind;

/// <summary>
/// Talks to the cabinet's own Qwen3-TTS server (<c>qwen_server.py</c>, run with the Python of the player's Qwen3-TTS):
/// loads a base model, then clones a voice from a clip and its words a sentence at a time, the audio streaming back
/// while it is made (24 kHz).
/// </summary>
public sealed class QwenTts(HttpClient http) : IVoiceEngine
{
    /// <summary>
    /// Speech steps (1/12 s each) made before a piece is sent. With the 1.7B model on the development laptop, 4 starts
    /// speaking about 0.33 s after asking and makes speech in 0.74–0.80 of the time it plays; 8 starts after about
    /// 0.5 s at 0.67 (measured 2026-10-03).
    /// </summary>
    public const int PieceSteps = 4;

    /// <summary>The base model of a size the voice is cloned with (see <see cref="VoiceProfile.CloneModels"/>).</summary>
    public static string Model(string? size) => $"Qwen/Qwen3-TTS-12Hz-{size ?? "1.7B"}-Base";

    /// <summary>The server's own code, carried inside the cabinet and written out beside its log when it starts.</summary>
    public static string ServerScript()
    {
        using var stream = typeof(QwenTts).Assembly.GetManifestResourceStream("qwen_server.py")
                           ?? throw new InvalidOperationException("qwen_server.py is missing from the build");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string? _loaded;

    public async Task<bool> Alive(CancellationToken cancel = default)
    {
        try
        {
            using var response = await http.GetAsync("health", cancel);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Loads (and warms up) the base model the voice is cloned with; the first time takes about 10 s.</summary>
    public async Task Load(VoiceProfile profile, string folder, CancellationToken cancel = default)
    {
        var model = Model(profile.Runs);
        if (_loaded == model)
            return;
        _loaded = null;
        using var response = await http.PostAsJsonAsync("load", new { model }, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
        _loaded = model;
    }

    public void Forget() => _loaded = null;

    public IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile profile, VoiceClip clip, string clipPath, CancellationToken cancel = default)
    {
        var body = new
        {
            text,
            language = profile.Language,
            ref_audio = clipPath,
            ref_text = clip.Text,
            chunk_size = PieceSteps,
        };
        return VoiceStream.Read(http, new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(body) }, cancel);
    }
}
