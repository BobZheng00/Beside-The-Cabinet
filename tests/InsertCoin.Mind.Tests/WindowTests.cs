using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>The window kept word for word, folding it cache-friendly, her sense of time, and asking for caching.</summary>
public class WindowTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static Conversation Talked(TempStore temp, FakeClock clock, int lines, int window)
    {
        var conversation = new Conversation(temp.Store, "shino", Shino, clock) { Window = window };
        for (var i = 0; i < lines; i++)
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), Friday.AddMinutes(i));
        clock.Now = Friday.AddMinutes(lines);
        return conversation;
    }

    [Fact]
    public async Task A_window_three_quarters_full_is_folded_while_quiet_and_only_a_full_one_at_once()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = Talked(temp, clock, 30, window: 4000);          // about 3,200 tokens
        var model = new FakeModel("They talked.");

        Assert.False(conversation.NeedsFolding);
        Assert.True(conversation.NeedsQuietFold);
        Assert.False(await conversation.Fold(model));                       // mid-conversation: not full, so not yet
        Assert.False(conversation.IsQuiet(Conversation.Quiet));
        clock.Now += Conversation.Quiet;
        Assert.True(conversation.IsQuiet(Conversation.Quiet));

        Assert.True(await conversation.Fold(model, quiet: true));

        var left = temp.Store.After("shino", temp.Store.SummaryOf("shino")!.Through);
        Assert.InRange(Prompt.Tokens(left), 1200, 2000);                   // the newest half of the window
        Assert.False(conversation.NeedsQuietFold);
    }

    [Fact]
    public void A_cloud_provider_keeps_32k_tokens_word_for_word_and_one_on_this_PC_8k()
    {
        Assert.Equal(32_000, new Provider(Protocol.OpenAi, "https://api.deepseek.com/v1", "deepseek-chat").DefaultWindow);
        Assert.Equal(8_000, new Provider(Protocol.OpenAi, "http://localhost:11434/v1", "qwen3").DefaultWindow);
        Assert.Equal(8_000, new Provider(Protocol.OpenAi, "http://127.0.0.1:1234/v1", "local").DefaultWindow);
    }

    [Fact]
    public async Task Her_replies_on_Anthropic_ask_for_automatic_caching_and_others_are_left_as_they_are()
    {
        var model = new FakeModel("hi");
        var claude = new Provider(Protocol.Anthropic, "https://api.anthropic.com", "claude-haiku-4-5").ForConversation(model);

        await foreach (var _ in claude.GetStreamingResponseAsync([new(ChatRole.User, "hello")], new ChatOptions { MaxOutputTokens = 600 }))
        {
        }

        var raw = Assert.IsType<MessageCreateParams>(model.Options.Single()!.RawRepresentationFactory!(claude));
        Assert.NotNull(raw.CacheControl);
        Assert.Equal(600, raw.MaxTokens);
        Assert.Same(model, new Provider(Protocol.OpenAi, "https://api.openai.com/v1", "gpt-5.5").ForConversation(model));
    }

    [Fact]
    public void She_is_told_how_long_ago_things_were_in_words()
    {
        var now = new DateTimeOffset(2026, 10, 4, 14, 5, 0, TimeSpan.FromHours(8));
        Assert.Equal("a moment ago", When.Ago(now.AddSeconds(-50), now));
        Assert.Equal("40 minutes ago", When.Ago(now.AddMinutes(-40), now));
        Assert.Equal("5 hours ago", When.Ago(now.AddHours(-5), now));
        Assert.Equal("yesterday", When.Ago(now.AddHours(-17), now));          // Saturday 21:05
        Assert.Equal("3 days ago", When.Ago(now.AddDays(-3), now));
        Assert.Equal("3 weeks ago", When.Ago(now.AddDays(-21), now));
        Assert.Equal("4 months ago", When.Ago(now.AddDays(-125), now));
        Assert.Equal("afternoon", When.PartOfDay(now));
        Assert.Equal("Sunday 4 October 2026, 14:05 (afternoon).", When.Now(now));
    }

    [Fact]
    public async Task The_turn_says_when_now_is_and_talk_that_resumes_is_marked_with_its_time()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        temp.Store.Add("shino", Speaker.User, "morning!", Friday.AddHours(-10));
        temp.Store.Add("shino", Speaker.Her, "Morning!", Friday.AddHours(-10).AddMinutes(1));
        temp.Store.Add("shino", Speaker.User, "back", Friday.AddHours(-10).AddMinutes(20));      // 19 minutes later: no mark
        temp.Store.Add("shino", Speaker.Her, "Welcome back!", Friday.AddHours(-10).AddMinutes(21));
        clock.Now = Friday.AddDays(1);                                       // Saturday evening
        var model = new FakeModel("Hi again!");

        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { });

        var sent = model.Requests.Single();
        var history = string.Join("\n", sent.Skip(1).Take(sent.Count - 2).Select(m => m.Text));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(history, @"\[cabinet\] Friday 2 October"));
        Assert.Contains("<now>\nSaturday 3 October 2026, 19:30 (evening). You last talked yesterday, Friday 2 October at 09:51.\n</now>", sent[^1].Text);
        Assert.DoesNotContain("Now:", sent[^1].Text);
    }
}
