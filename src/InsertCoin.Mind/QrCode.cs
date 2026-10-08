using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// A QR code for a short text (the address of the page a phone sends photos to): byte mode, error correction level L,
/// versions 1 to 5 (up to 106 bytes), whose single block needs no interleaving. Written from the QR standard
/// (ISO/IEC 18004) as the well-known reference implementations do it: data and error correction codewords, the fixed
/// patterns, the codewords placed two columns at a time, the mask with the least penalty, and the format bits.
/// </summary>
public static class QrCode
{
    private const int Level = 1;                            // the format bits for level L

    // per version 1 to 5 at level L: data codewords, error correction codewords (one block each)
    private static readonly int[] DataWords = [0, 19, 34, 55, 80, 108];
    private static readonly int[] CheckWords = [0, 7, 10, 15, 20, 26];

    /// <summary>The code's modules, true for dark, [row, column], without the quiet zone around it (leave four modules of light).</summary>
    public static bool[,] Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var version = Enumerable.Range(1, 5).FirstOrDefault(v => 4 + 8 + 8 * bytes.Length <= DataWords[v] * 8);
        if (version == 0)
            throw new ArgumentException($"too long for a small QR code: {bytes.Length} bytes, 106 at most", nameof(text));
        var codewords = Codewords(bytes, version);
        var size = 17 + 4 * version;
        var modules = new bool[size, size];
        var function = new bool[size, size];
        DrawPatterns(modules, function, version);
        DrawFormat(modules, function, 0);                   // reserved now, written for real once the mask is chosen
        Place(modules, function, codewords);

