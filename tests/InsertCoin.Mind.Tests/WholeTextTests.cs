using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>
/// What she writes is never kept cut off: text over its room is fitted at a whole sentence, and an answer the provider
/// cut at its length limit is asked again with more room, or for less at once.
/// </summary>
public class WholeTextTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public void Text_over_its_room_ends_at_its_last_whole_sentence_or_whole_word_never_mid_word()
    {
        Assert.Equal("First sentence.", Prompt.Fit("First sentence. Second one is longer than the room.", 20));
        Assert.Equal("It costs 3.5…", Prompt.Fit("It costs 3.5 dollars and more words here", 15));        // "3.5" ends no sentence
        Assert.Equal("今天很好。", Prompt.Fit("今天很好。我们去公园吧。", 8));
        Assert.Equal("Short enough.", Prompt.Fit("  Short enough. ", 100));
    }

    [Fact]
    public async Task Lines_whose_notes_come_back_cut_off_are_asked_again_with_more_room_then_in_smaller_batches()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock) { Window = 32_000 };
        for (var i = 0; i < 400; i++)
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), Friday.AddMinutes(i));
        clock.Now = Friday.AddMinutes(400);
        var model = new FakeModel("""{"observations": [{"date": "2026-10-02", "priority": "medium", "text": "They kept talking."}]}""");
        model.Finishes.Enqueue(ChatFinishReason.Length);
        model.Finishes.Enqueue(ChatFinishReason.Length);

        Assert.True(await conversation.Fold(model));

        int Lines(int request) => model.Requests[request][1].Text.Split('\n').Count(l => l.Contains("line "));
        Assert.Equal(model.Requests[0][1].Text, model.Requests[1][1].Text);                // the same lines, with twice the room
        Assert.Equal(8000, model.Options[1]!.MaxOutputTokens);
        Assert.InRange(Lines(2), 1, Lines(0) / 2 + 1);                                      // then half of them
        Assert.Equal(model.Requests.Count - 2, temp.Store.Observations("shino").Count);     // nothing kept from the cut answers
        Assert.False(conversation.NeedsFolding);
    }

    [Fact]
    public async Task Facts_kept_from_notes_that_come_back_cut_off_are_not_kept_and_the_next_try_reads_fewer_notes()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddHours(2)));
        for (var i = 0; i < 100; i++)             // notes upkeep hasn't kept facts from yet, far more than one request reads
            temp.Store.Observe("shino", [new Observation(0, Friday.AddMinutes(i), Priority.Medium, $"note {i} " + new string('x', 400), 1, 1)], 1);
        var model = new FakeModel("""{"facts": []}""");
        model.Finishes.Enqueue(ChatFinishReason.Length);
        model.Finishes.Enqueue(ChatFinishReason.Length);

        Assert.True(conversation.NeedsDigest(Conversation.Quiet));
        Assert.False(await conversation.Digest(model));
        Assert.True(await conversation.Digest(model));

        int Notes(int request) => model.Requests[request][1].Text.Split('\n').Count(l => l.Contains("note "));
        Assert.InRange(Notes(2), 1, Notes(0) / 2 + 1);
        Assert.True(conversation.NeedsDigest(Conversation.Quiet));         // the rest, next time
    }

    [Fact]
    public async Task A_note_that_comes_back_over_its_room_is_sent_back_to_be_shortened_and_never_cut_mid_word()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddHours(2)));
        temp.Store.Add("shino", Speaker.User, "I'm Sam, I live in Springfield and I love cats", Friday);
        var model = new FakeModel();
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The player is called Sam, lives in Springfield and loves cats."}]}""");
        model.Answers.Enqueue("""{"facts": [{"op": "add", "about": "player", "text": "The player is called Sam and lives in Springfield.", "importance": 9}]}""");
        model.Answers.Enqueue(string.Concat(Enumerable.Repeat("The player is called Sam and lives in Springfield. ", 40)));    // 1,960 characters
        model.Answers.Enqueue(string.Concat(Enumerable.Repeat("Sam lives in Springfield and loves cats. ", 40)));             // still over: 1,520

        Assert.True(await conversation.Digest(model));
        Assert.True(await conversation.RefreshNotes(model));

        var note = temp.Store.Note(MindStore.Shared, MindStore.AboutUser);
        Assert.Contains("Shorten this note to at most 180 words", model.Requests[3][0].Text);
        Assert.InRange(note.Length, 1300, Prompt.NoteLimit);
        Assert.StartsWith("Sam lives in Springfield", note);
        Assert.EndsWith("loves cats.", note);
    }
}
