using System.Buffers.Binary;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Just enough WAV: a file's length, and samples out of a WAV that arrives in pieces (a streamed answer). 16, 24 or
/// 32-bit, whole numbers or (32-bit) floating point, as GPT-SoVITS and the tools that cut voice clips write them.
/// </summary>
public sealed class Wav
{
    private readonly List<byte> _header = [];
    private bool _inData, _float;
    private byte[] _carry = [];                     // the start of a frame the last piece cut off
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
        var width = _bits / 8;
        if (_float ? width != 4 : width is not (2 or 3 or 4))
            throw new InvalidDataException($"{_bits}-bit {(_float ? "floating-point " : "")}WAV is not supported");
        byte[] data = _carry.Length > 0 ? [.. _carry, .. bytes] : bytes.ToArray();
        var frame = width * _channels;
        var frames = data.Length / frame;
        _carry = data[(frames * frame)..];
        var samples = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < _channels; c++)
            {
                var at = data.AsSpan((f * _channels + c) * width);
                sum += width switch
                {
                    2 => BinaryPrimitives.ReadInt16LittleEndian(at) / 32768f,
                    3 => ((at[0] | at[1] << 8 | at[2] << 16) << 8 >> 8) / 8388608f,       // sign from the top byte
                    _ when _float => BinaryPrimitives.ReadSingleLittleEndian(at),
                    _ => BinaryPrimitives.ReadInt32LittleEndian(at) / 2147483648f,
                };
            }
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
                var format = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(at + 8));
                if (format == 0xFFFE && size >= 40)         // WAVE_FORMAT_EXTENSIBLE: the real format leads its sub-format
                    format = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(at + 8 + 24));
                _float = format == 3;
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
