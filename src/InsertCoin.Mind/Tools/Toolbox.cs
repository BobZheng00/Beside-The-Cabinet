using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>How freely she may use a tool, as the player set it (Settings → Abilities), after Claude Code's permission rules.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Permission>))]
public enum Permission
{
    /// <summary>She uses it whenever it helps.</summary>
    Allowed,
    /// <summary>She asks first, every time, and the player answers on a card by the stool.</summary>
    Ask,
    /// <summary>It isn't offered to her at all.</summary>
    Off,
}

/// <summary>The player's answer when she asks to use a tool.</summary>
public enum Answer
{
    Once,
    /// <summary>Yes, and from now on without asking (for that site, say: see <see cref="Asking.Scope"/>).</summary>
    Always,
    No,
    /// <summary>No answer: they were away, or cut her off.</summary>
    None,
}

/// <summary>What the card says when she asks to use a tool.</summary>
/// <param name="What">The question, in a few words ("Open this page in your browser?").</param>
/// <param name="Detail">On what: the page's address, say. Always the real one, whatever she said about it.</param>
/// <param name="Scope">What "always" covers (a website), or null for every use of the tool.</param>
public sealed record Asking(Tool Tool, string What, string Detail, string? Scope);

/// <summary>
/// Something she can do: a function the model can call, built into the app or (later) from a connector the player
/// connected (docs/specs/2026-10-05-tools-and-abilities.md).
/// </summary>
/// <param name="Title">What the player sees it called (Settings → Abilities).</param>
/// <param name="OutsideApp">It acts outside the app (opens a page, plays music): she asks first unless the player allowed it.</param>
/// <param name="Ask">
/// What the card says for a call's arguments; without it, the title and the arguments as given. Null for a call that
/// would do nothing outside the app (no app by that name): nothing to ask about.
/// </param>
/// <param name="Source">Where it comes from: <see cref="BuiltIn"/>, or a connector's name.</param>
public sealed record Tool(AIFunction Function, string Title, bool OutsideApp, Func<AIFunctionArguments, (string What, string Detail, string? Scope)?>? Ask = null,
                          string Source = Tool.BuiltIn)
{
    public const string BuiltIn = "built-in";

    public string Name => Function.Name;

    /// <summary>Its permission until the player chooses: acting outside the app asks first; reading, and acting inside it, don't.</summary>
    public Permission Standard => OutsideApp ? Permission.Ask : Permission.Allowed;

    /// <summary>
    /// It looks at what is the player's own (their screen, their window titles): it is set on Settings → Privacy, and Auto
    /// never skips asking for it.
    /// </summary>
    public bool Private { get; init; }

    /// <summary>
    /// It brings in text from the web (a page, search results) or from the player's files (a note), which may try to
    /// talk her into things: once it has run in a reply, Auto stops going ahead for the rest of that reply, and what acts
    /// outside the app asks again.
    /// </summary>
    public bool FromWeb { get; init; }

    /// <summary>It needs the player at the PC (their screen, their windows, opening something on it): not while they text her from away.</summary>
    public bool AtThePc { get; init; }

    /// <summary>
    /// Whether the player's words her reply answers (null when the cabinet spoke) ask for this call: one they don't ask
    /// for isn't made, and she is told so (a focus session started only when asked for, #145). Null: every call is made.
    /// </summary>
    public Func<AIFunctionArguments, string?, bool>? AskedFor { get; init; }

    /// <summary>
    /// It tells her what she would simply know (the cabinet's numbers), at once: no line of hers before it, which is for
    /// a wait ("I'll check the cabinet's earning rules" read stiff, 2026-10-12).
    /// </summary>
    public bool Known { get; init; }

    /// <summary>
    /// Run by the provider itself (its own web search): offered in place of <see cref="Function"/>, which then only names
    /// and describes it. The provider runs it in the middle of a reply, so it is never asked about.
    /// </summary>
    public AITool? Hosted { get; init; }
}

