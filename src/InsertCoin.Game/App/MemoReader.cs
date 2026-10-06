using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// A memo, whole, on the cabinet's paper: its title, who it's from and when, and the text, with code in a fixed-width
/// font. From here it can be copied, pinned on the cabinet's side, taken off into the archive, changed or thrown
/// away; and a memo of the player's own is written here.
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
        _deleting = false;
        _delete.Text = "Delete";
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
            _memos.Print(title, text, by: null);
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
    public void DevPress(string button) => (button switch
    {
        "copy" => _copy, "pin" => _pin, "off" => _off, "edit" => _edit, "delete" => _delete, "save" => _save, _ => _cancel,
    }).EmitSignal(BaseButton.SignalName.Pressed);
}
