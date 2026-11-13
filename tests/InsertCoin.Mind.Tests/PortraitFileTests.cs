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
}