/// <summary>
/// The tools she has and the player's permission for each. The list offered to the model is sorted by name and
/// changes only when a tool comes, goes or is switched off: it sits at the start of every request, the part
/// providers cache, so allowing or asking never changes it.
/// </summary>
public sealed class Toolbox
{
    /// <summary>
    /// Tool calls she may make in one reply, each a request of its own; the last request gets no tools, so she answers.
    /// She says what she is doing as she goes, so a longer run of steps is fine; this only stops one that never ends.
    /// </summary>
    public const int Rounds = 10;
    private const int RecordLength = 300;
    // the arguments as the model gave them, with Chinese and Japanese as they are (not \u escapes)
    private static readonly JsonSerializerOptions Readable = new(AIJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

    private readonly SortedDictionary<string, Tool> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Permission> _chosen = [];
    private IList<AITool>? _offered;
    private bool _refused, _hostedRefused, _readTheWeb;

    public Toolbox(IEnumerable<Tool> tools, IReadOnlyDictionary<string, Permission>? chosen = null)
    {
        foreach (var tool in tools)
            _tools[tool.Name] = tool;
        if (chosen is not null)
            Choose(chosen);
    }

    public IReadOnlyCollection<Tool> All => _tools.Values;

    /// <summary>The player's choices, by tool name, or "name:scope" for one allowed always for a scope (a site).</summary>
    public IReadOnlyDictionary<string, Permission> Chosen => _chosen;

    /// <summary>Asks the player whether she may (the card by the stool). Unset, nothing that asks is ever done.</summary>
    public Func<Asking, CancellationToken, Task<Answer>> AskPlayer { get; set; } = (_, _) => Task.FromResult(Answer.None);

    /// <summary>
    /// The model has begun a call to a tool, its arguments still coming (see <see cref="Provider.ToolStarting"/>): a
    /// memo is being written, say, so she can be seen writing it.
    /// </summary>
    public event Action<Tool>? Starting;

    /// <summary>A tool is about to run: her body shows her busy until the rest of her reply comes.</summary>
    public event Action<Tool>? Using;

    /// <summary>The player answered "always": the choices have changed, to keep.</summary>
    public event Action? Changed;

    /// <summary>
    /// Auto: she goes ahead without asking where she would ask, as Claude Code's auto mode works on without stopping.
    /// She still says what she is doing, every use is still in the conversation, and tools switched off stay off.
    /// </summary>
    public bool Auto { get; set; }

    /// <summary>
    /// She is texting the player, who is away from the PC: what needs them at it (see <see cref="Tool.AtThePc"/>) is
    /// turned down when called. It stays offered, so that the list the provider caches never changes.
    /// </summary>
    public bool Away { get; set; }

    /// <summary>
    /// The provider turned tools down (a local model without them): none are offered, and she answers as before, with
    /// stage directions only, until the provider changes.
    /// </summary>
    public bool Refused
    {
        get => _refused;
        set
        {
            _refused = value;
            _offered = null;
        }
    }

    /// <summary>
    /// The provider turned its own tools down (web search not enabled for the account, say): the others are still
    /// offered, without those, until the provider changes.
    /// </summary>
    public bool HostedRefused
    {
        get => _hostedRefused;
        set
        {
            _hostedRefused = value;
            _offered = null;
        }
    }

    /// <summary>Whether a tool the provider runs is offered now.</summary>
    public bool Hosting => !_refused && !_hostedRefused && _tools.Values.Any(t => t.Hosted is not null && PermissionOf(t) != Permission.Off);

    /// <summary>Adds a tool, or replaces the one with its name (the web search, when the player changes how she searches).</summary>
    public void Put(Tool tool)
    {
        _tools[tool.Name] = tool;
        _offered = null;
    }

    /// <summary>Takes a tool away (no web search at all, say).</summary>
    public void Take(string name)
    {
        if (_tools.Remove(name))
            _offered = null;
    }

    /// <summary>The player's words the reply under way answers; null when the cabinet spoke (see <see cref="Tool.AskedFor"/>).</summary>
    public string? Said { get; private set; }

    /// <summary>
    /// A new reply begins, to <paramref name="said"/> (null when the cabinet spoke): nothing from the web has been read in
    /// it yet (see <see cref="Tool.FromWeb"/>).
    /// </summary>
    public void Fresh(string? said = null) => (_readTheWeb, Said) = (false, said);

    /// <summary>The model has begun calling <paramref name="name"/> (one of hers): <see cref="Starting"/> is raised.</summary>
    public void Begun(string name)
    {
        if (_tools.TryGetValue(name, out var tool))
            Starting?.Invoke(tool);
    }

    /// <summary>
    /// The provider is running one of its own tools (<paramref name="name"/>, its web search) in the middle of the reply:
    /// <see cref="Using"/> is raised, so her body shows her busy, and what it brings in counts as read from the web.
    /// </summary>
    public void Hosted(string name)
    {
        if (!_tools.TryGetValue(name, out var tool) || tool.Hosted is null)
            return;
        _readTheWeb |= tool.FromWeb;
        Using?.Invoke(tool);
    }

    /// <summary>The player's choices replace the ones held (from settings, or Settings → Abilities).</summary>
    public void Choose(IReadOnlyDictionary<string, Permission> chosen)
    {
        _chosen.Clear();
        foreach (var (key, permission) in chosen)
            _chosen[key] = permission;
        _offered = null;
    }

    /// <summary>How freely she may use <paramref name="tool"/>, for <paramref name="scope"/> if it has one.</summary>
    public Permission PermissionOf(Tool tool, string? scope = null) =>
        _chosen.TryGetValue(tool.Name, out var whole) && whole == Permission.Off ? Permission.Off
        : scope is not null && _chosen.TryGetValue($"{tool.Name}:{scope}", out var just) ? just
        : _chosen.TryGetValue(tool.Name, out whole) ? whole
        : tool.Standard;

    /// <summary>
    /// The tools that ask the player on a card before acting, as things are set now (Ask, and under Auto only what is the
    /// player's own): she asks in her own words as she uses one of these, and only these. Told to ask for whatever acts
    /// outside the app, she asked "Shall I turn your music on?" with music set to Always (2026-10-11).
    /// </summary>
    public IReadOnlyList<string> AsksFirst =>
        [.. _tools.Values.Where(t => PermissionOf(t) == Permission.Ask && (!Auto || t.Private)).Select(t => t.Name).Order(StringComparer.Ordinal)];

    /// <summary>The tools offered that tell her what she would simply know (see <see cref="Tool.Known"/>): no line before them.</summary>
    public IReadOnlyList<string> Known =>
        [.. _tools.Values.Where(t => t.Known && PermissionOf(t) != Permission.Off).Select(t => t.Name).Order(StringComparer.Ordinal)];

    /// <summary>The tools offered to the model, the same list every request (null when none are).</summary>
    public IList<AITool>? Offered
    {
        get
        {
            if (_refused)
                return null;
            _offered ??= [.. _tools.Values.Where(t => PermissionOf(t) != Permission.Off && !(t.Hosted is not null && _hostedRefused))
                                          .Select(t => t.Hosted ?? t.Function)];
            return _offered.Count > 0 ? _offered : null;
        }
    }

    /// <summary>
    /// Runs a call the model made, asking the player first where they want that.
    /// </summary>
    /// <returns>What the model is told, and a line for the conversation saying what happened.</returns>
    public async Task<(object? Result, string Record)> Use(AIFunction function, AIFunctionArguments arguments, CancellationToken cancel = default)
    {
        var given = Describe(arguments);
        if (!_tools.TryGetValue(function.Name, out var tool))
            return ($"There is no tool called {function.Name}.", $"You tried to use {function.Name}, which you don't have.");
        if (Away && tool.AtThePc)
            return ("The player is away from the PC, texting you: this needs them at it, so it wasn't done.",
                    $"You tried to use {tool.Name} ({given}) while the player was texting you from away; it needs them at the PC.");
        if (tool.AskedFor is { } asked && !asked(arguments, Said))
            return ("The player hasn't asked for this, so it wasn't done. Offer it if it would help; it's done once they say so.",
                    $"You tried to use {tool.Name} ({given}) without the player asking for it; it wasn't done.");
        var asking = tool.Ask is null ? (tool.Title, given, null) : tool.Ask(arguments);
        var (what, detail, scope) = asking ?? ("", "", null);
        var permission = PermissionOf(tool, scope);
        switch (asking is null && permission == Permission.Ask ? Permission.Allowed : permission)
        {
            case Permission.Off:
                return ("The player has switched this off.", $"You tried to use {tool.Name} ({given}), which the player has switched off.");
            // Auto goes ahead, except for what is the player's own, and once something from the web is in front of her
            case Permission.Ask when !Auto || tool.Private || _readTheWeb:
                var answer = await AskPlayer(new Asking(tool, what, detail, scope), cancel);
                cancel.ThrowIfCancellationRequested();
                if (answer == Answer.No)
                    return ("The player said no, so it wasn't done. Take that lightly, and don't ask again for the same thing now.",
                            $"You asked to use {tool.Name} ({given}); the player said no.");
                if (answer == Answer.None)
                    return ("The player didn't answer, so it wasn't done.", $"You asked to use {tool.Name} ({given}); the player didn't answer, so it wasn't done.");
                if (answer == Answer.Always)
                {
                    _chosen[scope is null ? tool.Name : $"{tool.Name}:{scope}"] = Permission.Allowed;
                    Changed?.Invoke();
                }
                break;
        }
        Using?.Invoke(tool);
        _readTheWeb |= tool.FromWeb;
        object? result;
        try
        {
            result = await function.InvokeAsync(arguments, cancel);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = $"It didn't work: {e.Message}";
        }
        var said = result is JsonElement { ValueKind: JsonValueKind.String } text ? text.GetString() : result?.ToString();
        return (result, Short($"You used {tool.Name} ({given}): {said}"));
    }

    /// <summary>
    /// Whether a failed request looks like the provider turning tools down: a model or server without them says so in
    /// its error ("does not support tools", "No endpoints found that support tool use", "does not support Function Calling").
    /// </summary>
    public static bool Refuses(Exception e) =>
        e is not OperationCanceledException && e is not TimeoutException && e is not PlanStopped
        && (e.Message.Contains("tool", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("function", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a failed request looks like the provider turning its own web search down: Anthropic answers "web search
    /// is not enabled" when the account's administrator switched it off.
    /// </summary>
    public static bool RefusesHosted(Exception e) =>
        e is not OperationCanceledException && e is not TimeoutException
        && (e.Message.Contains("web search", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("web_search", StringComparison.OrdinalIgnoreCase));

    private static string Describe(AIFunctionArguments arguments) =>
        arguments.Count == 0 ? "" : Short(JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value), Readable), 160);

    private static string Short(string text, int length = RecordLength) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
