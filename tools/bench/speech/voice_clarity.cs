// Which sound effects Windows puts on each microphone, by the kind of app that opens it (#346). It reads Windows' own
// list of a microphone's effects for a category and processing mode, as an app would get them: it never opens the
// microphone and records nothing. Voice Clarity (Windows 11 24H2) shows as echo cancelling, noise suppression and deep
// noise suppression on a stream opened for communications or speech, with the default processing.
//
//     dotnet run tools/bench/speech/voice_clarity.cs
//
// To hear the difference with your own microphone, run hear_voice_clarity.cs (it records, so only you run it).

#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false

using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media;

string[] names = ["Other", "AcousticEchoCancellation", "NoiseSuppression", "AutomaticGainControl", "BeamForming",
    "ConstantToneRemoval", "Equalizer", "LoudnessEqualizer", "BassBoost", "VirtualSurround", "VirtualHeadphones",
    "SpeakerFill", "RoomCorrection", "BassManagement", "EnvironmentalEffects", "SpeakerProtection",
    "SpeakerCompensation", "DynamicRangeCompression", "FarFieldBeamForming", "DeepNoiseSuppression"];
string Name(AudioEffectType t) => (int)t < names.Length ? names[(int)t] : $"type {(int)t}";

var communications = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Communications);
var console = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
var os = Environment.OSVersion.Version;
Console.WriteLine($"Windows {os.Major}.{os.Minor} build {os.Build}; {devices.Count} microphone(s)");
foreach (var device in devices)
{
    var marks = (device.Id == console ? " [default]" : "") + (device.Id == communications ? " [default for calls]" : "");
    Console.WriteLine($"\n{device.Name}{marks}");
    foreach (var category in new[] { MediaCategory.Communications, MediaCategory.Speech, MediaCategory.Other })
        foreach (var processing in new[] { AudioProcessing.Default, AudioProcessing.Raw })
        {
            try
            {
                var manager = AudioEffectsManager.CreateAudioCaptureEffectsManager(device.Id, category, processing);
                var effects = manager.GetAudioCaptureEffects().Select(e => Name(e.AudioEffectType)).ToList();
                Console.WriteLine($"  {category,-14} {processing,-7} {(effects.Count == 0 ? "(none)" : string.Join(", ", effects))}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {category,-14} {processing,-7} could not read: {e.Message}");
            }
        }
}
