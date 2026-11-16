// Windows' own voices saying lines into WAV files, for the test speech (#346). Reads jobs as tab-separated lines on
// standard input: the voice's name ("Microsoft Zira"), the output path, the text.
//
//     dotnet run tools/bench/speech/sapi_say.cs < jobs.tsv

#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false

using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

Console.InputEncoding = System.Text.Encoding.UTF8;     // a Chinese Windows reads standard input as GBK otherwise
var voices = SpeechSynthesizer.AllVoices;
using var synthesiser = new SpeechSynthesizer();
string? line;
while ((line = Console.ReadLine()) is not null)
{
    var parts = line.Split('\t');
    if (parts.Length < 3)
        continue;
    var voice = voices.FirstOrDefault(v => v.DisplayName == parts[0])
        ?? throw new Exception($"no voice {parts[0]}: {string.Join(", ", voices.Select(v => v.DisplayName))}");
    synthesiser.Voice = voice;
    using var stream = await synthesiser.SynthesizeTextToStreamAsync(parts[2]);
    var bytes = new byte[stream.Size];
    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
    {
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
    }
    await File.WriteAllBytesAsync(parts[1], bytes);
    Console.WriteLine($"made {parts[1]}");
}
