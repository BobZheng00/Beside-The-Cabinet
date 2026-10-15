namespace InsertCoin.Mind;

/// <summary>What became of one edit the player made in the memory book.</summary>
public enum Kept
{
    /// <summary>Nothing to write: her memory already says that.</summary>
    Same,

    /// <summary>Written.</summary>
    Saved,

    /// <summary>She changed the same thing since the book showed it: nothing written, and the player chooses which to keep.</summary>
    Clash,

    /// <summary>It has left her memory since (merged, retired, forgotten, done with): nothing written, and the player chooses.</summary>
    Gone,
}

/// <summary>
/// The memory book's edits, saved one field at a time as the player leaves it. Each writes only that field, onto the
/// item as it is now, so her other changes to it stay; and only if what is stored is still what the book showed when
/// the player began, else she changed it meanwhile and the player chooses (<see cref="Kept.Clash"/>). Nothing the
/// player left alone is written back. An item is matched by when it was learned or added as well as its id: a
/// forgotten item's id can be given to the next one.
/// </summary>
public static class MemoryEdits
{
    /// <summary>The fact the book showed, as stored now; null once it has left her mind (retired, merged, forgotten).</summary>
    public static Fact? Current(MindStore store, string character, Fact shown) =>
        store.Facts(character).FirstOrDefault(f => f.Id == shown.Id && f.Learned == shown.Learned);

    /// <summary>The agenda item the book showed, as stored now; null once it has left the agenda.</summary>
    public static Plan? Current(MindStore store, Plan shown) =>
        store.Agenda().FirstOrDefault(p => p.Id == shown.Id && p.Added == shown.Added);

    /// <summary>One of the two notes (About you, You and her).</summary>
    public static Kept Note(MindStore store, string scope, string name, string shown, string edited)
    {
        var kept = Decide(shown.Trim(), edited.Trim(), store.Note(scope, name).Trim());
        if (kept == Kept.Saved)
            store.SetNote(scope, name, edited.Trim());
        return kept;
    }

    /// <summary>A fact's words; emptied, it is forgotten.</summary>
    public static Kept Fact(MindStore store, string character, Fact shown, string edited)
    {
        if (Current(store, character, shown) is not { } now)
            return Kept.Gone;
        edited = edited.Trim();
        var kept = Decide(shown.Text, edited, now.Text);
        if (kept == Kept.Saved && edited.Length == 0)
            store.DeleteFact(now.Id);
        else if (kept == Kept.Saved)
            store.UpdateFact(now.Id, edited);
        return kept;
    }

    /// <summary>
    /// The player keeps their words for a fact whatever she did with it meanwhile: written over hers, brought back if
    /// her review retired it, or written down again if it was merged or forgotten. With <paramref name="keepHers"/>
    /// (a change of hers the player never answered), her words written over are kept as what the fact used to say,
    /// retired as changed, as upkeep keeps a fact's history. Emptied, it is forgotten. Returns the fact as it now is, or
    /// null when forgotten.
    /// </summary>
    public static Fact? KeepFact(MindStore store, string character, Fact shown, string edited, DateTimeOffset now, bool keepHers = false)
    {
        edited = edited.Trim();
        if (Current(store, character, shown) is { } hers)
        {
            if (keepHers && hers.Text != edited)
                store.Retire(store.AddFact(hers.Scope, hers.Text, hers.Importance, hers.Learned),
                             edited.Length == 0 ? Conversation.Ended : $"{Conversation.Changed} {edited}", now);
            if (edited.Length == 0)
            {
                store.DeleteFact(hers.Id);
                return null;
            }
            store.UpdateFact(hers.Id, edited);
            return Current(store, character, shown)!;
        }
        if (edited.Length == 0)
            return null;                                    // forgotten, and already out of her mind
        if (store.Retired(character).FirstOrDefault(f => f.Id == shown.Id && f.Learned == shown.Learned) is { } retired)
        {
            store.Restore(retired.Id, now);
            store.UpdateFact(retired.Id, edited);
            return Current(store, character, shown)!;
        }
        var id = store.AddFact(shown.Scope, edited, shown.Importance, now);
        if (shown.Pinned)
            store.PinFact(id, true);
        return store.Facts(character).First(f => f.Id == id);
    }

    /// <summary>What an agenda item says.</summary>
    public static Kept PlanText(MindStore store, Plan shown, string edited) =>
        Plan(store, shown, p => p.Text, edited.Trim(), p => p with { Text = edited.Trim() });

    /// <summary>When an agenda item is.</summary>
    public static Kept PlanWhen(MindStore store, Plan shown, DateTimeOffset at, bool allDay) =>
        Plan(store, shown, p => (p.At, p.AllDay), (at, allDay), p => p with { At = at, AllDay = allDay });

    /// <summary>
    /// A choice made with a click (its reminder, whether it repeats), or the player's part kept after a clash: written
    /// onto the item as it is now.
    /// </summary>
    public static Kept PlanChoice(MindStore store, Plan shown, Func<Plan, Plan> edit) =>
        Current(store, shown) is { } now ? Write(store, now, edit(now)) : Kept.Gone;

    /// <summary>
    /// The player keeps an agenda item as they have it, though it left the agenda meanwhile: written onto it if it is
    /// back, else put on the agenda again, once however often they ask (one just like it already there is the one).
    /// </summary>
    public static Plan KeepPlan(MindStore store, Plan shown, string text, DateTimeOffset at, bool allDay, TimeSpan? before, Repeat repeat, DateTimeOffset now)
    {
        text = text.Trim();
        if (Current(store, shown) is { } there)
        {
            Write(store, there, there with { Text = text, At = at, AllDay = allDay, Before = before, Repeat = repeat });
            return Current(store, shown)!;
        }
        if (store.Agenda().FirstOrDefault(p => p.Source is null && p.Text == text && p.At == at && p.AllDay == allDay) is { } back)
            return back;
        var id = store.AddPlan(text, at, allDay, before, now, shown.By, repeat);
        return store.Agenda().First(p => p.Id == id);
    }

    private static Kept Plan<T>(MindStore store, Plan shown, Func<Plan, T> part, T edited, Func<Plan, Plan> edit)
    {
        if (Current(store, shown) is not { } now)
            return Kept.Gone;
        var kept = Decide(part(shown), edited, part(now));
        return kept == Kept.Saved ? Write(store, now, edit(now)) : kept;
    }

    private static Kept Write(MindStore store, Plan now, Plan changed)
    {
        if (changed == now)
            return Kept.Same;
        store.UpdatePlan(changed.Id, changed.Text, changed.At, changed.AllDay, changed.Before, changed.Repeat);
        return Kept.Saved;
    }

    /// <summary>The player's value against what the book showed and what is stored now.</summary>
    private static Kept Decide<T>(T shown, T edited, T now) =>
        EqualityComparer<T>.Default.Equals(edited, now) ? Kept.Same
        : EqualityComparer<T>.Default.Equals(now, shown) ? Kept.Saved
        : Kept.Clash;
}
