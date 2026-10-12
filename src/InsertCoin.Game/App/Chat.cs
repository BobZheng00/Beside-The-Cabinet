using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Core;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>
/// Talking with her. What the user types goes to their own provider; her answer streams into her bubble and its
/// stage directions are acted out, and she uses her tools on the way (see <see cref="Tools"/>). Cabinet events and
/// her canned lines go into the same conversation, so she knows what happened. After an answer the older
/// conversation is folded into a summary when it has grown long. Everything runs on the main thread: the mind's
/// awaits come back through Godot's synchronization context.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Chat : IDisposable
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(90);
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>The <see cref="UseSearch"/> choice for no local model: recall by shared words only.</summary>
    public const string WordsOnly = "words";

    private IChatClient? _client, _talk;                // _talk: her replies, cached where the provider needs asking (see Provider.ForConversation)
    private Provider? _provider;
    private int? _window;
    private readonly Queue<(long Input, long Cached)> _usage = new();
    private Conversation? _conversation;
    private LocalEmbedder? _embedder;
    private string? _search;
    private int _searchLoads;
    private CancellationTokenSource? _answering;
    private TaskCompletionSource? _written;             // done once the reply under way is written, or stopped
    private Speech? _speech;                            // what her voice makes of the latest reply
    private CompanionView? _view;                       // where the reply under way is played
    private bool _folding, _upkeeping, _cutting;
    private bool _usedTool;                             // a tool ran in the reply under way: done, even with no words
    private DateTimeOffset _upkeepAfter;
    private readonly Pages _pages = new(Pages.Web());
    private readonly LeetCodeProblems _leetCode = new(Http);
    private SearXng? _searxng;

    public Chat(string dataDir)
    {
        var folder = Path.Combine(dataDir, "companion");
        Store = new MindStore(Path.Combine(folder, "memory.db"));
        Keys = new KeyStore(Path.Combine(folder, "key.bin"));
        Tools = new Toolbox([BuiltIn.OpenLink(OpenPage), BuiltIn.PrintMemo(Memo), BuiltIn.Focus(FocusAct), BuiltIn.Music(MusicAct, name => Playlist?.Invoke(name) ?? "No playlists saved"),
                             BuiltIn.LookAtScreen(LookAtScreen, () => OpenWindows.Working()?.Title), BuiltIn.LogActivity(LogActivity), BuiltIn.JudgeProof(JudgeProof), BuiltIn.SetBedtime(SetBedtimeAct),
                             BuiltIn.WindowTitles(OpenWindows.Titles), BuiltIn.ReadPage(_pages.Read), BuiltIn.LeetCodeProblem(_leetCode.Describe),
                             BuiltIn.OpenApp(StartMenu.Apps, StartMenu.Open)])
        {
            AskPlayer = AskFirst,
        };
        Tools.Starting += tool =>
        {
            if (tool.Name == "print_memo" && _view is { } view && GodotObject.IsInstanceValid(view))
                view.Typing();                              // she writes it on her phone while the model writes it
        };
        Tools.Using += tool =>
        {
            GD.Print($"Tools: she uses {tool.Name}");
            _usedTool = true;
            _answering?.CancelAfter(AnswerTimeout);         // each step of a longer piece of work gets the reply's full time again
            if (tool.Name != "print_memo" && _view is { } view && GodotObject.IsInstanceValid(view))
                view.Working();                             // a memo is sent instead (see PrintMemo)
        };
    }

    public MindStore Store { get; }
    public KeyStore Keys { get; }

    /// <summary>What she can do with tools, and the player's permission for each (Settings → Abilities).</summary>
    public Toolbox Tools { get; }

    /// <summary>Prints a memo (title, text, the character printing it) and says what she is told; unset, nothing prints.</summary>
    public Func<string, string, string, string>? PrintMemo { get; set; }

    private string Memo(string title, string text) =>
        PrintMemo is { } print && _conversation is { } conversation ? print(title, text, conversation.Character) : "The cabinet couldn't print it.";

    /// <summary>Runs focus sessions for her tool (the action, minutes of focus and of breaks) and says how things stand.</summary>
    public Func<string, int?, int?, string>? Focus { get; set; }

    /// <summary>Controls the player's music for her tool (the action, a playlist's name) and says what came of it.</summary>
    public Func<string, string?, Task<string>>? Music { get; set; }

    /// <summary>The saved playlist a name means, as her card shows it.</summary>
    public Func<string?, string>? Playlist { get; set; }

    private string FocusAct(string action, int? focus, int? rest) => Focus?.Invoke(action, focus, rest) ?? "Focus sessions aren't available here.";

    /// <summary>The activities that count, for her tools (see <see cref="Mind.Activities"/>).</summary>
    public Activities? Activities { get; set; }

    /// <summary>The pictures and files handed to her with the reply under way (for judging proof).</summary>
    public IReadOnlyList<Attachment> InFront { get; private set; } = [];

    private Task<string> LogActivity(string name, Size? size, int? minutes, bool? provable, CancellationToken cancel) =>
        Activities?.Log(name, size, minutes, provable, confirmed: false, cancel) ?? Task.FromResult("Activities can't be counted here.");

    private string JudgeProof(string name, Conviction conviction, string saw) => Activities?.Judge(name, conviction, saw) ?? "Proof can't be judged here.";

    /// <summary>Sets the player's bedtime for her tool (the time she gave) and says what came of it.</summary>
    public Func<string, CancellationToken, Task<string>>? Bedtime { get; set; }

    private Task<string> SetBedtimeAct(string time, CancellationToken cancel) => Bedtime?.Invoke(time, cancel) ?? Task.FromResult("Bedtimes can't be set here.");

    /// <summary>
    /// Takes a screenshot for her tool, of the whole screen or of the window the player was working in: the picture, and
    /// what it is of; null when it can't be taken.
    /// </summary>
    public Func<bool, (Attachment Shot, string Of)?>? Screen { get; set; }

    private (Attachment Shot, string Of)? _shot;        // a screenshot she took in the reply under way, shown to her right after it

    private string LookAtScreen(bool whole)
    {
        if (Screen?.Invoke(whole) is not { } shot)
            return "The screenshot couldn't be taken.";
        _shot = shot;
        return "Taken: you'll see it right after this reply. Say only a few words now (that you're having a look).";
    }

    /// <summary>The screenshot she took in her last reply, and what it is of, to show her now; null when she took none.</summary>
    public (Attachment Shot, string Of)? TakeShot()
    {
        var shot = _shot;
        _shot = null;
        return shot;
    }

    /// <summary>Whether a model is known not to take pictures or PDFs (none is sent to it; see <see cref="Conversation.Unseeing"/>).</summary>
    public Func<string, bool> CantSee { get; set; } = _ => false;

    /// <summary>A reply found that the model can't take pictures or PDFs: remember it for that model.</summary>
    public event Action<string>? FoundCantSee;

    private Task<string> MusicAct(string action, string? playlist) => Music?.Invoke(action, playlist) ?? Task.FromResult("The player's music can't be reached from here.");

    /// <summary>Asks the player whether she may use a tool (the card by her stool). Unset, the answer is always no.</summary>
    public Func<Asking, CancellationToken, Task<Answer>>? AskPlayer { get; set; }

    /// <summary>While the player decides, the reply's clock stops: they may take their time, and the card has a limit of its own.</summary>
    private async Task<Answer> AskFirst(Asking asking, CancellationToken cancel)
    {
        if (AskPlayer is null)
            return Answer.None;
        var answering = _answering;
        answering?.CancelAfter(Timeout.InfiniteTimeSpan);
        try
        {
            return await AskPlayer(asking, cancel);
        }
        finally
        {
            if (answering is not null && answering == _answering)
                answering.CancelAfter(AnswerTimeout);
        }
    }

    /// <summary>Opens a page in the player's browser. A test run (off screen) only says so: it never opens anything or takes focus.</summary>
    private static void OpenPage(Uri page)
    {
        if (Paper.OffScreen)
        {
            GD.Print($"Tools: would open {page.AbsoluteUri} (a test run opens nothing)");
            return;
        }
        OS.ShellOpen(page.AbsoluteUri);
    }

    /// <summary>A provider is set up and someone is on the stool.</summary>
    public bool Ready => _client is not null && _conversation is not null;
    public bool Answering => _answering is not null;

    /// <summary>A reply is asked for and none of it is out yet: she is still thinking.</summary>
    public bool StillThinking => _answering is not null && !_wordsOut;
    private bool _wordsOut;

    /// <summary>Her last reply as shown, once it has come in whole; null while one is coming, or when it failed or was cut off.</summary>
    public string? Replied { get; private set; }
    public Conversation? Conversation => _conversation;

    /// <summary>What her voice makes of her latest reply (whether it is subtitled, say); null once she was cut off.</summary>
    public Speech? Speech => _speech;

    /// <summary>Uses this provider (with the stored key) from now on, or none.</summary>
    public void Connect(Provider? provider)
    {
        _client?.Dispose();
        _provider = provider;
        // every request counted (see Metered, and the chat settings' Usage tab): her replies apart from her upkeep
        var timed = provider?.Connect(Keys.Load()) is { } client ? new Timed(client, Used) : null;
        _client = timed is null ? null : new Metered(timed, Store, provider!.Model, "upkeep");
        _talk = timed is null ? null : new Metered(provider!.ForConversation(timed), Store, provider.Model, "replies");
        _usage.Clear();
        Tools.Refused = false;                          // a new provider or model may take tools
        Tools.HostedRefused = false;
        UseWebSearch();
        UseWindow(_window);
        UseSearch(_search);
    }

    /// <summary>
    /// Searches the web through the player's own SearXNG (the add-on) from now on, or, with null, through the provider's
    /// own search where it has one.
    /// </summary>
    public void SearchThrough(Uri? searxng)
    {
        _searxng = searxng is null ? null : new SearXng(Http, searxng);
        UseWebSearch();
    }

    /// <summary>How she searches the web now, in a few words for the settings; null when she can't.</summary>
    public string? WebSearch => _searxng is { } searx ? $"through your SearXNG at {searx.Address.Authority}"
                              : _provider?.Protocol == Protocol.Anthropic ? (Tools.HostedRefused ? null : "with your provider's own search (Anthropic's)")
                              : null;

    /// <summary>Her web search: the player's SearXNG when the add-on is on, else the provider's own (Anthropic's), else none.</summary>
    private void UseWebSearch()
    {
        if (_searxng is { } searx)
            Tools.Put(BuiltIn.WebSearch(searx.Search));
        else if (_provider?.Protocol == Protocol.Anthropic)
            Tools.Put(BuiltIn.ProviderSearch());
        else
            Tools.Take(BuiltIn.SearchName);
    }

    /// <summary>A search through SearXNG at <paramref name="address"/>, for the add-on's Test button: how many results, or what is wrong.</summary>
    public static async Task<string> TrySearXng(Uri address)
    {
        var found = await new SearXng(Http, address).Results("LeetCode two sum");
        return found.Count == 0 ? "SearXNG answered, but with no results: check its search engines." : $"Works: {found.Count} results, the first {found[0].Url}";
    }

    /// <summary>How much of the conversation she keeps word for word (tokens); null: what suits the provider.</summary>
    public void UseWindow(int? tokens)
    {
        _window = tokens;
        if (_conversation is not null)
            _conversation.Window = Window;
    }

    public int Window => _window ?? _provider?.DefaultWindow ?? Prompt.DefaultWindow;

    /// <summary>Of the input her last replies read, the share the provider served from its cache (by its own count), and over how many replies; null before any.</summary>
    public (double Share, int Replies)? Cached =>
        _usage.Count == 0 || _usage.Sum(u => u.Input) == 0 ? null : ((double)_usage.Sum(u => u.Cached) / _usage.Sum(u => u.Input), _usage.Count);

    private void Used(UsageDetails usage)
    {
        _usage.Enqueue((usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0));
        while (_usage.Count > 20)
            _usage.Dequeue();
    }

    /// <summary>Someone else is on the stool: talk continues in their own conversation.</summary>
    public void Seat(string characterId, Persona persona)
    {
        _answering?.Cancel();
        _conversation = new Conversation(Store, characterId, persona) { Embedder = _embedder, Window = Window, ReminderLead = ReminderLead, KeepsInTouch = KeepsInTouch };
        _recorded = default;                                // their diary catches up on the days too
    }

    /// <summary>The player's "remind me before things on my agenda", as set now (see <see cref="Conversation.ReminderLead"/>).</summary>
    public Func<TimeSpan?> ReminderLead { get; init; } = () => null;

    /// <summary>The player chose "Keeps in touch", as set now (see <see cref="Conversation.KeepsInTouch"/>).</summary>
    public Func<bool> KeepsInTouch { get; init; } = () => false;

    /// <summary>The local models for search by meaning that this build carries (a light build may leave the big one out).</summary>
    public static EmbeddingModel[] Searchable() => [.. EmbeddingModel.All.Where(m => Godot.FileAccess.FileExists($"res://models/{m.Name}/model.onnx"))];

    /// <summary>The model searching now, if any (it may still be loading).</summary>
    public EmbeddingModel? Searching => _embedder?.Model;

    /// <summary>
    /// Search by meaning with this model (a name from <see cref="Searchable"/>, <see cref="WordsOnly"/>, or null for
    /// the best one present). It loads in the background, a second or two, and only while a provider is set up:
    /// someone who never chats does not pay for it in memory.
    /// </summary>
    public async void UseSearch(string? choice)
    {
        _search = choice;
        var model = _client is null || choice == WordsOnly ? null : Searchable().FirstOrDefault(m => m.Name == choice) ?? Searchable().FirstOrDefault();
        if (model?.Name == _embedder?.Model.Name)
            return;
        var load = ++_searchLoads;
        LocalEmbedder? loaded = null;
        if (model is not null)
            try
            {
                loaded = await Task.Run(() =>
                {
                    var folder = $"res://models/{model.Name}/";
                    using var tokenizer = new MemoryStream(Godot.FileAccess.GetFileAsBytes(folder + model.TokenizerFile));
                    return new LocalEmbedder(model, ModelFiles.OnDisk(folder + "model.onnx", model.Name), tokenizer);
                });
            }
            catch (Exception e)
            {
                GD.PrintErr($"Chat: could not load {model.Name}, recalling by shared words: {e.Message}");
            }
        if (load != _searchLoads)
        {
            loaded?.Dispose();                  // a newer choice came in while this one loaded
            return;
        }
        var old = _embedder;
        _embedder = loaded;
        if (_conversation is not null)
            _conversation.Embedder = loaded;
        old?.Dispose();
        GD.Print(loaded is null ? "Chat: recalling by shared words" : $"Chat: recalling by meaning with {loaded.Model.Name}");
        await Index();
    }

    /// <summary>Gives what she knows vectors from the loaded model, a batch at a time.</summary>
    private async Task Index()
    {
        if (_conversation is not { Embedder: not null } conversation)
            return;
        try
        {
            await conversation.Index();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: indexing memories failed, will try again later: {e.Message}");
        }
    }

    /// <summary>
    /// Sends what the user said and plays her answer on <paramref name="view"/>; with <paramref name="speech"/>, her
    /// voice says it too (and she is told how her voice works).
    /// </summary>
    /// <param name="voiceLanguage">
    /// The language of her voice whenever she has one switched on, ready this moment or not: her instructions stay the
    /// same while her voice rests (during a game), so the start of the prompt stays cached.
    /// </param>
    /// <param name="handed">What the player hands her with it: pictures, PDFs, text files (see <see cref="Attachment"/>).</param>
    public Task Ask(string said, string situation, CompanionView view, Speech? speech = null, string? voiceLanguage = null, IReadOnlyList<Attachment>? handed = null) =>
        Respond(said, fromCabinet: false, fallback: null, situation, view, speech, voiceLanguage, handed: handed);

    /// <summary>
    /// Has her pass on what the cabinet asks (reminders going off, see <see cref="Agenda.Announcement"/>) in her own
    /// words, shown and said as a reply is; when that fails, she says <paramref name="fallback"/> instead.
    /// </summary>
    /// <param name="call">
    /// A word first (see <see cref="WordFirst"/>): her call is asked for on its own while her remark is written, whole,
    /// before either is shown or said; then her call is, and this waits for the player's answer, given the remark (for
    /// a ticket): true, she goes on aloud; false, the rest is only shown.
    /// </param>
    public Task Announce(string note, string fallback, string situation, CompanionView view, Speech? speech = null, string? voiceLanguage = null,
                         IReadOnlyList<Attachment>? handed = null, Func<string, CancellationToken, Task<bool>>? call = null) =>
        Respond(note, fromCabinet: true, fallback, situation, view, speech, voiceLanguage, handed: handed, call: call);

    /// <summary>
    /// Has her think over what the cabinet tells her (a memo the player wrote, see <see cref="Memo.Written"/>) without
    /// being seen to: no thinking dots, nothing shown while it comes. What she does with it (a reminder, a date on the
    /// agenda) is done either way; only if she has something to say is it then said, as a reply is. Silent, her answer
    /// is only [quiet], and <see cref="Replied"/> is null.
    /// </summary>
    /// <param name="voice">Starts her voice for what she says, when she says something (starting it stops whatever it was saying).</param>
    public Task Consider(string note, string situation, CompanionView view, Func<Speech?> voice, string? voiceLanguage = null) =>
        Respond(note, fromCabinet: true, fallback: "", situation, view, speech: null, voiceLanguage, voice);

    /// <summary>
    /// Has her write what the cabinet asks as a text to the player's phone, as they are away from the PC (see
    /// <see cref="Reaching"/>): written as a remark is and kept in the conversation, but nothing is shown, acted or said
    /// here. Her words as shown, or null when none came: her mind failed, or took longer than <paramref name="within"/>,
    /// and then nothing of hers is kept either.
    /// </summary>
    public async Task<string?> Text(string note, string situation, CompanionView view, string? voiceLanguage, TimeSpan? within = null)
    {
        if (!Ready || Answering)
            return null;
        await Respond(note, fromCabinet: true, fallback: null, situation, view, speech: null, voiceLanguage, texting: true, within: within);
        return Replied;
    }

    /// <summary>
    /// Has her answer what the player texted her from their phone (see <see cref="Reaching.Texted"/>): as she answers at
    /// the PC, and kept in the conversation, but nothing is shown or said here. Her words as shown, or null when none came.
    /// </summary>
    public async Task<string?> TextBack(string said, IReadOnlyList<Attachment> handed, string situation, CompanionView view, string? voiceLanguage)
    {
        if (!Ready || Answering)
            return null;
        await Respond(said, fromCabinet: false, fallback: null, situation, view, speech: null, voiceLanguage, handed: handed, texting: true, aside: Reaching.Texted);
        return Replied;
    }

    private async Task Respond(string said, bool fromCabinet, string? fallback, string situation, CompanionView view, Speech? speech, string? voiceLanguage,
                               Func<Speech?>? voiceLater = null, IReadOnlyList<Attachment>? handed = null,
                               Func<string, CancellationToken, Task<bool>>? call = null, bool texting = false, TimeSpan? within = null, string? aside = null)
    {
        if (_talk is null || _conversation is null || _answering is not null)
            return;
        _shot = null;
        InFront = handed ?? [];
        var client = _talk;
        var conversation = _conversation;
        conversation.VoiceLanguage = voiceLanguage ?? speech?.Language;
        using var answering = _answering = new CancellationTokenSource(AnswerTimeout);
        if (within is { } limit)
            answering.CancelAfter(limit);
        _written = new TaskCompletionSource();
        _speech = speech;
        _view = view;
        var refused = Tools.Refused;
        _usedTool = false;
        _wordsOut = false;
        bool Here() => GodotObject.IsInstanceValid(view) && conversation == _conversation;     // not swapped for someone else meanwhile
        var clock = Stopwatch.StartNew();
        long? shown = null;
        // thought over unseen (see Consider), or written whole while her call is asked for (see WordFirst): her words and
        // directions are held, and played after; a text for the player's phone is held and never played
        var held = voiceLater is null && call is null && !texting ? null : new List<Piece>();
        var began = false;
        try
        {
            if (held is null)
            {
                view.BeginReply();
                began = true;
            }
            // her tools go with every reply, check-ins and reminders too: one list, so the cached start of the prompt holds
            Func<IChatClient, string, string, Action<string>, Action<string, string?>, CancellationToken, Toolbox?, IReadOnlyList<Attachment>?, Task<string>> reply =
                fromCabinet ? (c, note, s, t, d, k, tools, h) => conversation.Announce(c, note, s, t, d, k, tools, h, call is null ? null : WordFirst.Called, texting)
                            : (c, words, s, t, d, k, tools, h) => conversation.Answer(c, words, s, t, d, k, tools, h, aside, texting);
            Replied = null;
            var model = _provider?.Model ?? "";
            conversation.Unseeing = CantSee(model);
            // her call, asked for on its own while the remark is written: one reply asked for both came back as the call alone
            var calling = call is null ? null : conversation.Call(client, Tools, answering.Token);
            _ = calling?.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);    // one given up on may fail later: seen, not left unobserved
            var text = await reply(client, said, situation,
                                      words =>
                                      {
                                          if (!Here())
                                              return;
                                          if (held is not null)
                                          {
                                              held.Add(new Piece(words));
                                              return;
                                          }
                                          shown ??= clock.ElapsedMilliseconds;
                                          _wordsOut = true;
                                          view.Continue(words);
                                          speech?.Text(words);
                                      },
                                      (name, payload) =>
                                      {
                                          if (!Here())
                                              return;
                                          if (held is not null)
                                          {
                                              held.Add(new Piece(null, name, payload));
                                              return;
                                          }
                                          Act(view, name);
                                          speech?.Direction(name, payload);
                                      },
                                      answering.Token, Tools, handed);
            GD.Print(shown is { } ms
                ? $"Chat: her bubble showed its first words {ms} ms after asking, the whole reply after {clock.ElapsedMilliseconds} ms"
                : text.Length == 0
                    ? $"Chat: no words came back ({clock.ElapsedMilliseconds} ms after asking)"
                    : $"Chat: her whole reply came {clock.ElapsedMilliseconds} ms after asking, {(texting ? "written as a text" : "held to be played after")}");
            // the player's own message answered with nothing, even asked twice: said, as a failed request is, not left as
            // silence with her thinking dots gone (2026-10-11, a provider's empty answers)
            if (text.Length == 0 && !_usedTool && !fromCabinet && held is null && Here())
            {
                view.Play(Moment.Worry);
                view.Continue(Trouble.Empty);
            }
            if (handed is { Count: > 0 })
                GD.Print($"Chat: handed {string.Join(", ", handed.Select(h => $"{h.Name} ({h.MediaType}, {h.Data.Length / 1024} KB)"))}");
            if (conversation.Unseeing && !CantSee(model))
            {
                GD.Print($"Chat: {model} can't take pictures or PDFs; none is sent to it from now on");
                FoundCantSee?.Invoke(model);
            }
            if (conversation.CutShort)
                GD.Print("Chat: her reply reached the provider's length limit and was cut short");
            if (Tools.Refused && !refused)
                GD.Print("Chat: the provider turned tools down; she answers without them until the provider changes");
            if (held is not null && Here() && text.Length > 0 && !texting)
            {
                void Play(List<Piece> pieces)
                {
                    foreach (var (words, direction, payload) in pieces)
                    {
                        if (words is not null)
                        {
                            view.Continue(words);
                            speech?.Text(words);
                        }
                        else
                        {
                            Act(view, direction!);
                            speech?.Direction(direction!, payload);
                        }
                    }
                }

                if (call is null)
                    speech = _speech = voiceLater!();       // she has something to say about it: said now, as a reply is
                var first = calling is null ? null : await CallFor(conversation, calling, answering.Token);
                if (calling is not null && first is null)
                    GD.Print("Word first: no call of hers to make yet; she goes straight on");
                view.BeginReply();
                began = true;
                if (first is not null)
                {
                    var gap = Gap(WordFirst.Shown(first), WordFirst.Shown(held));
                    conversation.CalledFirst(WordFirst.Shown(first) + gap);    // her line starts with her call
                    Play(first);
                    answering.CancelAfter(Timeout.InfiniteTimeSpan);       // her call waits on the player, not on the provider
                    if (!await call!(WordFirst.Shown(held), answering.Token))
                        speech = null;                      // read on the ticket, or long unanswered: the rest is only shown
                    if (gap.Length > 0)
                        held.Insert(0, new Piece(gap));
                }
                Play(held);
            }
            if (Here())
            {
                speech?.Finish();
                if (speech is { Silent: true } && text.Length > 0)
                    GD.Print($"Voice: nothing to say aloud: her reply had no [say: …] and isn't in her voice's language ({speech.Language})");
                else if (speech is { Unheard: > 0 } partly)
                    GD.Print($"Voice: about {partly.Unheard} of her reply's sentences went unheard: the model gave them no [say: …] of their own");
                Replied = text.Length > 0 ? text : null;
            }
        }
        catch (OperationCanceledException) when (_cutting)
        {
            // the player cut her off: Interrupt takes it from here
        }
        catch (OperationCanceledException) when (texting && within is { } allowed && clock.Elapsed >= allowed)
        {
            GD.Print($"Chat: her words for a text took longer than {allowed.TotalSeconds:0} s; given up");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: {e.GetType().Name}: {e.Message}");
            if (Here() && fallback is not null)
            {
                if (fallback.Length > 0)
                {
                    if (!began)
                    {
                        view.BeginReply();                  // held for her call: nothing was shown yet
                        began = true;
                    }
                    view.Continue(fallback);                // a reminder still gets said, in the plain words; a check-in just lapses
                }
            }
            else if (Here() && !texting)                    // a text that failed shows nothing: the plain line goes instead
            {
                view.Play(Moment.Worry);
                view.Continue(Trouble.Explain(e));
            }
        }
        finally
        {
            _answering = null;
            _view = null;
            _cutting = false;
            InFront = [];
            _written.TrySetResult();
            if (began && GodotObject.IsInstanceValid(view))
                view.EndReply();
        }

        await Index();                          // a note she just took becomes searchable by meaning
        if (_folding || !conversation.NeedsFolding || _client is not { } plain)
            return;
        _folding = true;
        try
        {
            await conversation.Fold(plain);         // the window is full: fold now (normally upkeep folds it while quiet)
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: folding the conversation failed, will try again later: {e.Message}");
        }
        finally
        {
            _folding = false;
        }
    }

    private static readonly TimeSpan CallGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Her call for a word first: the one just asked for, when it has come by a few seconds after her remark; else her
    /// last one (see <see cref="Conversation.LastCall"/>); else none, and she goes straight on.
    /// </summary>
    private static async Task<List<Piece>?> CallFor(Conversation conversation, Task<List<Piece>?> calling, CancellationToken cancel)
    {
        try
        {
            var came = await Task.WhenAny(calling, Task.Delay(CallGrace, cancel)) == calling;
            cancel.ThrowIfCancellationRequested();
            if (came && await calling is { } call)
                return call;
            GD.Print(came ? "Word first: her call came back empty; her last one instead" : "Word first: her call is slow; her last one instead");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            GD.Print($"Word first: asking for her call failed ({e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}); her last one instead");
        }
        return conversation.LastCall;
    }

    /// <summary>A space between her call and the rest, unless one is there or either side is Chinese or Japanese (which use none).</summary>
    private static string Gap(string call, string rest) =>
        call.Length == 0 || rest.Length == 0 || char.IsWhiteSpace(call[^1]) || char.IsWhiteSpace(rest[0]) || call[^1] >= '\u2E80' || rest[0] >= '\u2E80' ? "" : " ";

    /// <summary>
    /// The player cut her off (clicked her, talked over her, asked something new): her voice stops, a reply still
    /// being written is stopped, and her line keeps only what they heard of it, or, without her voice, saw (see
    /// <see cref="Conversation.CutOff"/>). Nothing is cut when she had finished.
    /// </summary>
    public async Task Interrupt(Voice voice, CompanionView? view)
    {
        var conversation = _conversation;
        var here = view is not null && GodotObject.IsInstanceValid(view);
        int? at = _speech is { } speech && voice.Saying(speech) ? speech.CutAt(voice.Heard.Lines, voice.Heard.Into)
                : here ? view!.Seen : null;
        if (here && (_answering is not null || voice.Speaking || at is not null))
            view!.Play(Moment.CutOff);
        voice.Stop();
        if (_answering is { } answering)
        {
            _cutting = true;
            answering.Cancel();
            if (_written is { } written)
                await written.Task;
        }
        _speech = null;
        if (at is not { } cut || conversation is null || conversation != _conversation || conversation.CutOff(cut) is not { } unsaid)
            return;
        GD.Print($"Chat: cut off {cut} characters into her reply, {unsaid.Length} unsaid");
        if (view is not null && GodotObject.IsInstanceValid(view))
            view.Cut(cut);
    }

    /// <summary>
    /// Her sleep time, called every minute: once the conversation has been quiet for a while (sooner while a game
    /// runs, since she has nothing else to do) the part she has not digested yet becomes an episode and facts. A
    /// failure (offline, say) waits ten minutes before the next try.
    /// </summary>
    public async Task Upkeep(bool gameRunning, bool now = false)
    {
        if (_client is null || _conversation is not { } conversation || _answering is not null || _upkeeping || _folding
            || (!now && DateTimeOffset.Now < _upkeepAfter))
            return;
        var digest = now || conversation.NeedsDigest(gameRunning ? TimeSpan.FromMinutes(2) : Conversation.Quiet);
        _upkeeping = true;
        try
        {
            // something new to note; else her notes to condense; else, when quiet and due, a review of her facts;
            // and vectors for whatever has none yet
            if (digest)
                await conversation.Digest(_client);
            else if (!await conversation.Condense(_client) && conversation.IsQuiet(Conversation.Quiet))
                await conversation.Review(_client);
            await conversation.Index();
            // the window three quarters full and the talk quiet: fold it now, when a 5-minute cache has gone cold anyway
            if (conversation.IsQuiet(Conversation.Quiet) && conversation.NeedsQuietFold)
            {
                _folding = true;
                try
                {
                    await conversation.Fold(_client, quiet: true);
                }
                finally
                {
                    _folding = false;
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: upkeep failed, will try again later: {e.Message}");
            _upkeepAfter = DateTimeOffset.Now.AddMinutes(10);
        }
        finally
        {
            _upkeeping = false;
        }
    }

    /// <summary>Logs what happened at the cabinet, for her to know about later.</summary>
    public void Witness(IEnumerable<LedgerEvent> events, Rules rules)
    {
        if (!Ready)
            return;
        foreach (var e in events)
            if (Situation.Witness(e, rules) is { } what)
                _conversation!.Witness(what);
    }

    private DateOnly _recorded;

    /// <summary>Puts the cabinet's record of finished days in her diary (see <see cref="Conversation.Record"/>) when a day has turned.</summary>
    public void Record(LedgerState state)
    {
        if (!Ready || state.Days.Count == 0 || state.Days[^1].Day == _recorded)
            return;
        _conversation!.Record(state);
        _recorded = state.Days[^1].Day;
    }

    /// <summary>Logs a canned line she said on her own.</summary>
    public void Said(string line)
    {
        if (Ready)
            _conversation!.Said(line);
    }

    public void Dispose()
    {
        _answering?.Cancel();
        _client?.Dispose();
        _embedder?.Dispose();
        Store.Dispose();
    }

    /// <summary>
    /// Logs how a reply streamed in: when the first piece came, when the first words did (a model that thinks first
    /// sends nothing to show until it has thought), when it ended, and the tokens it took when the provider says.
    /// </summary>
    private sealed class Timed(IChatClient inner, Action<UsageDetails> used) : DelegatingChatClient(inner)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var clock = Stopwatch.StartNew();
            long? firstPiece = null, firstWords = null;
            var pieces = 0;
            UsageDetails? usage = null;
            try
            {
                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    firstPiece ??= clock.ElapsedMilliseconds;
                    if (update.Text.Length > 0)
                    {
                        firstWords ??= clock.ElapsedMilliseconds;
                        pieces++;
                    }
                    usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                    yield return update;
                }
            }
            finally
            {
                GD.Print($"Chat: reply streamed: first piece after {firstPiece} ms, first words after {firstWords} ms, done after {clock.ElapsedMilliseconds} ms, "
                         + $"{pieces} pieces of words" + (usage is null ? "" : $"; {usage.OutputTokenCount} tokens out ({usage.ReasoningTokenCount ?? 0} of them thinking), "
                         + $"{usage.InputTokenCount} in ({usage.CachedInputTokenCount ?? 0} of them from the provider's cache)"));
                if (usage is not null)
                    used(usage);
            }
        }
    }

    /// <summary>Plays a stage direction from her reply; ones her body has no move for are ignored.</summary>
    private static void Act(CompanionView view, string direction)
    {
        Moment? moment = direction switch
        {
            "cheer" => Moment.Cheer,
            "pump" => Moment.Pump,
            "wave" => Moment.Greet,
            "look" => Moment.Glance,
            "gasp" => Moment.Alarm,
            "worry" => Moment.Worry,
            "note" => Moment.Note,
            _ => null,
        };
        if (moment is { } m)
            view.Play(m);
    }
}
