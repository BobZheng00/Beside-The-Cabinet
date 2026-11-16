using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>A piece of her voice: mono samples (-1..1) at a sample rate (32 kHz for v2 models, 48 kHz for v4).</summary>
public readonly record struct VoiceAudio(int SampleRate, float[] Samples);

/// <summary>The voice engine could not do what was asked; the message says why, in its words (or in the player's, with the engine's inside).</summary>
public sealed class VoiceException(string message, Exception? inner = null) : Exception(message, inner)
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
/// <param name="hasV5">The engine has v5 fast's files (see <see cref="V5Fast"/>): without them a v5 fast copy speaks with v2ProPlus.</param>
/// <param name="card">The engine can use the graphics card: without one, v5 fast is far too slow to stream.</param>
public sealed class GptSoVits(HttpClient http, bool hasV5 = true, bool card = true)
{
    /// <summary>
    /// The base model a clip is cloned with when a voice has no weights of its own (see
    /// <see cref="VoiceProfile.CloneVersions"/>): v5 fast unless v2ProPlus is asked for.
    /// </summary>
    public static (string Gpt, string Sovits) Base(string? version) => version switch
    {
        "v2ProPlus" => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
        _ => ("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/gsv-v5-pretrained/s2Gv5turbo.pth"),
    };

    /// <summary>The voice as this engine can speak it.</summary>
    private VoiceProfile Fitted(VoiceProfile profile) =>
        hasV5 || profile.Runs != V5Fast.Version ? profile : profile with { CloneWith = "v2ProPlus" };

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
    private readonly SemaphoreSlim _free = new(1, 1);      // one request at a time: a line being made, or a switch of weights

