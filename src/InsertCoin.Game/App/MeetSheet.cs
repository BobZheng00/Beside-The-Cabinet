using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// "Meet …", shown when a character is added: what they have of their own and what they still lack (who they are,
/// from a card or a persona, else a plain friendly companion; their voice), and the name they call the player by,
/// which every character shares, as they share what the player has told any of them. Filled again as a card or a
/// voice comes in while it shows. The provider and the rest are the player's, set once (see <see cref="Welcome"/>).
/// </summary>
public partial class MeetSheet : Window
{
    public event Action? AddCard, AddVoice, SetUpVoices, ShowManner;
    /// <summary>How they sit, as chosen.</summary>
    public event Action<Companion.Manner>? MannerChosen;
    /// <summary>The name she calls the player by, as it is typed.</summary>
    public event Action<string>? Named;

    private VBoxContainer _column = null!;

    public void Build()
    {
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;
        var sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        _column = new VBoxContainer();
        sheet.AddChild(_column);
    }

    public void Open() => Paper.Open(this, new Vector2(520, 620), besideCabinet: true);       // they are seen as their manner is chosen

    /// <param name="persona">Where who they are comes from ("their character card"), or null: none yet.</param>
    /// <param name="voiced">They have a voice of their own.</param>
    /// <param name="voices">Voices can be spoken on this PC (GPT-SoVITS is set up).</param>
    /// <param name="manner">How they sit (see <see cref="Companion.Manner"/>).</param>
    public void Fill(string name, string? persona, bool voiced, bool voices, string? callMe, Companion.Manner manner)
    {
        Title = $"Meet {name}";
        foreach (var old in _column.GetChildren())
        {
            _column.RemoveChild(old);                       // gone now, not at the frame's end: the new parts come in its place
            old.QueueFree();
        }
        Form.Page(_column, $"Meet {name}");

        Form.Subheading(_column, $"Who {name} is");
        if (persona is not null)
            Form.Say(Form.Result(_column), $"From {persona}.", Paper.Good);
        else
        {
            Form.Hint(_column, $"No persona yet: {name} talks as a plain, friendly companion. A character card (.png or .json, SillyTavern " +
                               "style) says who they are and how they talk.");
            Form.Button(Form.Buttons(_column), "Add a character card…", () => AddCard?.Invoke());
        }

        Form.Subheading(_column, $"{name}'s voice");
        if (voiced)
            Form.Say(Form.Result(_column), voices ? $"{name} speaks with their own voice." : $"{name} has a voice; GPT-SoVITS speaks it, once set up.",
                     voices ? Paper.Good : Paper.Letters);
        else
        {
            Form.Hint(_column, $"No voice yet: {name} writes but doesn't speak. Clips of a voice with their words (or a .zip of them) give {name} one.");
            Form.Button(Form.Buttons(_column), "Add a voice…", () => AddVoice?.Invoke());
        }
        if (!voices)
            Form.Button(Form.Buttons(_column), "Set up voices…", () => SetUpVoices?.Invoke());

        Form.Subheading(_column, $"How {name} sits");
        CharacterSettings.Manners(_column, manner, chosen => MannerChosen?.Invoke(chosen), () => ShowManner?.Invoke());

        Form.Subheading(_column, "What they call you");
        Form.Field(Form.Grid(_column), "Your name", callMe, "your name, or what you like to be called", typed: text => Named?.Invoke(text));
        Form.Hint(_column, $"Every character calls you by it, unless their card says otherwise. What you have told any of them about yourself, " +
                           $"{name} knows too; your talks with each are your own.");

        var done = Form.Buttons(_column);
        done.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        Form.Button(done, "Done", QueueFree, quiet: false);
    }

    /// <summary>Dev: presses the button or types in the field with this label.</summary>
    public void DevDo(string label, string? value) => SettingsWindow.DevDo(this, label, value, Title);
}
