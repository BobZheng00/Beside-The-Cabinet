using System.Buffers.Binary;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>Just enough WAV: a file's length, and samples out of a WAV that arrives in pieces (a streamed answer).</summary>
public sealed class Wav
{
    private readonly List<byte> _header = [];
    private bool _inData;
    private byte? _odd;
    private int _channels = 1, _bits = 16;

    /// <summary>Samples per second, once the header has been read.</summary>
    public int SampleRate { get; private set; }

    /// <summary>Feeds the next bytes; returns the mono samples (-1..1) they completed, possibly none.</summary>
    public float[] Feed(ReadOnlySpan<byte> bytes)
    {
        if (!_inData)
        {
            _header.AddRange(bytes.ToArray());
            if (DataStart(_header.ToArray()) is not { } start)
                return [];
            _inData = true;
            var rest = _header.Skip(start).ToArray();
            _header.Clear();
            return Samples(rest);
        }
        return Samples(bytes);
    }

    private float[] Samples(ReadOnlySpan<byte> bytes)
    {
        if (_bits != 16)
            throw new InvalidDataException($"only 16-bit WAV is supported, not {_bits}-bit");
        var data = bytes.ToArray();
        if (_odd is { } carried)
        {
            data = [carried, .. data];
            _odd = null;
        }
        if (data.Length % 2 == 1)
        {
            _odd = data[^1];
            data = data[..^1];
        }
        var frames = data.Length / 2 / _channels;
        var samples = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < _channels; c++)
                sum += BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((f * _channels + c) * 2)) / 32768f;
            samples[f] = sum / _channels;
        }
        return samples;
    }

    /// <summary>Where the samples start, once the header up to the data chunk is all here; reads the format on the way.</summary>
    private int? DataStart(byte[] header)
    {
        if (header.Length < 12)
            return null;
        if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" || Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
            throw new InvalidDataException("not a WAV stream");
        var at = 12;
        while (at + 8 <= header.Length)
        {
            var id = Encoding.ASCII.GetString(header, at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at + 4));
            if (id == "data")
                return at + 8;                       // its size may be 0 or wrong in a stream: read to the end instead
            if (at + 8 + size > header.Length)
                return null;
            if (id == "fmt ")
            {
                _channels = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(at + 10));
                SampleRate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at + 12));
                _bits = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(at + 22));
            }
            at += 8 + size + (size & 1);
        }
        return null;
    }

    /// <summary>How long a WAV file plays, or null when it is not a WAV file this can read.</summary>
    public static double? Seconds(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var header = new byte[Math.Min(file.Length, 4096)];
            file.ReadExactly(header);
            var wav = new Wav();
            if (wav.DataStart(header) is not { } start || wav.SampleRate <= 0)
                return null;
            return (file.Length - start) / (double)(wav.SampleRate * wav._channels * (wav._bits / 8));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
