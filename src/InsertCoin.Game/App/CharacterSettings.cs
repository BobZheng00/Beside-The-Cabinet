using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Companion: who sits on the stool, as cards with their portraits, the one there chosen; how they sit;
/// adding a character (a 3D model, a character card, a voice), and the folder they are kept in; then whether anyone
/// sits there at all, and whether they say their lines in speech bubbles. Each change is used at once.
/// </summary>
public partial class CharacterSettings : VBoxContainer
{
    /// <summary>Someone on the stool (true), or nobody.</summary>
    public event Action<bool>? ShownChosen;
    /// <summary>Speech bubbles on or off.</summary>
    public event Action<bool>? BubblesChosen;
    /// <summary>Another character takes the stool (their id).</summary>
    public event Action<string>? Seated;
    /// <summary>"Add a character…" and "Open characters folder".</summary>
    public event Action? Add, OpenFolder;

    /// <param name="portrait">A character's portrait (by id) as it is now, and what to do with it once it is made.</param>
    /// <param name="mannerOf">How a character sits (see <see cref="Manner"/>).</param>
    /// <param name="setManner">The player chose how one sits.</param>
    /// <param name="showMe">"Show me": a few seconds of it on the stool.</param>
    public void Build((string Id, string Name)[] characters, string seated, bool shown, bool bubbles,
                      Func<string, Action<Texture2D>, Texture2D> portrait,
                      Func<string, Manner> mannerOf, Action<string, Manner> setManner, Action showMe)
    {
        Form.Page(this);
        var cards = new HFlowContainer();
        cards.AddThemeConstantOverride("h_separation", 10);
        cards.AddThemeConstantOverride("v_separation", 10);
        AddChild(cards);
        var group = new ButtonGroup();
        var manner = new VBoxContainer();
        void HowTheySit(string id)
        {
            foreach (var old in manner.GetChildren())
            {
                manner.RemoveChild(old);
                old.QueueFree();
            }
            Form.Subheading(manner, "How they sit");
            Manners(manner, mannerOf(id), chosen => setManner(id, chosen), showMe);
        }
        foreach (var (id, name) in characters)
        {
            var card = new Button
            {
                Text = name, TooltipText = name, ToggleMode = true, ButtonGroup = group, ButtonPressed = id == seated, ThemeTypeVariation = "Card",
                IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top,
                // the whole name, wrapped under a portrait the same size on every card (the theme's icon_max_width)
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(112, 0),
            };
            card.Icon = portrait(id, made =>
            {
                if (IsInstanceValid(card))
                    card.Icon = made;
            });
            card.Toggled += on =>
            {
                if (!on)
                    return;
                Seated?.Invoke(id);
                HowTheySit(id);
            };
            cards.AddChild(card);
        }
        AddChild(manner);
        if (seated.Length > 0)
            HowTheySit(seated);
        var buttons = Form.Buttons(this);
        Form.Button(buttons, "Add a character…", () => Add?.Invoke());
        Form.Button(buttons, "Open characters folder", () => OpenFolder?.Invoke());
        Form.Hint(this, "A 3D character (.vrm, .glb, .gltf, or a .zip holding one) takes the stool. A character card (.png or .json, " +
                        "SillyTavern style) gives whoever is on the stool a persona; a voice's clips or .zip give them a voice. Dropping " +
                        "any of them on the cabinet does the same. Files put in the folder by hand show up after a restart.");

        Form.Tick(this, "Someone on the stool", shown, on => ShownChosen?.Invoke(on));
        Form.Hint(this, "Untick it to leave the stool empty: the cabinet goes on as before, without anyone on it.");
        Form.Tick(this, "Speech bubbles", bubbles, on => BubblesChosen?.Invoke(on));
        Form.Hint(this, "Without them, whoever is on the stool still reacts to what happens, but says nothing unless you talk to them.");
    }

    /// <summary>
    /// How a character sits, the two manners to choose from, and "Show me": whoever is on the stool moves into each as
    /// it is chosen (the first run, "Meet …" and this page all ask it this way).
    /// </summary>
    public static void Manners(Container parent, Manner current, Action<Manner> chosen, Action showMe)
    {
        var group = new ButtonGroup();
        foreach (var (manner, label) in new[] { (Manner.Neat, "Neat: knees together, hands in the lap"),
                                                (Manner.Relaxed, "Relaxed: knees apart, hands on the thighs, fewer head tilts") })
        {
            var pick = new CheckBox
            {
                Text = label, ButtonGroup = group, ButtonPressed = manner == current, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0),     // wraps in a narrow window
            };
            pick.Toggled += on =>
            {
                if (on)
                    chosen(manner);
            };
            parent.AddChild(pick);
        }
        Form.Button(Form.Buttons(parent), "Show me", showMe).TooltipText = "A few seconds of it on the stool: listening, a nod, a tilt of the head.";
    }
}
