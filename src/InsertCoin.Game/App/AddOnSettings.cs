using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Connections → Web search: web search through the player's own SearXNG, off until they turn it on, saying
/// what it needs and where their searches go. Each change is used at once.
/// </summary>
public partial class AddOnSettings : VBoxContainer
{
    /// <summary>The add-ons as set now.</summary>
    public event Action<AddOns>? Chosen;

    /// <param name="searching">How she searches the web now, in a few words (<see cref="Chat.WebSearch"/>), or null when she can't.</param>
    public void Build(string name, Provider? current, AddOns addOns, Func<string?> searching)
    {
        Form.Page(this);
        var standing = Form.Connection(this, "Web search");
        Form.Subheading(this, "Web search with SearXNG");
        LineEdit address = null!;
        CheckBox on = null!;
        Label result = null!;
        // on, with a test of the SearXNG when it is that; off, a way to the SearXNG below
        void ShowStanding()
        {
            var how = searching();
            var line = how is not null ? $"{name} searches the web {how}."
                     : on.ButtonPressed ? $"{name} can't search the web until your SearXNG's address is filled in."
                     : $"{name} can't search the web: your provider has no search this app can use. Your own SearXNG can give {name} one.";
            standing.Show(how is null ? "Off" : "On", how is not null ? true : on.ButtonPressed ? false : null, line,
                          on.ButtonPressed ? "Test" : "Use my SearXNG", on.ButtonPressed ? () => _ = Test() : UseIt);
        }
        void UseIt()
        {
            on.ButtonPressed = true;
            Form.Bring(address);
        }
        void Changed(AddOns now)
        {
            Chosen?.Invoke(addOns = now);
            ShowStanding();
        }
        on = Form.Tick(this, "Search the web through my SearXNG", addOns.SearXng,
                       ticked => Changed(addOns with { SearXng = ticked, SearXngAddress = address.Text.Trim() }));
        var row = Form.Indented(this);
        row.AddChild(new Label { Text = "Address" });
        address = new LineEdit { Text = addOns.SearXngAddress, PlaceholderText = "http://localhost:8888", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        address.TextChanged += text => Changed(addOns with { SearXngAddress = text.Trim() });
        row.AddChild(address);
        result = standing.Result;
        async Task Test()
        {
            if (SearXng.Parse(address.Text) is not { } server)
            {
                Form.Say(result, "That isn't an address (like http://localhost:8888).", Paper.Bad);
                return;
            }
            Form.Say(result, "Searching…", Paper.Faint);
            try
            {
                var said = await Chat.TrySearXng(server);
                if (IsInstanceValid(this))
                    Form.Say(result, said, said.StartsWith("Works") ? Paper.Good : Paper.Bad);
            }
            catch (Exception e)
            {
                if (IsInstanceValid(this))
                    Form.Say(result, e.Message, Paper.Bad);
            }
        }
        Form.ShowWhile(on, row);
        ShowStanding();
        var own = current?.Protocol == Protocol.Anthropic ? "your provider's own search" : "no search at all (your provider has none this app can use)";
        Form.Hint(this, $"SearXNG is a free, open-source search engine you run yourself, on this PC or one you trust: it asks the big search " +
                        $"engines for you, and nothing goes through anyone else. With it on, {name} searches through it; without it, {own}. " +
                        "To run it: Docker Desktop, then docker run -d -p 8888:8080 searxng/searxng, and in its settings.yml add json under " +
                        "search: formats: (and restart it).");
    }
}
