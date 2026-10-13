using System.Globalization;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Usage: what her requests used, as the provider reported it (this month, and day by day for two weeks):
/// input read, the share served from the provider's cache, what was written to the cache, and output. With the prices
/// the player enters for a model (from their provider's price page: no table of prices is kept here, it would go out of
/// date and cannot know a proxy's), the cost too, cache reads and writes at their own prices.
/// </summary>
public sealed class UsagePage
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly LineEdit _input, _cached, _written, _output, _currency;

    /// <param name="changed">The prices as typed now (null: no input price) and their currency, kept for <paramref name="model"/>.</param>
    public UsagePage(VBoxContainer page, MindStore store, string? model, IReadOnlyDictionary<string, Prices> prices, string currency, Action<Prices?, string> changed)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var month = new DateOnly(today.Year, today.Month, 1);
        var rows = store.Usage(month < today.AddDays(-13) ? month : today.AddDays(-13));
        string Cost(IEnumerable<UsageRow> some)
        {
            var list = some.ToList();
            if (list.Count == 0)
                return "";
            var priced = list.Where(r => prices.ContainsKey(r.Model)).ToList();
            if (priced.Count == 0)
                return "?";
            var cost = priced.Sum(r => prices[r.Model].Cost(r));
            return $"{currency}{cost.ToString(cost < 1 ? "0.000" : "0.00", Invariant)}{(priced.Count < list.Count ? "+" : "")}";
        }

        page.AddChild(Hint("As your provider reports it, request by request. Replies are the answers to you; upkeep is memory work while you are away " +
                           "(summaries, facts, reviews). Nothing here is sent anywhere."));
        var thisMonth = rows.Where(r => r.Day >= month).ToList();
        page.AddChild(new Label { Text = $"This month ({month.ToString("MMMM", Invariant)})", ThemeTypeVariation = "Subheading" });
        page.AddChild(new Label { Text = Summary(thisMonth, Cost(thisMonth)), AutowrapMode = TextServer.AutowrapMode.WordSmart });

        page.AddChild(new Label { Text = "Day by day", ThemeTypeVariation = "Subheading" });
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 140), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var table = new GridContainer { Columns = 6, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        table.AddThemeConstantOverride("h_separation", 14);
        table.AddThemeConstantOverride("v_separation", 4);
        foreach (var head in new[] { "Day", "Requests", "Read", "From cache", "Output", "Cost" })
            table.AddChild(new Label { Text = head, ThemeTypeVariation = "Hint" });
        var days = rows.Where(r => r.Day > today.AddDays(-14)).GroupBy(r => r.Day).OrderByDescending(g => g.Key).ToList();
        foreach (var day in days)
        {
            var read = day.Sum(r => r.Input);
            table.AddChild(new Label { Text = day.Key == today ? "Today" : day.Key.ToString("ddd d MMM", Invariant) });
            table.AddChild(new Label { Text = day.Sum(r => r.Requests).ToString(Invariant) });
            table.AddChild(new Label { Text = Tokens(read) });
            table.AddChild(new Label { Text = read == 0 ? "" : $"{(double)day.Sum(r => r.Cached) / read:P0}" });
            table.AddChild(new Label { Text = Tokens(day.Sum(r => r.Output)) });
            table.AddChild(new Label { Text = Cost(day) });
        }
        scroll.AddChild(table);
        page.AddChild(scroll);
        if (days.Count == 0)
            page.AddChild(Hint("Nothing yet: it is counted from now on."));

        page.AddChild(new Label { Text = model is null ? "Prices" : $"Prices for {model}", ThemeTypeVariation = "Subheading" });
        page.AddChild(Hint("Per million tokens, from your provider's price page; leave them empty to see tokens only. Cache reads are usually a tenth " +
                           "of the input price, cache writes (Claude) a quarter more. If “from cache” stays at 0%, the provider, or a proxy in " +
                           "between, does not report it, or the cache is not being hit."));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        var mine = model is not null && prices.TryGetValue(model, out var p) ? p : null;
        _input = Price(grid, "Input", mine?.Input);
        _cached = Price(grid, "Cache read", mine?.Cached);
        _written = Price(grid, "Cache write", mine?.Written);
        _output = Price(grid, "Output", mine?.Output);
        page.AddChild(grid);
        var money = new HBoxContainer();
        money.AddThemeConstantOverride("separation", 10);
        money.AddChild(new Label { Text = "Currency" });
        _currency = new LineEdit { Text = currency, CustomMinimumSize = new Vector2(60, 0), MaxLength = 4 };
        money.AddChild(_currency);
        page.AddChild(money);
        foreach (var field in new[] { _input, _cached, _written, _output, _currency })
            field.TextChanged += _ => changed(Prices, Currency);
    }

    /// <summary>The prices entered (missing ones count as the input price, cache reads as a tenth of it), or null when there is no input price.</summary>
    public Prices? Prices
    {
        get
        {
            if (Number(_input) is not { } input)
                return null;
            return new Prices(input, Number(_cached) ?? input / 10, Number(_written) ?? input, Number(_output) ?? input);
        }
    }

    public string Currency => _currency.Text.Trim().Length > 0 ? _currency.Text.Trim() : "$";

    private static string Summary(IReadOnlyList<UsageRow> rows, string cost)
    {
        if (rows.Count == 0)
            return "Nothing yet.";
        var read = rows.Sum(r => r.Input);
        var replies = rows.Where(r => r.Purpose == "replies").Sum(r => r.Requests);
        var text = $"{rows.Sum(r => r.Requests)} requests ({replies} replies, {rows.Sum(r => r.Requests) - replies} upkeep). " +
                   $"Read {Tokens(read)} tokens, {(read == 0 ? 0 : (double)rows.Sum(r => r.Cached) / read):P0} of them from the provider's cache" +
                   $"{(rows.Sum(r => r.Written) > 0 ? $", {Tokens(rows.Sum(r => r.Written))} written to it" : "")}; " +
                   $"output {Tokens(rows.Sum(r => r.Output))} ({Tokens(rows.Sum(r => r.Reasoning))} of them thinking).";
        return text + (cost == "?" ? " Enter prices below to estimate the cost." : $" About {cost}.");
    }

    private static string Tokens(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
        >= 1_000 => $"{count / 1_000.0:0.#}k",
        _ => count.ToString(Invariant),
    };

    private static LineEdit Price(GridContainer grid, string label, decimal? value)
    {
        grid.AddChild(new Label { Text = label });
        var field = new LineEdit { Text = value?.ToString(Invariant) ?? "", PlaceholderText = "–", CustomMinimumSize = new Vector2(80, 0) };
        grid.AddChild(field);
        return field;
    }

    private static decimal? Number(LineEdit field) =>
        decimal.TryParse(field.Text.Trim().TrimStart('$', '¥', '€', '£'), NumberStyles.Number, Invariant, out var value) && value >= 0 ? value : null;

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
}
