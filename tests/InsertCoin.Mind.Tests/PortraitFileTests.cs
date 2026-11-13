namespace InsertCoin.Mind.Tests;

public class PortraitFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("portraits-").FullName;
    private string Kept => Path.Combine(_dir, "portraits");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string text, int minutesAgo = 10)
    {
        var file = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 11, 13, 4, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));
        return file;
    }

    [Fact]
    public void A_portrait_is_kept_while_the_model_stays_the_same()
    {
        var model = Write("hana.vrm", "model");
        Assert.Equal(PortraitFile.For(Kept, "hana", model), PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void A_model_replaced_by_another_is_made_again()
    {
        var model = Write("hana.vrm", "model");
        var before = PortraitFile.For(Kept, "hana", model);
        Write("hana.vrm", "another model", minutesAgo: 1);
        Assert.NotEqual(before, PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void A_model_rewritten_at_the_same_size_is_made_again()
    {
        var model = Write("hana.vrm", "model");
        var before = PortraitFile.For(Kept, "hana", model);
        Write("hana.vrm", "MODEL", minutesAgo: 1);
        Assert.NotEqual(before, PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void A_texture_beside_a_model_in_its_own_folder_changing_makes_it_again()
    {
        var model = Write(Path.Combine("hana", "hana.gltf"), "{}");
        Write(Path.Combine("hana", "textures", "skin.png"), "skin");
        var before = PortraitFile.For(Kept, "hana", model);
        Write(Path.Combine("hana", "textures", "skin.png"), "a new skin", minutesAgo: 1);
        Assert.NotEqual(before, PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void A_new_card_or_persona_beside_the_model_keeps_the_portrait()
    {
        var model = Write(Path.Combine("hana", "hana.gltf"), "{}");
        var before = PortraitFile.For(Kept, "hana", model);
        Write(Path.Combine("hana", "card.png"), "card", minutesAgo: 1);
        Write(Path.Combine("hana", "persona.md"), "who they are", minutesAgo: 1);
        Assert.Equal(before, PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void Another_file_in_the_characters_folder_keeps_a_loose_models_portrait()
    {
        var model = Write("hana.vrm", "model");
        var before = PortraitFile.For(Kept, "hana", model);
        Write("other.vrm", "someone else", minutesAgo: 1);
        Assert.Equal(before, PortraitFile.For(Kept, "hana", model));
    }

    [Fact]
    public void One_built_into_the_app_is_made_again_with_a_new_build()
    {
        var model = Write("shino.vrm", "model");
        Assert.NotEqual(PortraitFile.For(Kept, "shino", model, "build 1"), PortraitFile.For(Kept, "shino", model, "build 2"));
    }

    [Fact]
    public void Each_character_has_a_portrait_of_their_own()
    {
        var model = Write("hana.vrm", "model");
        Assert.NotEqual(PortraitFile.For(Kept, "hana", model), PortraitFile.For(Kept, "hana-2", model));
    }

    [Fact]
    public void A_new_portrait_takes_the_place_of_the_old_one_and_leaves_the_others()
    {
        Directory.CreateDirectory(Kept);
        foreach (var name in new[] { "hana.0000aaaa0000.png", "hana-2.0000bbbb0000.png", "my.hana.0000cccc0000.png" })
            File.WriteAllText(Path.Combine(Kept, name), "");
        var made = Path.Combine(Kept, "hana.0000dddd0000.png");
        File.WriteAllText(made, "");
        PortraitFile.Kept(made);
        Assert.Equal(["hana-2.0000bbbb0000.png", "hana.0000dddd0000.png", "my.hana.0000cccc0000.png"],
                     Directory.GetFiles(Kept).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_removed_characters_portrait_goes_and_the_others_stay()
    {
        Directory.CreateDirectory(Kept);
        foreach (var name in new[] { "hana.0000aaaa0000.png", "gone.0000bbbb0000.png", "shino.0000cccc0000.png" })
            File.WriteAllText(Path.Combine(Kept, name), "");
        PortraitFile.KeepOnly(Kept, ["hana", "shino"]);
        Assert.Equal(["hana.0000aaaa0000.png", "shino.0000cccc0000.png"], Directory.GetFiles(Kept).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Keeping_portraits_before_any_is_made_is_nothing_to_do() => PortraitFile.KeepOnly(Kept, ["hana"]);
}

public class ModelFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("models-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var file = Path.Combine(_dir, name);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    private string Write(string name, string text) => Write(name, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>A binary glTF as a .vrm is: a header, a JSON chunk and a binary one.</summary>
    private static byte[] Glb(string json = """{"asset":{"version":"2.0"}}""", int binary = 16)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
        var bytes = new List<byte>();
        void Word(uint value) => bytes.AddRange(BitConverter.GetBytes(value));
        Word(0x46546C67);
        Word(2);
        Word((uint)(12 + 8 + text.Length + 8 + binary));
        Word((uint)text.Length);
        Word(0x4E4F534A);
        bytes.AddRange(text);
        Word((uint)binary);
        Word(0x004E4942);
        bytes.AddRange(new byte[binary]);
        return [.. bytes];
    }

    [Fact]
    public void A_whole_model_is_read() => Assert.True(ModelFile.LooksWhole(Write("hana.vrm", Glb())));

    [Fact]
    public void A_download_cut_off_is_not_read() => Assert.False(ModelFile.LooksWhole(Write("hana.vrm", Glb()[..40])));

    [Fact]
    public void A_file_that_only_starts_like_a_model_is_not_read()
    {
        var bytes = Glb();
        Array.Fill(bytes, (byte)'x', 12, bytes.Length - 12);
        Assert.False(ModelFile.LooksWhole(Write("hana.glb", bytes)));
    }

    [Fact]
    public void A_model_whose_chunks_run_past_its_end_is_not_read()
    {
        var bytes = Glb();
        BitConverter.GetBytes(9999u).CopyTo(bytes, 12);
        Assert.False(ModelFile.LooksWhole(Write("hana.vrm", bytes)));
    }

    [Fact]
    public void A_model_without_its_description_is_not_read() => Assert.False(ModelFile.LooksWhole(Write("hana.vrm", Glb("{}"))));

    [Fact]
    public void Plain_text_is_not_read() => Assert.False(ModelFile.LooksWhole(Write("hana.vrm", "not a model")));

    [Fact]
    public void A_gltf_with_its_buffer_beside_it_is_read()
    {
        Write("hana.bin", new byte[16]);
        Assert.True(ModelFile.LooksWhole(Write("hana.gltf", """{"asset":{"version":"2.0"},"buffers":[{"uri":"hana.bin","byteLength":16}]}""")));
    }

    [Fact]
    public void A_gltf_whose_buffer_is_missing_or_short_is_not_read()
    {
        Assert.False(ModelFile.LooksWhole(Write("gone.gltf", """{"asset":{"version":"2.0"},"buffers":[{"uri":"gone.bin","byteLength":16}]}""")));
        Write("short.bin", new byte[8]);
        Assert.False(ModelFile.LooksWhole(Write("short.gltf", """{"asset":{"version":"2.0"},"buffers":[{"uri":"short.bin","byteLength":16}]}""")));
    }

    [Fact]
    public void An_empty_gltf_is_not_read() => Assert.False(ModelFile.LooksWhole(Write("hana.gltf", "{}")));

    [Fact]
    public void The_apps_own_models_are_whole()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "InsertCoin.slnx")))
            root = root.Parent!;
        var models = Directory.GetFiles(Path.Combine(root.FullName, "src", "InsertCoin.Game", "characters"), "*.vrm", SearchOption.AllDirectories);
        Assert.NotEmpty(models);
        Assert.All(models, model => Assert.True(ModelFile.LooksWhole(model), model));
    }
}
