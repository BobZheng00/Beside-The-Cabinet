using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>The tools built into the app: always there, nothing to set up (docs/specs/2026-10-05-tools-and-abilities.md).</summary>
public static class BuiltIn
{
    /// <summary>Opens a web page in the player's browser. It acts outside the app, so she asks first.</summary>
    /// <param name="open">Opens the page (the app's own way of doing it).</param>
    public static Tool OpenLink(Action<Uri> open) => new(
        AIFunctionFactory.Create(
            ([Description("The page's full address, starting with https://")] string url) =>
            {
                var page = Page(url) ?? throw new ArgumentException("only web pages (addresses starting with https:// or http://) can be opened");
                open(page);
                return "Opened in the player's browser.";
            },
            "open_link",
            "Opens a web page in the player's browser: a LeetCode problem, a playlist, something they asked for. The player is asked first."),
        "Open a web page",
        OutsideApp: true,
        arguments => Page(Text(arguments, "url")) is { } page
            ? ("Open this page in your browser?", Shown(page), Site(page))
            : ("Open this?", Text(arguments, "url") ?? "", null));

    /// <summary>
    /// Prints a memo from the cabinet (see <see cref="Memo"/>): long text for the player to read at their own pace and
    /// keep. It acts inside the app, so she needs no permission.
    /// </summary>
    /// <param name="print">Prints it (the app keeps it and shows it coming out of the slot); returns what she is told.</param>
    public static Tool PrintMemo(Func<string, string, string> print) => new(
        AIFunctionFactory.Create(
            ([Description("A short title, a few words, in the player's language")] string title,
             [Description("The memo itself, in the player's language. Plain text; lists start lines with \"- \"; code goes in ``` blocks. A page at most.")] string text) =>
                print(title, text),
            "print_memo",
            "Prints a memo from the cabinet: paper the player reads at their own pace and can pin on the cabinet's side. Use it for whatever is too long " +
            "to say in your bubble: a hint written out, steps, a list, a plan, what you found, code, or when they ask for a memo. Say only the gist aloud."),
        "Print a memo",
        OutsideApp: false);

    /// <summary>A web address she may open: http or https only, never a file, a program or a script.</summary>
    public static Uri? Page(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var page) && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp) && page.Host.Length > 0
            ? page : null;

    /// <summary>The site "always" covers: its host as the browser sees it, without "www.".</summary>
    public static string Site(Uri page) => page.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.IdnHost[4..] : page.IdnHost;

    /// <summary>An address as the card shows it: no "https://", the host as the browser will see it (so a look-alike shows as one).</summary>
    public static string Shown(Uri page) => page.IdnHost + (page.IsDefaultPort ? "" : $":{page.Port}") + (page.PathAndQuery == "/" ? "" : page.PathAndQuery);

    private static string? Text(AIFunctionArguments arguments, string name) => arguments.TryGetValue(name, out var value) ? value?.ToString() : null;
}
