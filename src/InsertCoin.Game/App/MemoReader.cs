using System.Globalization;
using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// A memo, whole, on the cabinet's paper: its title, who it's from and when, and the text, with code in a fixed-width
/// font; its paper colour and sticker, to tell it apart. From here it can be copied, pinned on the cabinet's side,
/// taken off into the archive, changed or thrown away; and a memo of the player's own is written here, with a tick
/// for whether she reads it.
/// </summary>
public partial class MemoReader : Window
{
    private static readonly Font Mono = new SystemFont { FontNames = ["Cascadia Mono", "Consolas", "Courier New"] };

    private Memos _memos = null!;
    private Memo? _memo;                                    // null while a new one is written
    private Label _title = null!, _from = null!, _note = null!;
    private RichTextLabel _text = null!;
    private LineEdit _titleField = null!;
    private TextEdit _textField = null!;
    private Button _copy = null!, _pin = null!, _off = null!, _edit = null!, _delete = null!, _save = null!, _cancel = null!;
    private bool _deleting;
    private string _colour = Memo.Yellow;
    private string? _sticker;
    private readonly List<(string Colour, Button Button)> _swatches = [];
    private readonly List<(string? Sticker, Button Button)> _stickers = [];
    private CheckBox _tell = null!;

    public void Build(Memos memos)
    {
        _memos = memos;
        Title = "Memo";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        page.AddChild(column);

        _title = new Label { ThemeTypeVariation = "Heading", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        _from = new Label { ThemeTypeVariation = "Hint" };
        _titleField = new LineEdit { PlaceholderText = "A title", MaxLength = Memo.TitleLength, Visible = false };
        column.AddChild(_title);
        column.AddChild(_titleField);
        column.AddChild(_from);

        // its paper and sticker: to tell it apart, on the cabinet and here
        var look = new HBoxContainer();
        look.AddThemeConstantOverride("separation", 5);
        foreach (var colour in Memo.Colours)
        {
            var swatch = new Button { CustomMinimumSize = new Vector2(24, 24), TooltipText = colour, FocusMode = Control.FocusModeEnum.None };
            swatch.Pressed += () => Choose(colour, _sticker);
            look.AddChild(swatch);
            _swatches.Add((colour, swatch));
        }
        look.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        foreach (var sticker in Memo.Stickers.Prepend(null))
        {
            var button = new StickerButton(sticker) { CustomMinimumSize = new Vector2(28, 28), TooltipText = sticker ?? "no sticker", FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Choose(_colour, sticker);
            look.AddChild(button);
            _stickers.Add((sticker, button));
        }
        column.AddChild(look);

        _text = new RichTextLabel
        {
            BbcodeEnabled = true, SelectionEnabled = true, ContextMenuEnabled = true, FitContent = false, ScrollActive = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.Click,
        };
        _text.AddThemeFontOverride("mono_font", Mono);
        _text.AddThemeFontSizeOverride("mono_font_size", 14);
        _text.AddThemeFontSizeOverride("normal_font_size", 16);
        _text.AddThemeFontSizeOverride("bold_font_size", 16);
        _text.AddThemeFontOverride("bold_font", Paper.Font(620));
        _text.AddThemeColorOverride("default_color", Paper.Letters);
        _text.AddThemeConstantOverride("line_separation", 3);
        _text.AddThemeStyleboxOverride("normal", Paper.Box(new Color("FBF6EE"), new Color("E2DAF7"), 1, 8, 14, 12));
        _textField = new TextEdit
        {
            PlaceholderText = "What to keep: a list, a plan, something to remember…", SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        column.AddChild(_text);
        column.AddChild(_textField);
        _tell = new CheckBox { ButtonPressed = true, Visible = false };
        column.AddChild(_tell);
        _note = new Label { ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        column.AddChild(_note);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _copy = Button("Copy", "Quiet", () =>
        {
            DisplayServer.ClipboardSet(_memo?.Text ?? "");
            Say("Copied.");
        });
        _pin = Button("Pin on the cabinet", "", () =>
        {
            if (_memo is null)
                return;
            if (_memos.Pin(_memo.Id))
                QueueFree();
            else
                Say($"The cabinet's side is full ({Memo.PinnedAtMost}): take one down first.");
        });
        _off = Button("Take it down", "Quiet", () =>
        {
            if (_memo is null)
                return;
            _memos.Archive(_memo.Id);
            QueueFree();
        });
        _edit = Button("Edit", "Quiet", () => Editing(true));
        _delete = Button("Delete", "Quiet", Delete);
        _save = Button("Save", "", Save);
        _cancel = Button("Cancel", "Quiet", () =>
        {
            if (_memo is null)
                QueueFree();
            else
                Editing(false);
        });
        foreach (var button in new[] { _copy, _edit, _delete })
            buttons.AddChild(button);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foreach (var button in new[] { _off, _pin, _cancel, _save })
            buttons.AddChild(button);
        column.AddChild(buttons);

        Button Button(string text, string look, Action pressed)
        {
            var button = new Button { Text = text, ThemeTypeVariation = look };
            button.Pressed += pressed;
            return button;
        }
    }

    /// <summary>Shows a memo to read.</summary>
    public void Read(Memo memo, string from)
    {
        _memo = memo;
        _colour = memo.Colour;
        _sticker = memo.Sticker;
        ShowLook();
        Title = memo.Title;
        _title.Text = memo.Title;
        _from.Text = $"{from} · {memo.At.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)}";
        _text.Text = Memo.ToBbcode(memo.Text);
        _pin.Visible = memo.Place != MemoPlace.Pinned;
        _off.Visible = memo.Place != MemoPlace.Archived;
        _off.Text = memo.Place == MemoPlace.Hanging ? "Tear it off" : "Take it down";
        _off.TooltipText = "Off the cabinet; it stays in the memory book's Memos tab.";
        Editing(false);
        Open();
    }

    /// <summary>A new memo of the player's own, to write.</summary>
    public void Write()
    {
        _memo = null;
        _colour = Memo.Yellow;
        _sticker = null;
        ShowLook();
        Title = "A memo to yourself";
        _titleField.Text = "";
        _textField.Text = "";
        Editing(true);
        Open();
        _titleField.GrabFocus();
    }

    private void Open()
    {
        if (!Visible)
            Paper.Open(this, new Vector2(520, 600));
    }

    private void Editing(bool editing)
    {
        if (editing && _memo is not null)
        {
            _titleField.Text = _memo.Title;
            _textField.Text = _memo.Text;
        }
        _title.Visible = _text.Visible = !editing;
        _titleField.Visible = _textField.Visible = editing;
        _from.Visible = _memo is not null;
        _copy.Visible = _edit.Visible = _delete.Visible = !editing;
        _pin.Visible = !editing && _memo is { Place: not MemoPlace.Pinned };
        _off.Visible = !editing && _memo is { Place: not MemoPlace.Archived };
        _save.Visible = _cancel.Visible = editing;
        _save.Text = _memo is null ? "Print it" : "Save";
        _note.Text = editing && _memo is null ? "It prints out of the cabinet's slot, to pin on its side or keep." : "";
        _tell.Visible = editing && _memo is null && _memos.HerName is not null;
        _tell.Text = $"Let {_memos.HerName} read it";
        _tell.TooltipText = "She reads it and may act on it (a reminder, a date on the agenda), or let it be. Untick to keep it to yourself.";
        _deleting = false;
        _delete.Text = "Delete";
    }

    /// <summary>A paper or sticker chosen: a memo being read changes at once; one being written takes it when printed.</summary>
    private void Choose(string colour, string? sticker)
    {
        _colour = colour;
        _sticker = sticker;
        ShowLook();
        if (_memo is not null)
        {
            _memos.Style(_memo.Id, colour, sticker);
            _memo = _memo with { Colour = colour, Sticker = sticker };
        }
    }

    /// <summary>The chosen paper and sticker are outlined; the page takes the paper's colour.</summary>
    private void ShowLook()
    {
        foreach (var (colour, swatch) in _swatches)
            foreach (var state in new[] { "normal", "hover", "pressed" })
                swatch.AddThemeStyleboxOverride(state, Paper.Box(MemoLook.Paper(colour), colour == _colour ? Paper.Accent : new Color("C9BFE6"), colour == _colour ? 3 : 1, 6, 0, 0));
        foreach (var (sticker, button) in _stickers)
            ((StickerButton)button).Chosen = sticker == _sticker;
        _text.AddThemeStyleboxOverride("normal", Paper.Box(MemoLook.Paper(_colour), new Color("E2DAF7"), 1, 8, 14, 12));
        _textField.AddThemeStyleboxOverride("normal", Paper.Box(MemoLook.Paper(_colour), new Color("C9BFE6"), 1, 8, 10, 6));
        _textField.AddThemeStyleboxOverride("focus", Paper.Box(MemoLook.Paper(_colour), Paper.Accent, 2, 8, 10, 6));
        _textField.AddThemeColorOverride("background_color", MemoLook.Paper(_colour));
    }

    /// <summary>A sticker to choose (or none), drawn as it is on the paper.</summary>
    private partial class StickerButton(string? sticker) : Button
    {
        private bool _chosen;

        public bool Chosen
        {
            set
            {
                _chosen = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            Flat = true;
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
                AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        public override void _Draw()
        {
            var centre = Size / 2;
            if (_chosen)
                DrawCircle(centre, Size.X / 2, Paper.Wash);
            if (sticker is null)
            {
                DrawArc(centre, 7, 0, Mathf.Tau, 20, Paper.Faint, 1.6f, true);
                DrawLine(centre + new Vector2(-5, 5), centre + new Vector2(5, -5), Paper.Faint, 1.6f, true);
            }
            else
                MemoLook.Sticker(this, sticker, centre, Size.X * 0.7f);
            if (_chosen)
                DrawArc(centre, Size.X / 2 - 1, 0, Mathf.Tau, 28, Paper.Accent, 2, true);
        }
    }

    private void Save()
    {
        var title = _titleField.Text.Trim();
        var text = _textField.Text.Trim();
        if (text.Length == 0)
        {
            Say("Write something first.");
            return;
        }
        if (title.Length == 0)
            title = text.Split('\n')[0] is var first && first.Length > 40 ? first[..40].TrimEnd() + "…" : text.Split('\n')[0];
        if (_memo is null)
        {
            _memos.Print(title, text, by: null, colour: _colour, sticker: _sticker, tell: _tell.Visible && _tell.ButtonPressed);
            QueueFree();
            return;
        }
        _memos.Save(_memo.Id, title, text);
        _memos.Open(_memo.Id);                              // shown again as saved
    }

    /// <summary>Deleting asks once more: a second press within a few seconds.</summary>
    private void Delete()
    {
        if (_memo is null)
            return;
        if (!_deleting)
        {
            _deleting = true;
            _delete.Text = "Really delete?";
            GetTree().CreateTimer(4).Timeout += () =>
            {
                if (IsInstanceValid(this))
                {
                    _deleting = false;
                    _delete.Text = "Delete";
                }
            };
            return;
        }
        _memos.Delete(_memo.Id);
        QueueFree();
    }

    private void Say(string text) => _note.Text = text;

    /// <summary>Dev: presses a button by its name (copy, pin, off, edit, delete, save, cancel).</summary>
    public void DevPress(string button)
    {
        if (button.StartsWith("colour:"))
            Choose(button[7..], _sticker);
        else if (button.StartsWith("sticker:"))
            Choose(_colour, button[8..] is { Length: > 0 } sticker ? sticker : null);
        else if (button.StartsWith("write:"))                // write:title:text fills the fields of a new one
        {
            var parts = button[6..].Split(':', 2);
            _titleField.Text = parts[0];
            _textField.Text = parts.Length > 1 ? parts[1] : "";
        }
        else if (button == "tell")
            _tell.ButtonPressed = !_tell.ButtonPressed;
        else
            Press(button);
    }

    private void Press(string button) => (button switch
    {
        "copy" => _copy, "pin" => _pin, "off" => _off, "edit" => _edit, "delete" => _delete, "save" => _save, _ => _cancel,
    }).EmitSignal(BaseButton.SignalName.Pressed);
}
