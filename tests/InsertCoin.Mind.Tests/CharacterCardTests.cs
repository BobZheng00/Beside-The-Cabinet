using System.IO.Compression;
using System.Text;

namespace InsertCoin.Mind.Tests;

public class CharacterCardTests
{
    private const string V2 = """
        {"spec": "chara_card_v2", "spec_version": "2.0", "data": {
          "name": "Elowen", "description": "{{char}} is the young leader of Harbor Isle.", "personality": "kind, determined",
          "scenario": "{{char}} keeps {{user}} company between operations.", "first_mes": "*waves* Doctor, you're back!",
          "mes_example": "<START>\n{{user}}: Tired?\n{{char}}: A little. But we keep going.", "system_prompt": "",
          "character_book": {"entries": [
            {"keys": ["Glimmerstone", "辉石"], "content": "Glimmerstone is a dangerous mineral that powers the realm.", "enabled": true},
            {"keys": ["Corwen"], "content": "Corwen is Harbor Isle's stern doctor.", "enabled": false},
            {"keys": [], "content": "Elowen calls the player Doctor.", "constant": true}]}}}
        """;

    /// <summary>A minimal 1x1 PNG with the card in a text chunk, the way SillyTavern writes them.</summary>
    private static byte[] Png(string keyword, string json)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string kind, byte[] body)
        {
            file.Write(BitConverter.GetBytes(body.Length).Reverse().ToArray());
            var typed = Encoding.ASCII.GetBytes(kind).Concat(body).ToArray();
            file.Write(typed);
            file.Write(BitConverter.GetBytes(System.IO.Hashing.Crc32.HashToUInt32(typed)).Reverse().ToArray());
        }
        Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        Chunk("tEXt", Encoding.Latin1.GetBytes(keyword + "\0" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json))));
        using var pixels = new MemoryStream();
        using (var z = new ZLibStream(pixels, CompressionLevel.Optimal, leaveOpen: true))
            z.Write([0, 0, 0, 0, 0]);
        Chunk("IDAT", pixels.ToArray());
        Chunk("IEND", []);
        return file.ToArray();
    }

    [Fact]
    public void A_v2_card_in_a_png_becomes_a_persona_with_its_lorebook()
    {
        var card = CharacterCard.Read(Png("chara", V2));

        Assert.NotNull(card);
        var persona = card.ToPersona("Bob");
        Assert.Equal("Elowen", persona.Name);
        Assert.StartsWith("Elowen is the young leader of Harbor Isle.", persona.Description);
        Assert.Contains("Personality: kind, determined", persona.Description);
        Assert.Contains("Background: Elowen keeps Bob company between operations.", persona.Description);
        Assert.Contains("How Elowen talks, for example:\nBob: Tired?\nElowen: A little.", persona.Description);
        Assert.Equal("*waves* Doctor, you're back!", persona.Greeting);
        Assert.Equal(2, persona.Lore.Count);                           // the disabled entry is left out
        Assert.Equal(["Elowen calls the player Doctor.", "Glimmerstone is a dangerous mineral that powers the realm."],
                     persona.LoreFor("tell me about 辉石").Select(l => l.Content).OrderBy(c => c));
        Assert.Equal(["Elowen calls the player Doctor."], persona.LoreFor("good morning").Select(l => l.Content));
    }

    [Fact]
    public void A_v3_chunk_wins_and_plain_json_and_v1_cards_read_too()
    {
        var v3 = V2.Replace("chara_card_v2", "chara_card_v3").Replace("\"Elowen\"", "\"Elowen (V3)\"");
        Assert.Equal("Elowen (V3)", CharacterCard.Read(Png("ccv3", v3))!.Name);
        Assert.Equal("Elowen", CharacterCard.Read(Encoding.UTF8.GetBytes(V2))!.Name);
        Assert.Equal("Rin", CharacterCard.Read(Encoding.UTF8.GetBytes("""{"name": "Rin", "description": "A quiet archer."}"""))!.Name);
    }

    [Fact]
    public void Pictures_and_other_files_are_not_cards()
    {
        Assert.Null(CharacterCard.Read(Png("Comment", "hello")));
        Assert.Null(CharacterCard.Read(Encoding.UTF8.GetBytes("not json at all")));
        Assert.Null(CharacterCard.Read(Encoding.UTF8.GetBytes("""{"title": "a song"}""")));
    }

    [Fact]
    public async Task Lore_whose_key_comes_up_is_put_in_front_of_her()
    {
        using var temp = new TempStore();
        var persona = CharacterCard.Read(Encoding.UTF8.GetBytes(V2))!.ToPersona("Bob");
        var conversation = new Conversation(temp.Store, "elowen", persona, new FakeClock(DateTimeOffset.Now));
        var model = new FakeModel("Careful with it, Doctor.");

        await conversation.Answer(model, "I found some Glimmerstone today", "", _ => { }, (_, _) => { });

        var sent = model.Requests.Single();
        Assert.Contains("Elowen is the young leader of Harbor Isle.", sent[0].Text);
        Assert.Contains("- Glimmerstone is a dangerous mineral that powers the realm.", sent[^1].Text);
        Assert.Contains("- Elowen calls the player Doctor.", sent[^1].Text);
    }
}
