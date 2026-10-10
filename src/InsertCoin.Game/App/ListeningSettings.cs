using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>Settings → Talking aloud: the talk key, and when she listens without it (see <see cref="HandsFree"/>). Each change is used at once.</summary>
public partial class ListeningSettings : VBoxContainer
{
    /// <summary>The talk key chosen (see <see cref="TalkKey"/>).</summary>
    public event Action<string>? KeyChosen;
    /// <summary>When she listens without the key, as ticked now.</summary>
    public event Action<HandsFree>? HandsFreeChosen;

    private HandsFree _handsFree = new();
    private CheckBox _conversationKey = null!, _followUp = null!, _afterAnswers = null!, _afterQuestions = null!, _afterRemarks = null!,
        _inCalls = null!, _inFullScreen = null!, _whilePlaying = null!;
    private SpinBox _followUpSeconds = null!;

    /// <param name="hears">This build has her ears (the speech model), so she can be talked to aloud.</param>
    /// <param name="turns">…and the turn-taking models, so she can listen hands-free.</param>
    public void Build(string name, string talkKey, HandsFree handsFree, bool hears, bool turns)
    {
        _handsFree = handsFree;
        Form.Page(this, "Talking aloud");
        var talk = Form.Grid(this);
        var key = Form.Choice(talk, "Talk key", TalkKey.Keys.Select(k => k.Label), Array.FindIndex(TalkKey.Keys, k => k.Name == talkKey),
                              index => KeyChosen?.Invoke(TalkKey.Keys[index].Name), wide: true);
        key.Disabled = !hears;
        Form.Hint(this, hears
            ? $"Talk to {name} aloud: hold the mouse on her, or hold this key in any app, and let go when you're done; she " +
              "answers what she heard. The microphone on the slip writes what you say into it instead, to change before sending. " +
              "Heard on this PC; nothing is recorded. The cabinet's screen shows MIC while the microphone is open."
            : "Talking aloud needs the speech model, which this build doesn't have (python tools/models/fetch.py listening).");

        AddChild(new HSeparator());
        Form.Subheading(this, "Without the key");
        var h = _handsFree;
        _conversationKey = Form.Tick(this, "Double-tap the talk key to start or end a conversation", h.ConversationKey, _ => Chosen());
        Form.Hint(this, $"In a conversation {name} listens after everything she says, so you can answer without the key, until you've " +
                        $"been quiet for {HandsFree.ConversationSeconds:0} seconds. The tray's Conversation switch does the same.");
        _followUp = Form.Tick(this, "Listen for a moment after she speaks", h.FollowUp, _ => Chosen());
        var seconds = Form.Indented(this);
        seconds.AddChild(new Label { Text = "for" });
        _followUpSeconds = new SpinBox
        {
            MinValue = HandsFree.Shortest, MaxValue = HandsFree.Longest, Step = 1, Value = h.FollowUpSeconds, Suffix = "seconds",
            CustomMinimumSize = new Vector2(140, 0),
        };
        _followUpSeconds.ValueChanged += _ => Chosen();
        seconds.AddChild(_followUpSeconds);
        _afterAnswers = Form.Tick(Form.Indented(this), "after she answers you", h.AfterAnswers, _ => Chosen());
        _afterQuestions = Form.Tick(Form.Indented(this), "after she asks you something on her own", h.AfterQuestions, _ => Chosen());
        _afterRemarks = Form.Tick(Form.Indented(this), "after anything else she says on her own", h.AfterRemarks, _ => Chosen());
        Form.Hint(this, "If you start talking within those seconds, she hears it as your answer; if not, the microphone closes. " +
                        "She never listens while she is speaking: talking over her still needs the key or a click.");

        AddChild(new Label { Text = "She may listen on her own…" });
        _inCalls = Form.Tick(Form.Indented(this), "during a call", h.InCalls, _ => Chosen());
        _inFullScreen = Form.Tick(Form.Indented(this), "in full screen or Windows' quiet time", h.InFullScreen, _ => Chosen());
        _whilePlaying = Form.Tick(Form.Indented(this), "while a game runs", h.WhilePlaying, _ => Chosen());

        void Enable()
        {
            foreach (var box in new[] { _conversationKey, _followUp, _inCalls, _inFullScreen, _whilePlaying })
                box.Disabled = !turns || !hears;
            foreach (var box in new[] { _afterAnswers, _afterQuestions, _afterRemarks })
                box.Disabled = !turns || !hears || !_followUp.ButtonPressed;
            _followUpSeconds.Editable = turns && hears && _followUp.ButtonPressed;
        }
        _followUp.Toggled += _ => Enable();
        Enable();
        if (!turns)
            Form.Hint(this, "Listening without the key needs the turn-taking models, which this build doesn't have (python tools/models/fetch.py turns).");
    }

    /// <summary>When she listens without the key, as ticked, handed on at once.</summary>
    private void Chosen()
    {
        _handsFree = _handsFree with
        {
            ConversationKey = _conversationKey.ButtonPressed, FollowUp = _followUp.ButtonPressed, FollowUpSeconds = _followUpSeconds.Value,
            AfterAnswers = _afterAnswers.ButtonPressed, AfterQuestions = _afterQuestions.ButtonPressed, AfterRemarks = _afterRemarks.ButtonPressed,
            InCalls = _inCalls.ButtonPressed, InFullScreen = _inFullScreen.ButtonPressed, WhilePlaying = _whilePlaying.ButtonPressed,
        };
        HandsFreeChosen?.Invoke(_handsFree);
    }
}
