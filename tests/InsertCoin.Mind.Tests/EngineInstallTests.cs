using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace InsertCoin.Mind.Tests;

public class EngineInstallTests : IDisposable
{
    // Made with Windows' tar (--format 7zip, LZMA2) as GPT-SoVITS's package is packed: one with a top folder
    // (GPT-SoVITS-test: api_v2.py, runtime/python.exe, GPT_SoVITS/inference.py), one without (api_v2.py, runtime/python.exe).
    private const string WithTopFolder = "N3q8ryccAAPDsA+eAwEAAAAAAAAcAAAAAAAAAJ4S6f8BACNwcmludCgnaGknKQpub3QgcmVhbGx5IHB5dGhvbgp4ID0gMQoA4AI8ANNdAACBMweuD9CHwjyfP0dBBAQyev+CcIgaZbt9H4uTEF4sHjnvLmO906ClU5gdv54Kn+Jk0i100rR/7uzinIeLaT7Drl3TXnavB2wEyy5H12e4lTpLEuSA/FChl50vEAZ18ISNbof8nO2lSGfALiy5nfRRKaVmOrFYbHVT5FgQyyLuYeR2VEarS8HVjk5lGsu6EoPow2YUbIv05td+RgT4pl0lJ+8yF0Gq6bXLgk7J/JmBBk8ZeRDi5JLYPFFCtNN7bEwUQUjSsmyiAtqZfRJzvqSwAAAAFwYoAQmA2wAHCwEAASEhARYMgj0KAeJv+xkAAA==";
    private const string WithoutTopFolder = "N3q8ryccAAOR40nbuQAAAAAAAAAcAAAAAAAAADIzNIcBAB1wcmludCgnaGknKQpub3QgcmVhbGx5IHB5dGhvbgoA4ADcAI9dAACBMweuD9As9LyfP0dBBv7JQ0bO58Iy4IqmZ35l9R7raOwBeQaAqo6baaP+vNUMbggKxUUYM12jlxj6+gMU1WHHliIyf/bZVgygLR2gOEN4cg1brt+Fhjmo/cz6kJ7juq5nJNXched7Emxs+W6dUeQZ9L6hUoiWie1kY38Nf1qJGkGR208ohTBZHFnHAAAAABcGIgEJgJcABwsBAAEhIQEWDIDdCgGLFsFIAAA=";

    private static readonly Uri Package = new("https://example.test/package.7z");
    private readonly string _dir = Directory.CreateTempSubdirectory("engine-install-").FullName;

    public void Dispose() => V5Fast.Remove(_dir);

    private sealed class Listener<T>(Action<T>? heard = null) : IProgress<T>
    {
        public T? Last { get; private set; }
        public void Report(T value)
        {
            Last = value;
            heard?.Invoke(value);
        }
    }

