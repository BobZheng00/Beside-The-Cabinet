using Godot;
using InsertCoin.Adapters;
using InsertCoin.Core;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>
/// First-run setup on the cabinet screen, arcade style: ENTER YOUR INITIALS, then the LeetCode player —
/// typed as a username or pasted as a profile link — checked against the task source before it is accepted. The
/// LeetCode name is optional: Enter with none goes on without one (focus and the rest earn play too).
/// </summary>
public sealed class Onboarding(ScreenView screen, ITaskSource tasks, AudioBank audio)
{
    private enum Step { Initials, Player, Checking }

    /// <summary>Raised with (initials, username) once the player has been verified; the username is null when they went on without one.</summary>
    public event Action<string, string?>? Completed;
    public event Action? Cancelled;

    private Step _step;
    private string _initials = "", _player = "", _note = "";
    private bool _cursor = true, _canCancel;

    public bool Active { get; private set; }

    /// <summary>Starts setup. With <paramref name="keepInitials"/> only the player is asked (changing player later).</summary>
    public void Start(string? keepInitials = null)
    {
        Active = true;
        _canCancel = keepInitials is not null;
        _initials = keepInitials ?? "";
        _player = _note = "";
        _step = keepInitials is null ? Step.Initials : Step.Player;
        Draw();
    }

    public void Blink()
    {
        if (!Active)
            return;
        _cursor = !_cursor;
        Draw();
    }

    public void Key(InputEventKey key)
    {
        if (!Active || _step == Step.Checking)
            return;
        _note = "";
        audio.Play("key");
        if (key.Keycode == Godot.Key.Escape && _canCancel)
        {
            Finish();
            Cancelled?.Invoke();
            return;
        }
        if (key.Keycode == Godot.Key.V && key.IsCommandOrControlPressed())
            Type(DisplayServer.ClipboardGet());
        else if (key.Keycode == Godot.Key.Backspace)
            Edit(text => text.Length > 0 ? text[..^1] : text);
        else if (key.Keycode is Godot.Key.Enter or Godot.Key.KpEnter)
            Confirm();
        else if (key.Unicode >= 32 && !key.IsCommandOrControlPressed())
            Type(char.ConvertFromUtf32((int)key.Unicode));
        Draw();
    }

    private void Type(string text)
    {
        text = new string([.. text.Where(c => !char.IsControl(c))]);
        if (_step == Step.Initials)
            Edit(current => Limit(current + new string([.. text.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit)]), 3));
        else
            Edit(current => Limit(current + text, 120));
    }

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];

    private void Edit(Func<string, string> change)
    {
        if (_step == Step.Initials)
            _initials = change(_initials);
        else
            _player = change(_player);
    }

    private async void Confirm()
    {
        if (_step == Step.Initials)
        {
            if (_initials.Length == 0)
                _note = "AT LEAST ONE LETTER";
            else
                _step = Step.Player;
            return;
        }

        if (_player.Trim().Length == 0)
        {
            Finish();
            Completed?.Invoke(_initials, null);
            return;
        }
        if (!LeetCodeProfile.TryParse(_player, out var username, out var error))
        {
            _note = error!;
            audio.Play("error");
            return;
        }
        _step = Step.Checking;
        Draw();
        try
        {
            if (await tasks.PlayerExistsAsync(username!, CancellationToken.None))
            {
                Finish();
                Completed?.Invoke(_initials, username!);
                return;
            }
            _note = "PLAYER NOT FOUND";
        }
        catch (TaskSourceUnavailableException)
        {
            _note = "LINK DOWN - TRY AGAIN";
        }
        audio.Play("error");
        _step = Step.Player;
        Draw();
    }

    private void Finish()
    {
        Active = false;
        screen.ClearPrompt();
    }

    private void Draw()
    {
        if (!Active)
            return;
        var cursor = _cursor ? "_" : " ";
        switch (_step)
        {
            case Step.Initials:
                screen.Prompt(_initials + (_initials.Length < 3 ? cursor : ""), "ENTER YOUR INITIALS", Note("TYPE, THEN ENTER"));
                break;
            case Step.Player:
                var shown = _player.Length > 22 ? "<" + _player[^21..] : _player;
                screen.Prompt(shown + cursor, "LEETCODE NAME\nOR PROFILE LINK", Note(_canCancel ? "CTRL+V PASTES - ESC CANCELS" : "OPTIONAL - ENTER TO SKIP"));
                break;
            case Step.Checking:
                screen.Prompt("...", "ASKING LEETCODE", "PLEASE WAIT");
                break;
        }
    }

    private string Note(string hint) => _note.Length > 0 ? _note : hint;
}
