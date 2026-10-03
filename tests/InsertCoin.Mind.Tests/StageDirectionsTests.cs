namespace InsertCoin.Mind.Tests;

public class StageDirectionsTests
{
    private static (string Shown, List<(string Name, string? Payload)> Directions) Run(params string[] pieces)
    {
        var directions = new List<(string, string?)>();
        var stage = new StageDirections((name, payload) => directions.Add((name, payload)));
        var shown = string.Concat(pieces.Select(stage.Feed)) + stage.Finish();
        return (shown, directions);
    }

    [Fact]
    public void A_leading_direction_is_acted_not_shown()
    {
        var (shown, directions) = Run("[cheer] Nice one!");
        Assert.Equal("Nice one!", shown);
        Assert.Equal([("cheer", null)], directions);
    }

    [Fact]
    public void A_direction_split_across_pieces_is_still_found()
    {
        var (shown, directions) = Run("[ch", "eer", "]  Nice", " one!");
        Assert.Equal("Nice one!", shown);
        Assert.Equal([("cheer", null)], directions);
    }

    [Fact]
    public void A_direction_can_carry_a_payload()
    {
        var (shown, directions) = Run("[note: exam on Friday] Got it, I'll remember!");
        Assert.Equal("Got it, I'll remember!", shown);
        Assert.Equal([("note", "exam on Friday")], directions);
    }

    [Fact]
    public void An_unknown_one_word_direction_is_dropped_too()
    {
        var (shown, directions) = Run("[smiles] Hi!");
        Assert.Equal("Hi!", shown);
        Assert.Equal([("smiles", null)], directions);
    }

    [Fact]
    public void A_direction_at_the_end_leaves_no_trailing_blank()
    {
        var (shown, directions) = Run("Great job! [cheer]");
        Assert.Equal("Great job!", shown);
        Assert.Single(directions);
    }

    [Fact]
    public void Brackets_that_are_not_directions_stay_text()
    {
        Assert.Equal("Use a[i] + a[j], or [Two Sum].", Run("Use a[i] + a[j], or [Two Sum].").Shown);
    }

    [Fact]
    public void An_unclosed_bracket_is_released_at_the_end()
    {
        Assert.Equal("Wait [what", Run("Wait [what").Shown);
    }

    [Fact]
    public void Chinese_text_passes_through()
    {
        var (shown, directions) = Run("[wave] 你好！今天", "也加油哦。");
        Assert.Equal("你好！今天也加油哦。", shown);
        Assert.Equal([("wave", null)], directions);
    }
}