    /// <summary>The pause before a switch the server turned down is asked for again.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a line cut off is still read to its end on v5 fast's server, so it has finished the line before it is
    /// asked anything else: that server turns down whatever comes while it is making one ("attached to a different
    /// loop"). The package's own server has no such trouble, and a line cut off there ends at once.
    /// </summary>
    public TimeSpan FinishLimit { get; init; } = TimeSpan.FromSeconds(20);

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
        profile = Fitted(profile);
        var (gpt, sovits) = profile.SpeaksTrained
            ? (Path.GetFullPath(Path.Combine(folder, profile.Gpt!)), Path.GetFullPath(Path.Combine(folder, profile.Sovits!)))
            : Base(profile.Runs);
        return Load(gpt, sovits, cancel);
    }

    /// <summary>
    /// Loads a voice's weights, unless they are the ones loaded already (switching takes a few seconds), once the line
    /// being made has finished. A switch the server turns down is asked for once more; if it is turned down again,
    /// the exception says so in the player's words, with the server's reason inside.
    /// </summary>
    public async Task Load(string gpt, string sovits, CancellationToken cancel = default)
    {
        if (_loaded == (gpt, sovits))
            return;
        await _free.WaitAsync(cancel);
        try
        {
            _loaded = null;
            await Switch($"set_gpt_weights?weights_path={Uri.EscapeDataString(gpt)}");
            cancel.ThrowIfCancellationRequested();
            await Switch($"set_sovits_weights?weights_path={Uri.EscapeDataString(sovits)}");
            _loaded = (gpt, sovits);
            _stale = true;                  // the server keeps the clip it prepared for the old weights: prepare it again
        }
        finally
        {
            _free.Release();
        }
    }

    /// <summary>One switch, never cut off halfway (the server would go on loading and turn the next request down), asked for twice if need be.</summary>
    private async Task Switch(string path)
    {
        try
        {
            await Call(path, CancellationToken.None);
        }
        catch (VoiceException)
        {
            await Task.Delay(RetryAfter);
            try
            {
                await Call(path, CancellationToken.None);
            }
            catch (VoiceException e)
            {
                throw new VoiceException("the voice didn't load. Switch it off and on above to try again.", e);
            }
        }
    }

    /// <summary>The server was restarted: whatever it had loaded is gone.</summary>
    public void Forget() => _loaded = null;

    /// <summary>
    /// Says <paramref name="text"/> in the voice's language with the tone of <paramref name="clip"/> (blended, for v2
    /// models, with its first few clips). Audio comes back in pieces as it is made, without the quiet GPT-SoVITS often
    /// puts before a line (about a second, measured 2026-10-09): <paramref name="leadIn"/> of it is kept (see
    /// <see cref="QuietStart"/>). Clips are named relative to <paramref name="folder"/>.
    /// </summary>
    public async IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile profile, string folder, VoiceClip clip,
                                                    [EnumeratorCancellation] CancellationToken cancel = default, TimeSpan? leadIn = null)
    {
        profile = Fitted(profile);
        var clipPath = Path.GetFullPath(Path.Combine(folder, clip.File));
        await _free.WaitAsync(cancel);
        // cut off, a line on v5 fast's server is still read to its end (up to FinishLimit), so the server has finished it
        // before it is asked anything else; on the package's own server it ends at once
        using var finish = new CancellationTokenSource();
        using var cutOff = cancel.Register(() =>
        {
            if (hasV5)
                finish.CancelAfter(FinishLimit);
            else
                finish.Cancel();
        });
        HttpResponseMessage? response = null;
        Stream? stream = null;
        var ended = false;
        var buffer = new byte[16384];
        try
        {
            if (_stale)
            {
                await Call($"set_refer_audio?refer_audio_path={Uri.EscapeDataString(clipPath)}", CancellationToken.None);
                _stale = false;
            }
            cancel.ThrowIfCancellationRequested();
            // her first few, whichever is speaking: the same set whatever the mood, so the server prepares them only once
            var blended = Blends(profile.Runs)
                ? profile.Clips.Select(c => Path.GetFullPath(Path.Combine(folder, c.File))).Where(File.Exists).Take(BlendClips).ToArray()
                : [];
            var body = new Dictionary<string, object>
            {
                ["text"] = text,
                ["text_lang"] = profile.Language,
                ["ref_audio_path"] = clipPath,
                ["aux_ref_audio_paths"] = blended,
                ["prompt_text"] = clip.Text,
                ["prompt_lang"] = clip.Language.Length > 0 ? clip.Language : profile.Language,
                ["text_split_method"] = "cut5",
                ["batch_size"] = 1,
                ["media_type"] = "wav",
                ["streaming_mode"] = 2,             // starts sooner than a whole sentence at once, at no cost heard (measured 2026-10-02)
                ["parallel_infer"] = false,
            };
            if (profile.Runs != V5Fast.Version)
                body["sample_steps"] = profile.Steps;       // only v3 and v4 use it; v5 fast's own default is 4, and 16 would be four times the work
            else if (card)
            {
                body["gpt_streaming"] = true;
                body["streaming_chunk_seconds"] = 1.0;
                body["gpt_streaming_lookahead_seconds"] = 0.3;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(body) };
            response = await Uncut(http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, finish.Token), cancel);
            if (!response.IsSuccessStatusCode)
            {
                ended = true;
                throw VoiceException.From(await response.Content.ReadAsStringAsync(CancellationToken.None));
            }
            stream = await Uncut(response.Content.ReadAsStreamAsync(finish.Token), cancel);
            var wav = new Wav();
            QuietStart? quiet = null;
            LastTrimmed = TimeSpan.Zero;
            while (!cancel.IsCancellationRequested)
            {
                var read = await Uncut(stream.ReadAsync(buffer, finish.Token).AsTask(), cancel);
                if (read == 0)
                {
                    ended = true;
                    break;
                }
                if (wav.Feed(buffer.AsSpan(0, read)) is { Length: > 0 } samples)
                {
                    quiet ??= new QuietStart(wav.SampleRate, leadIn);
                    var heard = quiet.Feed(samples);
                    LastTrimmed = quiet.DroppedTime;
                    if (heard.Length > 0)
                        yield return new VoiceAudio(wav.SampleRate, heard);
                }
            }
            cancel.ThrowIfCancellationRequested();
        }
        finally
        {
            if (stream is not null && !ended && hasV5)
            {
                if (!cancel.IsCancellationRequested)
                    finish.CancelAfter(FinishLimit);        // left before its end without being cut off: the same limit from now
                try
                {
                    while (await stream.ReadAsync(buffer, finish.Token) > 0)
                    {
                    }
                }
                catch (Exception)
                {
                    // the server went, or took too long: nothing more to wait for
                }
            }
            stream?.Dispose();
            response?.Dispose();
            _free.Release();
        }
    }

    /// <summary>A line cut off whose server then went (stopped for a game, say) ends as cut off, not as a fault.</summary>
    private static async Task<T> Uncut<T>(Task<T> task, CancellationToken cancel)
    {
        try
        {
            return await task;
        }
        catch (Exception e) when (cancel.IsCancellationRequested && e is not OperationCanceledException)
        {
            throw new OperationCanceledException(cancel);
        }
    }

    private async Task Call(string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(path, cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
    }
}
