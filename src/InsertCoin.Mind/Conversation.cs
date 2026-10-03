using System.Text;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// One character's side of the never-ending conversation with the player: answers what the player says (streamed,
/// with stage directions split off), logs what the cabinet does and what she says unprompted, and folds the
/// older part of the thread into a summary once it grows past a budget (auto-compaction).
/// </summary>
public sealed class Conversation(MindStore store, string character, Persona persona, TimeProvider? clock = null)
{
    /// <summary>Verbatim conversation (tokens) that triggers folding, and how much of the newest is kept verbatim.</summary>
    public const int FoldAbove = 4000, KeepVerbatim = 1500;
    private const int KeepLines = 6, SummaryLimit = 2400;
    private static readonly ChatOptions ReplyOptions = new() { MaxOutputTokens = 600 };     // room for models that think first
    private static readonly ChatOptions FoldOptions = new() { MaxOutputTokens = 1500 };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string Character => character;
    public Persona Persona => persona;

    /// <summary>
    /// Streams her answer to <paramref name="said"/>: shown text through <paramref name="text"/>, stage directions
    /// through <paramref name="direction"/>. Both lines are logged; a failed request leaves only the player's.
    /// </summary>
    /// <returns>Everything she said.</returns>
    public async Task<string> Answer(IChatClient client, string said, string situation, Action<string> text, Action<string, string?> direction,
                                     CancellationToken cancel = default)
    {
        var summary = store.SummaryOf(character);
        var messages = Prompt.Build(persona, store.Note(MindStore.Shared, MindStore.AboutUser), store.Note(character, MindStore.Us),
                                    summary, store.After(character, summary?.Through ?? 0), situation, said);
        store.Add(character, Speaker.User, said.Trim(), _clock.GetLocalNow());

        var stage = new StageDirections(direction);
        var reply = new StringBuilder();
        void Show(string piece)
        {
            if (piece.Length == 0)
                return;
            reply.Append(piece);
            text(piece);
        }
        await foreach (var update in client.GetStreamingResponseAsync(messages, ReplyOptions, cancel))
            Show(stage.Feed(update.Text));
        Show(stage.Finish());

        var answer = reply.ToString().Trim();
        if (answer.Length > 0)
            store.Add(character, Speaker.Her, answer, _clock.GetLocalNow());
        return answer;
    }

    /// <summary>Something the cabinet did, for her to know about later.</summary>
    public void Witness(string what) => store.Add(character, Speaker.Cabinet, what, _clock.GetLocalNow());

    /// <summary>Something she said on her own (a canned line about an event).</summary>
    public void Said(string line) => store.Add(character, Speaker.Her, line, _clock.GetLocalNow());

    public bool NeedsFolding => Prompt.Tokens(Unfolded()) > FoldAbove;

    /// <summary>
    /// Folds all but the newest part of the verbatim conversation into the summary. Returns false when there was
    /// nothing to fold or the model gave nothing back (the lines stay, and the next attempt includes them).
    /// </summary>
    public async Task<bool> Fold(IChatClient client, CancellationToken cancel = default)
    {
        var summary = store.SummaryOf(character);
        var lines = store.After(character, summary?.Through ?? 0);
        if (Prompt.Tokens(lines) <= FoldAbove)
            return false;

        var keep = 0;
        var kept = 0;
        while (keep < lines.Count && (keep < KeepLines || kept + Prompt.Tokens(lines[^(keep + 1)].Text) + 4 <= KeepVerbatim))
            kept += Prompt.Tokens(lines[^++keep].Text) + 4;
        var fold = lines.Take(lines.Count - keep).ToList();
        if (fold.Count == 0)
            return false;

        var response = await client.GetResponseAsync(Prompt.Fold(persona, summary, fold), FoldOptions, cancel);
        var folded = response.Text.Trim();
        if (folded.Length == 0)
            return false;
        store.Fold(character, folded.Length > SummaryLimit ? folded[..SummaryLimit] : folded, fold[^1].Id, _clock.GetLocalNow());
        return true;
    }

    private IReadOnlyList<Line> Unfolded() => store.After(character, store.SummaryOf(character)?.Through ?? 0);
}
