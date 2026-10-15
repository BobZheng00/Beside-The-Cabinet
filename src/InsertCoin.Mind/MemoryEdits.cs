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
/// player left alone is written back.
/// </summary>
public static class MemoryEdits
{
    /// <summary>One of the two notes (About you, You and her).</summary>
    public static Kept Note(MindStore store, string scope, string name, string shown, string edited)
    {
        var kept = Decide(shown.Trim(), edited.Trim(), store.Note(scope, name).Trim());
        if (kept == Kept.Saved)
            store.SetNote(scope, name, edited.Trim());
        return kept;
    }

    /// <summary>A fact's words; emptied, it is forgotten.</summary>
    public static Kept Fact(MindStore store, string character, long id, string shown, string edited)
    {
        if (store.Facts(character).FirstOrDefault(f => f.Id == id) is not { } now)
            return Kept.Gone;
        edited = edited.Trim();
        var kept = Decide(shown, edited, now.Text);
        if (kept == Kept.Saved && edited.Length == 0)
            store.DeleteFact(id);
        else if (kept == Kept.Saved)
            store.UpdateFact(id, edited);
        return kept;
    }

    /// <summary>
    /// The player keeps their words for a fact whatever she did with it meanwhile: written over hers, brought back if
    /// her review retired it, or written down again if it was merged or forgotten. Returns the fact's id, which is new
    /// in that last case.
    /// </summary>
    public static long KeepFact(MindStore store, string character, Fact shown, string edited, DateTimeOffset now)
    {
        var id = shown.Id;
        if (store.Retired(character).Any(f => f.Id == id))
            store.Restore(id, now);
        else if (store.Facts(character).All(f => f.Id != id))
        {
            id = store.AddFact(shown.Scope, edited, shown.Importance, now);
            if (shown.Pinned)
                store.PinFact(id, true);
            return id;
        }
        store.UpdateFact(id, edited);
        return id;
    }

    /// <summary>What an agenda item says.</summary>
    public static Kept PlanText(MindStore store, Plan shown, string edited) =>
        Plan(store, shown, p => p.Text, edited.Trim(), p => p with { Text = edited.Trim() });

    /// <summary>When an agenda item is.</summary>
    public static Kept PlanWhen(MindStore store, Plan shown, DateTimeOffset at, bool allDay) =>
        Plan(store, shown, p => (p.At, p.AllDay), (at, allDay), p => p with { At = at, AllDay = allDay });

    /// <summary>
    /// A choice made with a click (its reminder, whether it repeats): there was no time for her to change it in
    /// between, so it is written onto the item as it is now.
    /// </summary>
    public static Kept PlanChoice(MindStore store, long id, Func<Plan, Plan> edit)
    {
        if (store.Agenda().FirstOrDefault(p => p.Id == id) is not { } now)
            return Kept.Gone;
        return Write(store, now, edit(now));
    }

    private static Kept Plan<T>(MindStore store, Plan shown, Func<Plan, T> part, T edited, Func<Plan, Plan> edit)
    {
        if (store.Agenda().FirstOrDefault(p => p.Id == shown.Id) is not { } now)
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
