using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Add-ons: optional extras, off until the player turns them on, each saying what it needs and where their
/// data goes. The first: web search through their own SearXNG. Each change is used at once.
/// </summary>
public partial class AddOnSettings : VBoxContainer
{
    /// <summary>The add-ons as set now.</summary>
    public event Action<AddOns>? Chosen;

    public void Build(string name, Provider? current, AddOns addOns)
    {
        Form.Page(this, "Add-ons");
        Form.Hint(this, "Extras that are off until you turn them on. Each says what it needs and where what you give it goes.");
        Form.Subheading(this, "Web search with SearXNG");
        LineEdit address = null!;
        var on = Form.Tick(this, "Search the web through my SearXNG", addOns.SearXng,
                           ticked => Chosen?.Invoke(addOns = addOns with { SearXng = ticked, SearXngAddress = address.Text.Trim() }));
        var row = Form.Indented(this);
        row.AddChild(new Label { Text = "Address" });
        address = new LineEdit { Text = addOns.SearXngAddress, PlaceholderText = "http://localhost:8888", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        address.TextChanged += text => Chosen?.Invoke(addOns = addOns with { SearXngAddress = text.Trim() });
        row.AddChild(address);
        var result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0), Visible = false };
        Form.Button(row, "Test", async () =>
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
        });
        AddChild(result);
        Form.ShowWhile(on, row);
        var own = current?.Protocol == Protocol.Anthropic ? "your provider's own search" : "no search at all (your provider has none this app can use)";
        Form.Hint(this, $"SearXNG is a free, open-source search engine you run yourself, on this PC or one you trust: it asks the big search " +
                        $"engines for you, and nothing goes through anyone else. With it on, {name} searches through it; without it, {own}. " +
                        "To run it: Docker Desktop, then docker run -d -p 8888:8080 searxng/searxng, and in its settings.yml add json under " +
                        "search: formats: (and restart it).");
    }
}
