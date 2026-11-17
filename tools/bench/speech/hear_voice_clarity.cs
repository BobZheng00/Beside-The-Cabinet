// Hear what Windows' Voice Clarity does to your own microphone (#346). For the owner to run once, by hand: it opens
// the microphone, so no test or agent ever runs it.
//
//     dotnet run tools/bench/speech/hear_voice_clarity.cs [-- [--apart] [<folder for the recordings>]]
//
// It records the same 10 seconds twice at once from your default microphone: as the app opens it today (an ordinary
// stream) and as a call opens it (communications mode, where Windows 11 applies Voice Clarity: on this PC's USB
// microphones Windows lists echo cancelling, noise suppression, gain control and deep noise suppression; Microsoft
// also names de-reverb, which the list doesn't show separately). A voice talks from the speakers for part of it, as
// the companion would. Then it plays both back. Use speakers, not headphones, and talk from where you usually sit,
// over the voice too. The recordings are kept in the folder (by default voice-clarity-check in the current folder)
// and sent nowhere.
//
// If the two recordings sound the same, don't conclude yet that Voice Clarity does nothing: some drivers run one
// processing mode at a time, so both streams may have got the same one. Run it again with --apart: it records the
// two modes one after the other, 10 seconds each, and plays those back.

#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false

using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;

const int Seconds = 10;
var apart = args.Contains("--apart");
var folder = Path.GetFullPath(args.FirstOrDefault(a => a != "--apart") ?? "voice-clarity-check");
Directory.CreateDirectory(folder);
var microphone = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);

foreach (var (label, category) in new[] { ("today", MediaCategory.Other), ("call", MediaCategory.Communications) })
{
    var effects = AudioEffectsManager.CreateAudioCaptureEffectsManager(microphone, category, AudioProcessing.Default)
        .GetAudioCaptureEffects().Select(e => e.AudioEffectType.ToString()).ToList();
    Console.WriteLine($"{label,-6} Windows' effects: {(effects.Count == 0 ? "none" : string.Join(", ", effects))}");
}

async Task<(MediaCapture, StorageFile)> Recorder(string name, MediaCategory category)
{
    var capture = new MediaCapture();
    await capture.InitializeAsync(new MediaCaptureInitializationSettings
    {
        StreamingCaptureMode = StreamingCaptureMode.Audio,
        AudioDeviceId = microphone,
        MediaCategory = category,
        AudioProcessing = AudioProcessing.Default,
    });
    var file = await (await StorageFolder.GetFolderFromPathAsync(folder))
        .CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
    return (capture, file);
}

// records the given streams at once while a voice talks from the speakers
async Task Record(params (MediaCapture Capture, StorageFile File)[] recorders)
{
    Console.WriteLine($"\nIn 3 seconds: talk for {Seconds} seconds from where you sit. A voice will talk from the speakers too.");
    await Task.Delay(3000);
    var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
    foreach (var r in recorders)
        await r.Capture.StartRecordToStorageFileAsync(profile, r.File);
    Console.WriteLine("Recording...");
    using var synthesiser = new SpeechSynthesizer();
    var speech = await synthesiser.SynthesizeTextToStreamAsync(
        "This is the companion talking from the speakers. Keep talking over me, so you can hear whether my voice is taken out.");
    using var player = new MediaPlayer { Source = MediaSource.CreateFromStream(speech, speech.ContentType) };
    await Task.Delay(1500);
    player.Play();
    await Task.Delay(Seconds * 1000 - 1500);
    foreach (var r in recorders)
    {
        await r.Capture.StopRecordAsync();
        r.Capture.Dispose();
    }
}

var today = await Recorder("1-today.wav", MediaCategory.Other);
if (apart)
{
    await Record(today);
    Console.WriteLine("Now the same again, as a call opens the microphone.");
}
var call = await Recorder("2-voice-clarity.wav", MediaCategory.Communications);
if (apart)
    await Record(call);
else
    await Record(today, call);
Console.WriteLine("Done. The microphone is closed.\n");

async Task Play(string label, StorageFile file)
{
    Console.WriteLine(label);
    var done = new TaskCompletionSource();
    using var back = new MediaPlayer { Source = MediaSource.CreateFromStorageFile(file) };
    back.MediaEnded += (_, _) => done.TrySetResult();
    back.MediaFailed += (_, e) => done.TrySetException(new Exception(e.ErrorMessage));
    back.Play();
    await done.Task;
    await Task.Delay(800);
}
await Play("1. As the app hears you today:", today.Item2);
await Play("2. With Voice Clarity (communications mode):", call.Item2);
Console.WriteLine($"\nBoth recordings are in {folder}");
