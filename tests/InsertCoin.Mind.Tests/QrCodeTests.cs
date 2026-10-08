using System.Text;

namespace InsertCoin.Mind.Tests;

public class QrCodeTests
{
    [Fact]
    public void Error_correction_matches_the_standards_worked_example()
    {
        // "HELLO WORLD" at version 1-M, as worked through in the well-known QR tutorials
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];

        Assert.Equal([196, 35, 39, 119, 235, 215, 231, 226, 93, 23], QrCode.ErrorCorrection(data, 10));
    }

    [Theory]
    [InlineData(1, 0, 0b111011111000100)]                 // L, mask 0
    [InlineData(0, 0, 0b101010000010010)]                 // M, mask 0
    public void Format_bits_match_the_standards_table(int level, int mask, int bits) =>
        Assert.Equal(bits, QrCode.FormatBits(level, mask));

    [Fact]
    public void Every_format_code_is_a_codeword_of_its_BCH_code()
    {
        for (var level = 0; level < 4; level++)
            for (var mask = 0; mask < 8; mask++)
            {
                var code = QrCode.FormatBits(level, mask) ^ 0x5412;
                for (var bit = 14; bit >= 10; bit--)
                    if ((code >> bit & 1) != 0)
                        code ^= 0x537 << (bit - 10);
                Assert.Equal(0, code);
                Assert.Equal(level << 3 | mask, (QrCode.FormatBits(level, mask) ^ 0x5412) >> 10);
            }
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("http://192.168.1.23:48213/p/Zk3qW9x_Lm2aB7cD")]
    [InlineData("http://192.168.100.200:65535/p/0123456789abcdef0123456789abcdef")]
    [InlineData("A photo for Shino: 写真を送るね")]
    public void A_code_reads_back_to_its_text(string text)
    {
        var modules = QrCode.Encode(text);

        Assert.Equal(text, Read(modules));
    }

    [Fact]
    public void A_code_has_its_finders_timing_and_dark_module_where_scanners_look()
    {
        var modules = QrCode.Encode("http://192.168.1.23:48213/p/Zk3qW9x_Lm2aB7cD");
        var size = modules.GetLength(0);

        Assert.Equal(29, size);                             // 44 bytes: version 3
        foreach (var (top, left) in new[] { (0, 0), (0, size - 7), (size - 7, 0) })
            for (var y = 0; y < 7; y++)
                for (var x = 0; x < 7; x++)
                {
                    var ring = Math.Max(Math.Abs(y - 3), Math.Abs(x - 3));
                    Assert.Equal(ring != 2, modules[top + y, left + x]);
                }
        for (var i = 8; i < size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, modules[6, i]);
            Assert.Equal(i % 2 == 0, modules[i, 6]);
        }
        Assert.True(modules[size - 8, 8]);
    }

    [Fact]
    public void Text_too_long_for_a_small_code_is_refused() =>
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('x', 107)));

    // ---- a decoder of its own, written from the standard independently of the encoder ----

    private static string Read(bool[,] modules)
    {
        var size = modules.GetLength(0);
        var version = (size - 17) / 4;

        // the format bits, both copies, which must agree
        int first = 0, second = 0;
        int[] firstRows = [0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8];
        int[] firstColumns = [8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0];
        for (var i = 0; i < 15; i++)
        {
            if (modules[firstRows[i], firstColumns[i]])
                first |= 1 << i;
            var (row, column) = i < 8 ? (8, size - 1 - i) : (size - 15 + i, 8);
            if (modules[row, column])
                second |= 1 << i;
        }
        Assert.Equal(first, second);
        var format = (first ^ 0x5412) >> 10;
        Assert.Equal(1, format >> 3);                       // level L
        var mask = format & 7;

        // which modules hold data: everything but the finders, separators, format areas, timing and alignment
        bool Reserved(int y, int x)
        {
            if (y <= 8 && x <= 8 || y <= 8 && x >= size - 8 || y >= size - 8 && x <= 8)
                return true;
            if (y == 6 || x == 6)
                return true;
            var at = 4 * version + 10;
            return version >= 2 && Math.Abs(y - at) <= 2 && Math.Abs(x - at) <= 2;
        }
        bool Flipped(int y, int x) => mask switch
        {
            0 => (y + x) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (y + x) % 3 == 0,
            4 => (y / 2 + x / 3) % 2 == 0,
            5 => y * x % 2 + y * x % 3 == 0,
            6 => (y * x % 2 + y * x % 3) % 2 == 0,
            _ => ((y + x) % 2 + y * x % 3) % 2 == 0,
        };

        // the bits in placement order: column pairs from the right, up then down, the timing column skipped
        var bits = new List<bool>();
        var up = true;
        for (var column = size - 1; column > 0; column -= 2)
        {
            if (column == 6)
                column--;
            for (var step = 0; step < size; step++)
            {
                var y = up ? size - 1 - step : step;
                foreach (var x in new[] { column, column - 1 })
                    if (!Reserved(y, x))
                        bits.Add(modules[y, x] ^ Flipped(y, x));
            }
            up = !up;
        }
        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < codewords.Length * 8; i++)
            if (bits[i])
                codewords[i / 8] |= (byte)(0x80 >> i % 8);

        // Reed-Solomon: the whole block, read as a polynomial, vanishes at α⁰ … α^(n−1)
        int[] check = [0, 7, 10, 15, 20, 26];
        int[] total = [0, 26, 44, 70, 100, 134];
        var block = codewords[..total[version]];
        var exp = new int[256];
        var log = new int[256];
        for (int i = 0, v = 1; i < 255; i++, v = v << 1 ^ (v >= 128 ? 0x11D : 0))
            (exp[i], log[v]) = (v, i);
        int Times(int a, int b) => a == 0 || b == 0 ? 0 : exp[(log[a] + log[b]) % 255];
        for (var i = 0; i < check[version]; i++)
        {
            var value = 0;
            foreach (var c in block)
                value = Times(value, exp[i]) ^ c;
            Assert.Equal(0, value);
        }

        // byte mode: 0100, a count, the bytes
        var data = block[..(total[version] - check[version])];
        int Take(int from, int length)
        {
            var value = 0;
            for (var i = from; i < from + length; i++)
                value = value << 1 | (data[i / 8] >> (7 - i % 8) & 1);
            return value;
        }
        Assert.Equal(0b0100, Take(0, 4));
        var count = Take(4, 8);
        return Encoding.UTF8.GetString([.. Enumerable.Range(0, count).Select(i => (byte)Take(12 + 8 * i, 8))]);
    }
}