        var best = 0;
        var lowest = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            Mask(modules, function, mask);
            DrawFormat(modules, function, mask);
            var penalty = Penalty(modules);
            if (penalty < lowest)
                (best, lowest) = (mask, penalty);
            Mask(modules, function, mask);                  // masking twice undoes it
        }
        Mask(modules, function, best);
        DrawFormat(modules, function, best);
        return modules;
    }

    /// <summary>The 15 format bits for an error correction level (the standard's two bits: L 1, M 0, Q 3, H 2) and a mask, BCH-coded and masked.</summary>
    public static int FormatBits(int level, int mask)
    {
        var data = level << 3 | mask;
        var remainder = data;
        for (var i = 0; i < 10; i++)
            remainder = remainder << 1 ^ (remainder >> 9) * 0x537;
        return (data << 10 | remainder) ^ 0x5412;
    }

    /// <summary>The data codewords for the text, then its error correction codewords.</summary>
    private static byte[] Codewords(byte[] bytes, int version)
    {
        var bits = new List<bool>();
        void Put(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
                bits.Add((value >> i & 1) != 0);
        }
        Put(0b0100, 4);                                     // byte mode
        Put(bytes.Length, 8);
        foreach (var b in bytes)
            Put(b, 8);
        var capacity = DataWords[version] * 8;
        Put(0, Math.Min(4, capacity - bits.Count));         // the terminator
        Put(0, (8 - bits.Count % 8) % 8);
        var data = new byte[DataWords[version]];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i])
                data[i / 8] |= (byte)(0x80 >> i % 8);
        for (var i = bits.Count / 8; i < data.Length; i++)
            data[i] = (i - bits.Count / 8) % 2 == 0 ? (byte)0xEC : (byte)0x11;   // pad bytes, alternating
        return [.. data, .. ErrorCorrection(data, CheckWords[version])];
    }

    /// <summary>Reed-Solomon error correction codewords over GF(256) (the polynomial x⁸+x⁴+x³+x²+1).</summary>
    public static byte[] ErrorCorrection(byte[] data, int count)
    {
        var divisor = new byte[count];
        divisor[count - 1] = 1;
        byte root = 1;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                divisor[j] = Multiply(divisor[j], root);
                if (j + 1 < count)
                    divisor[j] ^= divisor[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        var result = new byte[count];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, count - 1);
            result[count - 1] = 0;
            for (var i = 0; i < count; i++)
                result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    private static byte Multiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = z << 1 ^ (z >> 7) * 0x11D;
            z ^= (y >> i & 1) * x;
        }
        return (byte)z;
    }

    /// <summary>The finder patterns (with their separators), the timing patterns and the alignment pattern.</summary>
    private static void DrawPatterns(bool[,] modules, bool[,] function, int version)
    {
        var size = modules.GetLength(0);
        for (var i = 0; i < size; i++)
        {
            Set(modules, function, 6, i, i % 2 == 0);
            Set(modules, function, i, 6, i % 2 == 0);
        }
        foreach (var (row, column) in new[] { (3, 3), (3, size - 4), (size - 4, 3) })
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var (r, c) = (row + dy, column + dx);
                    var ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (r >= 0 && r < size && c >= 0 && c < size)
                        Set(modules, function, r, c, ring != 2 && ring != 4);
                }
        if (version >= 2)
        {
            var at = 4 * version + 10;                      // 18, 22, 26, 30: the one alignment pattern clear of the finders
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    Set(modules, function, at + dy, at + dx, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }
    }

    /// <summary>Both copies of the format bits, and the dark module beside the lower left finder.</summary>
    private static void DrawFormat(bool[,] modules, bool[,] function, int mask)
    {
        var size = modules.GetLength(0);
        var bits = FormatBits(Level, mask);
        bool Bit(int i) => (bits >> i & 1) != 0;
        // first copy, around the top left finder (x is the column, y the row)
        for (var i = 0; i <= 5; i++)
            Set(modules, function, i, 8, Bit(i));
        Set(modules, function, 7, 8, Bit(6));
        Set(modules, function, 8, 8, Bit(7));
        Set(modules, function, 8, 7, Bit(8));
        for (var i = 9; i < 15; i++)
            Set(modules, function, 8, 14 - i, Bit(i));
        // second copy, split between the top right and the bottom left finders
        for (var i = 0; i < 8; i++)
            Set(modules, function, 8, size - 1 - i, Bit(i));
        for (var i = 8; i < 15; i++)
            Set(modules, function, size - 15 + i, 8, Bit(i));
        Set(modules, function, size - 8, 8, true);
    }

    /// <summary>The codewords' bits, in pairs of columns from the right, going up and down in turn, skipping the timing column.</summary>
    private static void Place(bool[,] modules, bool[,] function, byte[] codewords)
    {
        var size = modules.GetLength(0);
        var i = 0;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
                right = 5;
            var upward = (right + 1 & 2) == 0;
            for (var vertical = 0; vertical < size; vertical++)
                for (var j = 0; j < 2; j++)
                {
                    var column = right - j;
                    var row = upward ? size - 1 - vertical : vertical;
                    if (function[row, column])
                        continue;
                    modules[row, column] = i < codewords.Length * 8 && (codewords[i >> 3] >> (7 - (i & 7)) & 1) != 0;
                    i++;                                    // past the codewords: the remainder bits, light
                }
        }
    }

    private static void Mask(bool[,] modules, bool[,] function, int mask)
    {
        var size = modules.GetLength(0);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (function[y, x])
                    continue;
                var flip = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (flip)
                    modules[y, x] = !modules[y, x];
            }
    }

    /// <summary>The standard's penalty for a masked code: long runs, 2×2 blocks, finder-like stretches, and dark and light out of balance.</summary>
    private static int Penalty(bool[,] modules)
    {
        var size = modules.GetLength(0);
        var penalty = 0;
        bool Get(int row, int column, bool across) => across ? modules[row, column] : modules[column, row];
        foreach (var across in new[] { true, false })
            for (var line = 0; line < size; line++)
            {
                var run = 1;
                for (var i = 1; i < size; i++)
                {
                    if (Get(line, i, across) == Get(line, i - 1, across))
                    {
                        run++;
                        if (run == 5)
                            penalty += 3;
                        else if (run > 5)
                            penalty++;
                    }
                    else
                        run = 1;
                }
                for (var i = 0; i + 10 < size; i++)
                {
                    var pattern = Enumerable.Range(0, 11).Select(k => Get(line, i + k, across)).ToArray();
                    if (pattern.SequenceEqual(FinderLike) || pattern.SequenceEqual(FinderLike.Reverse()))
                        penalty += 40;
                }
            }
        for (var y = 0; y + 1 < size; y++)
            for (var x = 0; x + 1 < size; x++)
                if (modules[y, x] == modules[y, x + 1] && modules[y, x] == modules[y + 1, x] && modules[y, x] == modules[y + 1, x + 1])
                    penalty += 3;
        var dark = 0;
        foreach (var module in modules)
            if (module)
                dark++;
        var total = size * size;
        penalty += (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1 is var k and > 0 ? k * 10 : 0;
        return penalty;
    }

    private static readonly bool[] FinderLike = [true, false, true, true, true, false, true, false, false, false, false];

    private static void Set(bool[,] modules, bool[,] function, int row, int column, bool dark)
    {
        modules[row, column] = dark;
        function[row, column] = true;
    }
}
