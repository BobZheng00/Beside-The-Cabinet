using System.Runtime.Versioning;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Abilities and → Privacy: what she can do beyond talking, and how freely, tool by tool; what of the player's
/// she may look at (asked for like her other abilities, but never skipped by Auto), and what she is told in the
/// background. Each change is used at once.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AbilitySettings : VBoxContainer
{
    /// <summary>The permissions as chosen now: only what differs from a tool's standard, and the sites still allowed always.</summary>
    public event Action<IReadOnlyDictionary<string, Permission>>? PermissionsChosen;
    /// <summary>Auto (see <see cref="Toolbox.Auto"/>) ticked or not.</summary>
    public event Action<bool>? AutoChosen;
    /// <summary>What she is told in the background, as ticked now.</summary>
    public event Action<Privacy>? PrivacyChosen;

    /// <summary>The permissions offered for a tool, as the page names them.</summary>
    private static readonly (Permission Permission, string Label)[] Permissions =
        [(Permission.Allowed, "Allowed"), (Permission.Ask, "Ask me first"), (Permission.Off, "Off")];

    private Chat _chat = null!;

    /// <summary>The Abilities page: what she can do beyond talking, and how freely, tool by tool.</summary>
    public void BuildAbilities(Chat chat, string name)
    {
        _chat = chat;
        var tools = chat.Tools;
        Form.Page(this, "Abilities");
        Form.Hint(this, $"What {name} can do beyond talking. Allowed: she does it when it helps. Ask me first: she asks, and a card by " +
                        "her stool lets you say yes or no. Off: she doesn't have it. Whatever acts outside this app asks first unless you allow it.");
        Form.Tick(this, "Auto: she goes ahead without asking", tools.Auto, on => AutoChosen?.Invoke(on));
        Form.Hint(this, "Like an auto mode: where she would ask first, she just does it. She still says what she's doing, and what's " +
                        "switched off stays off. Every use is in the chat history. Two exceptions: what's on the Privacy page, and the rest " +
                        "of a reply once she has read a page or search results, since a page could try to talk her into things.");
        Form.Subheading(this, "Built in");
        AddChild(Choices(tools.All.Where(t => t.Source == Tool.BuiltIn && !t.Private)));
        Form.Hint(this, chat.WebSearch is { } searching
            ? $"She searches the web {searching}."
            : "She can't search the web: your provider has no search of its own that this app can use. The SearXNG add-on gives her one.");

        var always = tools.Chosen.Where(c => c.Key.Contains(':')).Select(c => c.Key).Order().ToList();
        if (always.Count > 0)
        {
            Form.Subheading(this, "Always allowed");
            foreach (var key in always)
            {
                var colon = key.IndexOf(':');
                var title = tools.All.FirstOrDefault(t => t.Name == key[..colon])?.Title ?? key[..colon];
                var row = Form.Buttons(this);
                row.AddChild(new Label { Text = $"{title}: {key[(colon + 1)..]}", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
                Form.Button(row, "Forget", () =>
                {
                    var chosen = new Dictionary<string, Permission>(_chat.Tools.Chosen);
                    chosen.Remove(key);
                    PermissionsChosen?.Invoke(chosen);
                    row.QueueFree();
                });
            }
            Form.Hint(this, "Where you answered Always on her card. Forget one, and she asks again there.");
        }
        if (tools.Refused)
            Form.Hint(this, "Your model turned tools down, so for now she answers without them. Choosing a model that takes tools brings them back.");
    }

    /// <summary>
    /// The Privacy page: what she can look at that is the player's own, and what she is told in the background. All of it
    /// goes to their provider with the conversation.
    /// </summary>
    public void BuildPrivacy(Chat chat, string name, Provider? current, Privacy privacy)
    {
        _chat = chat;
        var provider = current is not null && Uri.TryCreate(current.BaseUrl, UriKind.Absolute, out var server) ? server.Host : "your AI provider";
        Form.Page(this, "Privacy");
        Form.Hint(this, $"What {name} can see of what's yours. Whatever she looks at or is told goes to {provider} with your conversation, " +
                        "as everything you say to her does; nothing else leaves this PC.");
        Form.Subheading(this, "When you ask for help");
        AddChild(Choices(chat.Tools.All.Where(t => t.Private)));
        Form.Hint(this, "Auto never skips these: whatever is set to ask first here always asks.");
        Form.Subheading(this, "In the background");
        CheckBox music = null!, days = null!;
        void Chosen() => PrivacyChosen?.Invoke(privacy with { Music = music.ButtonPressed, Days = days.ButtonPressed });
        music = Form.Tick(this, "What's playing in your music app (track and artist)", privacy.Music, _ => Chosen());
        days = Form.Tick(this, "What your record of days shows: nights running late, days without play", privacy.Days, _ => Chosen());
        Form.Hint(this, "Your play time, focus and what you've done today she always knows: the cabinet runs on them. Web searches go to your " +
                        "provider, or to your own SearXNG (Add-ons); pages she reads are fetched by this PC from their site.");
    }

    /// <summary>A permission to choose for each tool (the provider's own search only runs or doesn't: it can't ask).</summary>
    private GridContainer Choices(IEnumerable<Tool> listed)
    {
        var tools = _chat.Tools;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var tool in listed)
        {
            var offered = Permissions.Where(p => tool.Hosted is null || p.Permission != Permission.Ask).ToList();
            var choice = new OptionButton { CustomMinimumSize = new Vector2(160, 0) };
            foreach (var (_, label) in offered)
                choice.AddItem(label);
            choice.Select(Math.Max(0, offered.FindIndex(p => p.Permission == tools.PermissionOf(tool))));
            choice.ItemSelected += index =>
            {
                var chosen = new Dictionary<string, Permission>(_chat.Tools.Chosen);
                if (offered[(int)index].Permission is var permission && permission != tool.Standard)
                    chosen[tool.Name] = permission;
                else
                    chosen.Remove(tool.Name);
                PermissionsChosen?.Invoke(chosen);
            };
            grid.AddChild(new Label { Text = tool.Title, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tool.Function.Description,
                                      MouseFilter = MouseFilterEnum.Pass });
            grid.AddChild(choice);
        }
        return grid;
    }
}
