using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Characters: the name she calls the player by; who sits on the stool, or nobody; whether she says her lines
/// in speech bubbles; adding a character (a 3D model, a character card, a voice), and the folder they are kept in. Each
/// change is used at once.
/// </summary>
public partial class CharacterSettings : VBoxContainer
{
    /// <summary>Someone on the stool (true), or nobody.</summary>
    public event Action<bool>? ShownChosen;
    /// <summary>Speech bubbles on or off.</summary>
    public event Action<bool>? BubblesChosen;
    /// <summary>Another character takes the stool (their id).</summary>
    public event Action<string>? Seated;
    /// <summary>The name she calls the player by, as it is typed.</summary>
    public event Action<string>? Named;
    /// <summary>"Add a character…" and "Open characters folder".</summary>
    public event Action? Add, OpenFolder;

    public void Build((string Id, string Name)[] characters, string seated, bool shown, bool bubbles, string? callMe)
    {
        Form.Page(this, "Characters");
        Form.Field(Form.Grid(this), "Your name", callMe, "what she calls you", typed: text => Named?.Invoke(text));
        Form.Tick(this, "Someone on the stool", shown, on => ShownChosen?.Invoke(on));
        Form.Hint(this, "Untick it to leave the stool empty: the cabinet goes on as before, without her.");
        Form.Tick(this, "Speech bubbles", bubbles, on => BubblesChosen?.Invoke(on));
        Form.Hint(this, "Without them she still reacts to what happens, but says nothing unless you talk to her.");

        Form.Subheading(this, "On the stool");
        var group = new ButtonGroup();
        foreach (var (id, name) in characters)
        {
            var pick = new CheckBox { Text = name, ButtonGroup = group, ButtonPressed = id == seated };
            pick.Toggled += on =>
            {
                if (on)
                    Seated?.Invoke(id);
            };
            AddChild(pick);
        }
        var buttons = Form.Buttons(this);
        Form.Button(buttons, "Add a character…", () => Add?.Invoke());
        Form.Button(buttons, "Open characters folder", () => OpenFolder?.Invoke());
        Form.Hint(this, "A 3D character (.vrm, .glb, .gltf, or a .zip holding one) takes the stool. A character card (.png or .json, " +
                        "SillyTavern style) gives whoever is on the stool a persona; a voice's clips or .zip give her a voice. Dropping " +
                        "any of them on the cabinet does the same. Files put in the folder by hand show up after a restart.");
    }
}
