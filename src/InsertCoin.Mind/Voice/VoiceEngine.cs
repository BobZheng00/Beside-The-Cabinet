using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>A piece of her voice: mono samples (-1..1) at a sample rate (24 kHz for Qwen3-TTS, 32 kHz for v2 models, 48 kHz for v4).</summary>
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

/// <summary>The engines her voice can run on, both on this PC: GPT-SoVITS (its official Windows package) and Qwen3-TTS.</summary>
public static class VoiceEngines
{
    public const string GptSoVits = "gpt-sovits", Qwen = "qwen3-tts";
    public static readonly string[] All = [GptSoVits, Qwen];

    public static string Name(string engine) => engine == Qwen ? "Qwen3-TTS" : "GPT-SoVITS";
}

/// <summary>A voice engine's server, as the cabinet uses it: load a voice, then say a sentence at a time with it.</summary>
public interface IVoiceEngine
{
    /// <summary>Whether the server answers.</summary>
    Task<bool> Alive(CancellationToken cancel = default);

    /// <summary>Loads what <paramref name="profile"/> speaks with (its files are in <paramref name="folder"/>), unless that is loaded already.</summary>
    Task Load(VoiceProfile profile, string folder, CancellationToken cancel = default);

    /// <summary>The server was restarted: whatever it had loaded is gone.</summary>
    void Forget();

    /// <summary>Says <paramref name="text"/> in the voice's language with the tone of <paramref name="clip"/> (whose file is at <paramref name="clipPath"/>); audio comes back in pieces as it is made.</summary>
    IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile profile, VoiceClip clip, string clipPath, CancellationToken cancel = default);
}

internal static class VoiceStream
{
    /// <summary>Sends <paramref name="request"/> (and disposes of it); the samples of the WAV answer as they arrive, or the engine's own explanation if it said no.</summary>
    public static async IAsyncEnumerable<VoiceAudio> Read(HttpClient http, HttpRequestMessage request, [EnumeratorCancellation] CancellationToken cancel)
    {
        using var sent = request;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var wav = new Wav();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
            if (wav.Feed(buffer.AsSpan(0, read)) is { Length: > 0 } samples)
                yield return new VoiceAudio(wav.SampleRate, samples);
    }
}
