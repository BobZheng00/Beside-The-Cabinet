using Godot;
using InsertCoin.Adapters;
using InsertCoin.Core;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>
/// The LeetCode player on the cabinet screen, arcade style (Settings → Balance → Change…): typed as a username or
/// pasted as a profile link, checked against the task source before it is accepted. Enter with none goes on without
/// one (focus and the rest earn play too); Esc leaves it as it was. The first run asks for the rest (see <see cref="Welcome"/>).
/// </summary>
public sealed class Onboarding(ScreenView screen, ITaskSource tasks, AudioBank audio)
{
    private enum Step { Player, Checking }

    /// <summary>Raised with the username once verified, or null when they went on without one.</summary>
    public event Action<string?>? Completed;
    public event Action? Cancelled;

    private Step _step;
    private string _player = "", _note = "";
    private bool _cursor = true;

    public bool Active { get; private set; }

    public void Start()
    {
        Active = true;
        _player = _note = "";
        _step = Step.Player;
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
        if (key.Keycode == Godot.Key.Escape)
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

    private void Type(string text) => Edit(current => Limit(current + new string([.. text.Where(c => !char.IsControl(c))]), 120));

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];

    private void Edit(Func<string, string> change) => _player = change(_player);

    private async void Confirm()
    {
        if (_player.Trim().Length == 0)
        {
            Finish();
            Completed?.Invoke(null);
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
                Completed?.Invoke(username!);
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
            case Step.Player:
                var shown = _player.Length > 22 ? "<" + _player[^21..] : _player;
                screen.Prompt(shown + cursor, "LEETCODE NAME\nOR PROFILE LINK", Note("CTRL+V PASTES - ESC CANCELS"));
                break;
            case Step.Checking:
                screen.Prompt("...", "ASKING LEETCODE", "PLEASE WAIT");
                break;
        }
    }

    private string Note(string hint) => _note.Length > 0 ? _note : hint;
}
