using System.Net;
using System.Text;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

public class AttachmentTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");
    private static readonly Attachment Steps = new("steps.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

    private static Attachment File(string name, string text) => new(name, "text/plain", Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task A_picture_goes_with_the_message_and_what_she_saw_is_kept_in_its_place()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("[saw: a step counter showing 8,214 steps on 2 October] ", "That's a ", "good walk!");
        var shown = new StringBuilder();

        var answer = await conversation.Answer(model, "my walk today", "", words => shown.Append(words), (_, _) => { }, handed: [Steps]);

        var turn = model.Requests[0][^1];
        var picture = Assert.Single(turn.Contents.OfType<DataContent>());
        Assert.Equal("image/jpeg", picture.MediaType);
        Assert.Equal(Steps.Data, picture.Data.ToArray());
        Assert.Contains("the picture \"steps.jpg\"", turn.Text);
        Assert.Contains("[saw:", turn.Text);
        Assert.Equal("That's a good walk!", answer);
        Assert.Equal("That's a good walk!", shown.ToString());
        Assert.Equal("my walk today [picture: steps.jpg: a step counter showing 8,214 steps on 2 October]",
                     temp.Store.After("shino", 0)[0].Text);
    }

    [Fact]
    public async Task The_picture_is_sent_once_and_only_her_words_about_it_after()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        await conversation.Answer(new FakeModel("[saw: a cat on a sofa] Cute!"), "look", "", _ => { }, (_, _) => { }, handed: [Steps with { Name = "cat.jpg" }]);
        var next = new FakeModel("Yes.");

        await conversation.Answer(next, "isn't it?", "", _ => { }, (_, _) => { });

        Assert.DoesNotContain(next.Requests[0], m => m.Contents.OfType<DataContent>().Any());
        Assert.Contains(next.Requests[0], m => m.Text.Contains("[picture: cat.jpg: a cat on a sofa]"));
    }

    [Fact]
    public async Task A_text_file_goes_in_as_text_and_a_short_one_is_kept_word_for_word()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("[saw: a Python two-sum with a dictionary] Looks right.");
        var code = "def two_sum(nums, target):\n    seen = {}\n";

        await conversation.Answer(model, "check this", "", _ => { }, (_, _) => { }, handed: [File("sum.py", code)]);

        var turn = model.Requests[0][^1];
        Assert.Empty(turn.Contents.OfType<DataContent>());
        Assert.Contains("--- sum.py ---\n" + code, turn.Text);
        Assert.Equal("check this\n[file: sum.py]\n" + code + "\n[end of sum.py]", temp.Store.After("shino", 0)[0].Text);
    }

    [Fact]
    public async Task A_long_text_file_is_kept_only_as_what_she_saw_of_it()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel("[saw: a long server log with three timeouts] Hm."), "", "", _ => { }, (_, _) => { },
                                  handed: [File("server.log", new string('x', Attachment.KeptWordForWord + 1))]);

        Assert.Equal("[file: server.log: a long server log with three timeouts]", temp.Store.After("shino", 0)[0].Text);
    }

    [Fact]
    public async Task A_model_that_cant_take_pictures_is_asked_again_without_them_and_she_says_so()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("Sorry, I can't see pictures.")
        {
            RefusesPictures = new HttpRequestException("image input is not supported", null, HttpStatusCode.BadRequest),
        };

        var answer = await conversation.Answer(model, "my walk today", "", _ => { }, (_, _) => { }, handed: [Steps]);

        Assert.Equal("Sorry, I can't see pictures.", answer);
        Assert.True(conversation.Unseeing);
        Assert.Equal(2, model.Requests.Count);
        Assert.Empty(model.Requests[1][^1].Contents.OfType<DataContent>());
        Assert.Contains("can't take pictures or PDFs", model.Requests[1][^1].Text);
        Assert.Equal("my walk today [picture: steps.jpg, not seen: the model can't take them]", temp.Store.After("shino", 0)[0].Text);
    }

    [Fact]
    public async Task Once_she_cant_see_no_picture_is_sent_at_all()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday)) { Unseeing = true };
        var model = new FakeModel("I can't see it, sorry.");

        await conversation.Answer(model, "", "", _ => { }, (_, _) => { }, handed: [Steps]);

        Assert.Single(model.Requests);
        Assert.Empty(model.Requests[0][^1].Contents.OfType<DataContent>());
    }

    [Fact]
    public async Task A_failure_with_nothing_to_do_with_pictures_is_not_taken_for_blindness()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("x") { Fails = new HttpRequestException("the server is down", null, HttpStatusCode.BadGateway) };

        await Assert.ThrowsAsync<HttpRequestException>(() => conversation.Answer(model, "look", "", _ => { }, (_, _) => { }, handed: [Steps]));

        Assert.False(conversation.Unseeing);
        Assert.Equal("look [picture: steps.jpg]", temp.Store.After("shino", 0)[0].Text);
    }

    [Theory]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("shot.png", "image/png")]
    [InlineData("paper.pdf", "application/pdf")]
    [InlineData("notes.md", "text/plain")]
    [InlineData("Makefile", "text/plain")]
    public void A_file_is_taken_by_its_name_and_start(string name, string type) =>
        Assert.Equal(type, Attachment.TypeOf(name, "all: build\n"u8));

    [Fact]
    public void Programs_archives_and_binary_files_are_not_taken()
    {
        Assert.Null(Attachment.TypeOf("setup.exe", "MZ"u8));
        Assert.Null(Attachment.TypeOf("photos.zip", "PK"u8));
        Assert.Null(Attachment.TypeOf("data.bin", [1, 0, 2, 0]));
        Assert.Null(Attachment.TypeOf("photo.heic", [0, 0, 0, 24]));
    }

    [Fact]
    public void A_text_files_words_are_read_whatever_its_encoding_and_cut_when_long()
    {
        Assert.Equal("héllo", new Attachment("a.txt", "text/plain", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("héllo")]).Words());
        Assert.Equal("你好", new Attachment("a.txt", "text/plain", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("你好")]).Words());
        Assert.EndsWith("the file goes on]", File("big.txt", new string('a', Attachment.TextLimit + 5)).Words());
    }

    [Fact]
    public void Looking_at_the_screen_asks_first_and_always_covers_every_screenshot()
    {
        var tool = BuiltIn.LookAtScreen(() => "Taken.");

        Assert.Equal(Permission.Ask, tool.Standard);
        Assert.Equal("screen", tool.Ask!(new AIFunctionArguments())!.Value.Scope);
    }
}