    /// <summary>Serves one file, with ranges unless told not to; its first answer can break off after so many bytes.</summary>
    private sealed class FakeHost(byte[] file) : HttpMessageHandler
    {
        public int? DropAfter { get; set; }
        public bool NoRanges { get; set; }
        public bool AlwaysDrops { get; set; }
        public List<long?> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var from = NoRanges ? 0 : request.Headers.Range?.Ranges.Single().From ?? 0;
            Asked.Add(request.Headers.Range?.Ranges.Single().From);
            var body = file[(int)from..];
            Stream stream = DropAfter is { } after ? new Breaking(body, after) : new MemoryStream(body);
            if (!AlwaysDrops)
                DropAfter = null;
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new StreamContent(stream) };
            if (from > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, file.Length - 1, file.Length);
            return Task.FromResult(response);
        }
    }

    /// <summary>A body that breaks off, as a dropped connection does, after so many bytes.</summary>
    private sealed class Breaking(byte[] body, int after) : MemoryStream(body)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= after)
                throw new IOException("connection reset");
            return base.Read(buffer, offset, (int)Math.Min(count, after - Position));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            if (Position >= after)
                throw new IOException("connection reset");
            return base.ReadAsync(buffer[..(int)Math.Min(buffer.Length, after - Position)], cancel);
        }
    }

    private static byte[] Bytes(int count) => [.. Enumerable.Range(0, count).Select(i => (byte)(i * 31 % 251))];

    [Fact]
    public void The_package_is_the_build_for_the_graphics_card()
    {
        Assert.Same(EnginePackage.Blackwell, EnginePackage.For("NVIDIA GeForce RTX 5070 Ti"));
        Assert.Same(EnginePackage.Blackwell, EnginePackage.For("NVIDIA GeForce RTX 5090 Laptop GPU"));
        Assert.Same(EnginePackage.Blackwell, EnginePackage.For("NVIDIA RTX PRO 6000 Blackwell Workstation Edition"));
        Assert.Same(EnginePackage.Standard, EnginePackage.For("NVIDIA GeForce RTX 3060 Laptop GPU"));
        Assert.Same(EnginePackage.Standard, EnginePackage.For("NVIDIA GeForce RTX 3050"));
        Assert.Same(EnginePackage.Standard, EnginePackage.For(null));
        Assert.Equal("https://huggingface.co/lj1995/GPT-SoVITS-windows-package/resolve/main/GPT-SoVITS-v2pro-20250604.7z",
                     EnginePackage.Standard.From(EngineSource.HuggingFace).ToString());
        Assert.Equal("https://hf-mirror.com/lj1995/GPT-SoVITS-windows-package/resolve/main/GPT-SoVITS-v2pro-20250604-nvidia50.7z",
                     EnginePackage.Blackwell.From(EngineSource.HfMirror).ToString());
    }

    [Fact]
    public async Task A_download_cut_off_carries_on_from_where_it_got_to()
    {
        var file = Bytes(3_000_000);
        var host = new FakeHost(file) { DropAfter = 1_200_000 };
        var path = Path.Combine(_dir, "package.7z");
        var heard = new Listener<long>();

        await EngineInstall.Fetch(new HttpClient(host), Package, path, file.Length, heard, CancellationToken.None, backOff: TimeSpan.Zero);

        Assert.Equal(file, File.ReadAllBytes(path));
        Assert.Equal([null, 1_200_000], host.Asked);       // the second time from where the first broke off
        Assert.Equal(file.Length, heard.Last);
    }

    [Fact]
    public async Task A_download_left_from_an_earlier_time_is_carried_on_and_one_already_whole_is_not_fetched_again()
    {
        var file = Bytes(500_000);
        var path = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(path, file[..200_000]);
        var host = new FakeHost(file);

        await EngineInstall.Fetch(new HttpClient(host), Package, path, file.Length, new Listener<long>(), CancellationToken.None);
        await EngineInstall.Fetch(new HttpClient(host), Package, path, file.Length, new Listener<long>(), CancellationToken.None);

        Assert.Equal(file, File.ReadAllBytes(path));
        Assert.Equal([200_000], host.Asked);
    }

    [Fact]
    public async Task A_server_that_sends_the_whole_file_again_is_written_from_the_start()
    {
        var file = Bytes(400_000);
        var path = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(path, file[..100_000]);

        await EngineInstall.Fetch(new HttpClient(new FakeHost(file) { NoRanges = true }), Package, path, file.Length, new Listener<long>(), CancellationToken.None);

        Assert.Equal(file, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_download_that_keeps_failing_gives_up_and_keeps_what_it_has()
    {
        var file = Bytes(400_000);
        var path = Path.Combine(_dir, "package.7z");
        var host = new FakeHost(file) { DropAfter = 0, AlwaysDrops = true };

        await Assert.ThrowsAsync<IOException>(() =>
            EngineInstall.Fetch(new HttpClient(host), Package, path, file.Length, new Listener<long>(), CancellationToken.None, tries: 3, backOff: TimeSpan.Zero));
        Assert.Equal(3, host.Asked.Count);
    }

    [Fact]
    public async Task A_download_is_checked_against_its_published_fingerprint()
    {
        var file = Bytes(300_000);
        var path = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(path, file);
        var heard = new Listener<long>();

        Assert.True(await EngineInstall.Matches(path, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), heard, CancellationToken.None));
        Assert.Equal(file.Length, heard.Last);
        file[1000] ^= 1;
        Assert.False(await EngineInstall.Matches(path, Convert.ToHexString(SHA256.HashData(file)), heard, CancellationToken.None));
    }

    [Fact]
    public void The_package_unpacks_into_a_folder_of_its_own_and_works_as_GPT_SoVITS()
    {
        var archive = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(archive, Convert.FromBase64String(WithTopFolder));
        Directory.CreateDirectory(Path.Combine(_dir, "GPT-SoVITS-pkg.unpacking", "left from a time cut short"));
        var heard = new Listener<double>();

        var folder = EngineInstall.Unpack(archive, _dir, "GPT-SoVITS-pkg", heard, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "GPT-SoVITS-test"), folder);
        Assert.Null(EngineInstall.Problem(folder));
        Assert.Equal("x = 1\n", File.ReadAllText(Path.Combine(folder, "GPT_SoVITS", "inference.py")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "GPT-SoVITS-pkg.unpacking")));
        Assert.Equal(1, heard.Last, 3);
        Assert.Throws<IOException>(() => EngineInstall.Unpack(archive, _dir, "GPT-SoVITS-pkg", heard, CancellationToken.None));     // never over one there
    }

    [Fact]
    public void Unpacking_a_big_file_says_how_far_it_has_got_on_the_way()
    {
        // big/model.bin: 3 MB of zeros, packed the same way
        const string big = "N3q8ryccAAMmREvhkAIAAAAAAAAcAAAAAAAAADRnO9///xEBbF0AAG/9//+jt/9HPkgVcjlhUbiSKOajhgf57uQegtMvxTo8AUuxfsmKik0vow3Zf6bjjCMRU+BZGMV1iuJ3+LaUfwxqwN50SWTi6VxTsgTY90QMq18NbUbp5cN2iLeWV6y2TeFpHW/7S4gQbELLiD9cAI/QTq8mKJRxHz2PJOFwnqcjX+woy4XRlZiKfiqR8id19xnABphNmP3Yr9WQD8QlU/j1kTYxBaWw7m/BcE1HDNGREaqtYB26zrEnGFxZhulmUli+6XasWeTlWwUI+cfarfz7Uit0zR5bIEL53VM9+ClkCTuAyyps37U78MS9Ll+qDz5LZkKQEw7/EJP4cXhZ+AvN/5UoRg+p/Hze+5owLlbAj4Xzg4HAZcQlU/j1kTYxBaWw7m/BcE1HDNGREaqtYB26zrEnGFxZhulmUli+6XasWeTlWwUI+cfarfz7Uit0zR5bIEL53VM9+ClkCTuAyyps37U78MS8SCfmWJAA7QCZAOxzU6f9vq58MRqft40xbnCepyNf7CjLhdGVmIp+KpHyJ3X3GcAGmE2Y/div1ZAPxCVT+PWRNjEFpbDub8FwTUcM0ZERqq1gHbrOsScYXFmG6WZSWL7pdqxZ5OVbBQj5x9qt/PtSK3TNHlsgQvndUz34KWQJO4DLKmzftTvwxL0uX6oPPktmQpATDv8Qk/hxeFn4C8yw4tkAAADgAJYAdV0AAIEzB64P1UM+GfnGZtqN8+3Q7hxv9NNJ/InwPGZJ5TmeMD4a08rYRsYjn/zbBtWNGMrtVBkqpGQC6yIcMI3+fHkrbcjxoS+HMwVVvK/Jcl25hbkUWPhAGTTUAjh6fYUcsIS3/7JMMr+hHiHB6PQ7vhggAAAAABcGghMBCX0ABwsBAAEhIQEWDICXCgFu+iGmAAA=";
        var archive = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(archive, Convert.FromBase64String(big));
        var heard = new List<double>();

        var folder = EngineInstall.Unpack(archive, _dir, "pkg", new Listener<double>(heard.Add), CancellationToken.None);

        Assert.Equal(3 << 20, new FileInfo(Path.Combine(folder, "model.bin")).Length);
        Assert.Contains(heard, share => share is > 0.2 and < 0.9);      // partway through the one file, not only at its end
        Assert.Equal(1, heard[^1], 3);
    }

    [Fact]
    public void A_package_without_a_top_folder_unpacks_into_one_named_for_it()
    {
        var archive = Path.Combine(_dir, "package.7z");
        File.WriteAllBytes(archive, Convert.FromBase64String(WithoutTopFolder));

        var folder = EngineInstall.Unpack(archive, _dir, "GPT-SoVITS-pkg", new Listener<double>(), CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "GPT-SoVITS-pkg"), folder);
        Assert.Null(EngineInstall.Problem(folder));
    }

    [Fact]
    public void An_install_already_on_the_PC_is_found_where_people_put_it()
    {
        void Install(params string[] path)
        {
            var folder = Path.Combine([_dir, .. path]);
            Directory.CreateDirectory(Path.Combine(folder, "runtime"));
            File.WriteAllText(Path.Combine(folder, "api_v2.py"), "");
            File.WriteAllText(Path.Combine(folder, "runtime", "python.exe"), "");
        }
        Install("Tools", "voice", "GPT-SoVITS-v2pro-20250604");   // three folders down: found
        Install("sovits stuff", "gptsovits");                       // in a folder named for it that isn't one
        Install("a", "b", "c", "GPT-SoVITS-deep");                  // too deep
        Install("AppData", "GPT-SoVITS-hidden");                    // not looked in
        Install("Tools", "my voice engine");                        // not named for it
        Directory.CreateDirectory(Path.Combine(_dir, "Other", "GPT-SoVITS-unpacked-wrong", "GPT-SoVITS-v2pro"));

        var found = EngineInstall.Installed([_dir]).Select(f => Path.GetRelativePath(_dir, f)).Order().ToList();

        Assert.Equal([Path.Combine("sovits stuff", "gptsovits"), Path.Combine("Tools", "voice", "GPT-SoVITS-v2pro-20250604")], found);
    }

    // ---- v5 fast's files ------------------------------------------------------------------------------------------------

    /// <summary>Stands in for Hugging Face and GitHub: each file by the last part of its address.</summary>
    private sealed class Hosts(params (string Name, FakeHost Host)[] hosts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            new HttpMessageInvoker(hosts.Single(h => h.Name == request.RequestUri!.Segments[^1]).Host).SendAsync(request, cancel);
    }

    private static readonly Uri CodeZip = new("https://github.test/code.zip");
    private static Uri WeightFrom(string name) => new($"https://hf.test/{name}");

    private static V5Fast.Weight Weight(string name, byte[] file) => new(name, file.Length, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());

    /// <summary>GitHub's kind of zip: everything in one top folder, as checked in (LF line endings).</summary>
    private static byte[] Code()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in new[]
                     {
                         ("api_v2.py", "print('v5')\n"), ("GPT_SoVITS/pretrained_models/.gitignore", "*\n"), ("GPT_SoVITS/text/symbols.py", "a = 1\n"), ("tools/portable_runtime.py", "b = 2\n"),
                     })
                using (var writer = new StreamWriter(zip.CreateEntry("GPT-SoVITS-b4570b8/" + name).Open()))
                    writer.Write(text);
        return memory.ToArray();
    }

    private static string CodeHash(byte[] zip)
    {
        var folder = Directory.CreateTempSubdirectory("engine-install-code-").FullName;
        try
        {
            using (var archive = new ZipArchive(new MemoryStream(zip)))
                archive.ExtractToDirectory(folder);
            return V5Fast.ContentHash(Directory.GetDirectories(folder).Single());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A package as the cabinet installs it, without the gigabytes.</summary>
    private string Engine(string name = "GPT-SoVITS-v2pro-20250604")
    {
        var engine = Path.Combine(_dir, name);
        foreach (var folder in new[] { "runtime", "GPT_SoVITS/pretrained_models/v2Pro", "GPT_SoVITS/text/G2PWModel" })
            Directory.CreateDirectory(Path.Combine(engine, folder));
        File.WriteAllText(Path.Combine(engine, "api_v2.py"), "print('old')");
        File.WriteAllText(Path.Combine(engine, "runtime", "python.exe"), "");
        File.WriteAllText(Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "v2Pro", "s2Gv2ProPlus.pth"), "the package's own");
        File.WriteAllText(Path.Combine(engine, "GPT_SoVITS", "text", "G2PWModel", "g2pw.onnx"), "the package's own");
        return engine;
    }

    private static Task Install(string engine, Hosts hosts, IReadOnlyList<V5Fast.Weight> weights, byte[] zip) =>
        V5Fast.Install(new HttpClient(hosts), engine, WeightFrom, weights, CodeZip, CodeHash(zip), new Listener<(EngineStep, long, long)>(), CancellationToken.None, TimeSpan.Zero);

    [Fact]
    public async Task V5_fast_comes_in_beside_the_package_and_shares_its_models_rather_than_copying_them()
    {
        var engine = Engine();
        var (turbo, vocoder, zip) = (Bytes(200_000), Bytes(50_000), Code());
        V5Fast.Weight[] weights = [Weight("s2Gv5turbo.pth", turbo), Weight("vocoder.pth", vocoder)];
        var hosts = new Hosts(("s2Gv5turbo.pth", new FakeHost(turbo)), ("vocoder.pth", new FakeHost(vocoder)), ("code.zip", new FakeHost(zip)));

        Assert.False(V5Fast.Has(engine, weights));
        await Install(engine, hosts, weights, zip);

        Assert.True(V5Fast.Has(engine, weights));
        var code = V5Fast.Code(engine);
        Assert.Equal("print('v5')\n", File.ReadAllText(Path.Combine(code, "api_v2.py")));
        Assert.Equal("print('old')", File.ReadAllText(Path.Combine(engine, "api_v2.py")));                 // the package as it was
        Assert.Equal(turbo, File.ReadAllBytes(Path.Combine(code, "GPT_SoVITS", "pretrained_models", "gsv-v5-pretrained", "s2Gv5turbo.pth")));    // through the link
        Assert.Equal("the package's own", File.ReadAllText(Path.Combine(code, "GPT_SoVITS", "pretrained_models", "v2Pro", "s2Gv2ProPlus.pth")));
        Assert.Equal("the package's own", File.ReadAllText(Path.Combine(code, "GPT_SoVITS", "text", "G2PWModel", "g2pw.onnx")));
        Assert.True(File.Exists(Path.Combine(code, "runtime", "python.exe")));
        Assert.False(File.Exists(code + ".zip.part"));     // nothing left behind
        Assert.False(Directory.Exists(code + ".unpacking"));
    }

    [Fact]
    public async Task A_download_of_v5_fast_cut_off_leaves_the_engine_as_it_was_and_carries_on_next_time()
    {
        var engine = Engine();
        var (turbo, zip) = (Bytes(3_000_000), Code());
        V5Fast.Weight[] weights = [Weight("s2Gv5turbo.pth", turbo)];
        var kept = Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "gsv-v5-pretrained");
        Directory.CreateDirectory(kept);
        File.WriteAllBytes(Path.Combine(kept, "s2Gv5turbo.pth.part"), turbo[..1_200_000]);      // what an earlier try got
        var host = new FakeHost(turbo) { DropAfter = 0, AlwaysDrops = true };
        var hosts = new Hosts(("s2Gv5turbo.pth", host), ("code.zip", new FakeHost(zip)));

        await Assert.ThrowsAsync<IOException>(() => V5Fast.Install(new HttpClient(hosts), engine, WeightFrom, weights, CodeZip, CodeHash(zip),
                                                                   new Listener<(EngineStep, long, long)>(), CancellationToken.None, TimeSpan.Zero));
        Assert.False(V5Fast.Has(engine, weights));
        Assert.False(File.Exists(Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "gsv-v5-pretrained", "s2Gv5turbo.pth")));
        Assert.False(Directory.Exists(V5Fast.Code(engine)));

        host.AlwaysDrops = false;
        host.DropAfter = null;
        await Install(engine, hosts, weights, zip);

        Assert.True(V5Fast.Has(engine, weights));
        Assert.Equal(1_200_000, host.Asked[^1]);           // from where the last try got to
    }

    [Fact]
    public async Task A_weight_that_doesnt_match_is_thrown_away_and_nothing_is_put_in_place()
    {
        var engine = Engine();
        var (turbo, zip) = (Bytes(100_000), Code());
        V5Fast.Weight[] weights = [new("s2Gv5turbo.pth", turbo.Length, new string('0', 64))];
        var hosts = new Hosts(("s2Gv5turbo.pth", new FakeHost(turbo)), ("code.zip", new FakeHost(zip)));

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(engine, hosts, weights, zip));

        Assert.Empty(Directory.GetFiles(Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "gsv-v5-pretrained")));
        Assert.False(V5Fast.Has(engine, weights));
    }

    [Fact]
    public async Task Code_that_doesnt_match_is_thrown_away_and_the_engine_speaks_as_before()
    {
        var engine = Engine();
        var (turbo, zip) = (Bytes(100_000), Code());
        V5Fast.Weight[] weights = [Weight("s2Gv5turbo.pth", turbo)];
        var hosts = new Hosts(("s2Gv5turbo.pth", new FakeHost(turbo)), ("code.zip", new FakeHost(zip)));

        await Assert.ThrowsAsync<InvalidDataException>(() => V5Fast.Install(new HttpClient(hosts), engine, WeightFrom, weights, CodeZip, new string('0', 64),
                                                                            new Listener<(EngineStep, long, long)>(), CancellationToken.None, TimeSpan.Zero));

        Assert.False(V5Fast.Has(engine, weights));
        Assert.False(Directory.Exists(V5Fast.Code(engine)));
        Assert.False(Directory.Exists(V5Fast.Code(engine) + ".unpacking"));
        Assert.Null(EngineInstall.Problem(engine));
    }

    [Fact]
    public async Task Unpacking_cut_short_starts_again_and_takes_only_its_links_away_not_what_they_lead_to()
    {
        var engine = Engine();
        var (turbo, zip) = (Bytes(100_000), Code());
        V5Fast.Weight[] weights = [Weight("s2Gv5turbo.pth", turbo)];
        var hosts = new Hosts(("s2Gv5turbo.pth", new FakeHost(turbo)), ("code.zip", new FakeHost(zip)));
        var left = V5Fast.Code(engine) + ".unpacking";
        Directory.CreateDirectory(left);
        var link = Path.Combine(left, "runtime");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd")
               {
                   ArgumentList = { "/c", "mklink", "/J", link, Path.Combine(engine, "runtime") }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
               })!)
        {
            mklink.StandardOutput.ReadToEnd();
            mklink.WaitForExit();
        }
        Assert.True(File.Exists(Path.Combine(link, "python.exe")));

        await Install(engine, hosts, weights, zip);

        Assert.True(V5Fast.Has(engine, weights));
        Assert.True(File.Exists(Path.Combine(engine, "runtime", "python.exe")));
    }

    [Fact]
    public async Task Code_with_no_promised_size_is_fetched_whole_each_time()
    {
        var file = Bytes(100_000);
        var path = Path.Combine(_dir, "code.zip.part");
        File.WriteAllBytes(path, file[..1000]);             // left from a try cut short, and of no use: its size isn't known
        var host = new FakeHost(file);

        await EngineInstall.Fetch(new HttpClient(host), Package, path, null, new Listener<long>(), CancellationToken.None);

        Assert.Equal(file, File.ReadAllBytes(path));
        Assert.Equal([null], host.Asked);
    }

    [Fact]
    public void Only_the_package_the_cabinet_installs_can_have_v5_fast_added()
    {
        Assert.True(V5Fast.CanAdd(Engine()));
        Assert.False(V5Fast.CanAdd(Engine("GPT-SoVITS-v2pro-20250604-nvidia50")));         // its own Python and torch, untried
        Assert.False(V5Fast.CanAdd(Engine("a folder the player chose")));
        Assert.False(V5Fast.CanAdd(Path.Combine(_dir, "nothing here")));
    }

    [Fact]
    public void The_download_the_player_agrees_to_is_added_up_in_one_place()
    {
        Assert.Equal(EnginePackage.Standard.Size + V5Fast.Download, EnginePackage.Standard.Download);
        Assert.Equal(EnginePackage.Blackwell.Size, EnginePackage.Blackwell.Download);
        Assert.InRange(V5Fast.Download, 824_167_307, 900_000_000);
    }

    [Fact]
    public void V5_fast_stays_pinned_to_the_commit_that_really_streams()
    {
        // later versions accept gpt_streaming and silently make whole segments: moving this needs streaming heard again
        Assert.Equal("b4570b876f5c086819ba84e3215472733c6a7b70", V5Fast.Commit);
    }

    [Fact]
    public void The_code_is_checked_by_its_contents_whatever_its_line_endings()
    {
        void Write(string folder, string text)
        {
            Directory.CreateDirectory(Path.Combine(folder, "tools"));
            File.WriteAllText(Path.Combine(folder, "tools", "a.py"), text);
            File.WriteAllBytes(Path.Combine(folder, "model.bin"), [1, 13, 2]);          // a lone carriage return stays
        }
        var (lf, crlf, other) = (Path.Combine(_dir, "lf"), Path.Combine(_dir, "crlf"), Path.Combine(_dir, "other"));
        Write(lf, "x = 1\ny = 2\n");
        Write(crlf, "x = 1\r\ny = 2\r\n");
        Write(other, "x = 1\ny = 3\n");

        Assert.Equal(V5Fast.ContentHash(lf), V5Fast.ContentHash(crlf));
        Assert.NotEqual(V5Fast.ContentHash(lf), V5Fast.ContentHash(other));
    }
}
