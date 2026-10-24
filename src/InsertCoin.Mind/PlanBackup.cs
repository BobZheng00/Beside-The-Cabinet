using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// The backup she carries on with while the player's ChatGPT plan has stopped (spec 2026-10-18-chatgpt-plan.md, step 2):
/// an API key or a local model, chosen on Settings → Mind. Nothing goes to it until the player says yes (see
/// <see cref="Allow"/>), so a paid key is never spent unasked; until then every request fails as the plan's do, and she
/// waits. After the yes everything she does goes to the backup, while the plan is tried again now and then (the plan's
/// own <see cref="ChatGptPlan.TryAgainAfter"/>); as soon as it answers she is back on it (see <see cref="Back"/>), and the
/// next time it stops the player is asked again.
/// </summary>
public sealed class PlanBackup
{
    private readonly ChatGptPlan _plan;

    public PlanBackup(ChatGptPlan plan)
    {
        _plan = plan;
        plan.Changed += () =>
        {
            if (!Allowed || plan.Stopped is not null)
                return;
            Allowed = false;                                // the yes was for this stop only
            Back?.Invoke();
        };
    }

    /// <summary>The player said yes: she carries on with the backup until the plan works again.</summary>
    public bool Allowed { get; private set; }

    /// <summary>The plan answered again after she had carried on with the backup: she is back on it (said once).</summary>
    public event Action? Back;

    /// <summary>The player said yes: she carries on with the backup from now on (see <see cref="Back"/> for the way back).</summary>
    public event Action? Allowing;

    /// <summary>The player's yes to carrying on with the backup; nothing when the plan works again meanwhile.</summary>
    public void Allow()
    {
        if (_plan.Stopped is null || Allowed)
            return;
        Allowed = true;
        Allowing?.Invoke();
    }

    /// <summary>The yes was taken back: the backup it named was changed or removed, so the player is asked again (if the plan still waits).</summary>
    public event Action? Forgot;

    /// <summary>
    /// The backup the player said yes to is no longer the one behind the plan (another saved, or none): a yes is for the
    /// backup it named, so she waits on the plan again until asked anew.
    /// </summary>
    public void Forget()
    {
        if (!Allowed)
            return;
        Allowed = false;
        Forgot?.Invoke();
    }

    /// <summary>
    /// Her mind on the plan, <paramref name="onPlan"/>, with <paramref name="backup"/> behind it: a request the plan
    /// turns down, or doesn't make while it waits, goes to the backup instead, but only after the player's yes.
    /// </summary>
    public IChatClient Over(IChatClient onPlan, IChatClient backup) => new Switch(this, onPlan, backup);

    private sealed class Switch(PlanBackup backup, IChatClient onPlan, IChatClient other) : DelegatingChatClient(onPlan)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (PlanStopped) when (backup.Allowed)
            {
                return await other.GetResponseAsync(messages, options, cancellationToken);
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                             [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // the plan's updates that show nothing (its response.created comes first, before a limit reached part-way) are
            // held until one that does: a request the plan stops before that goes to the backup whole
            var held = new List<ChatResponseUpdate>();
            await using (var plan = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                bool shown = false, stopped = false;
                while (true)
                {
                    try
                    {
                        if (!await plan.MoveNextAsync())
                            break;
                    }
                    catch (PlanStopped) when (!shown && backup.Allowed)
                    {
                        stopped = true;
                        break;
                    }
                    if (!shown && plan.Current.Contents.Count == 0)
                    {
                        held.Add(plan.Current);
                        continue;
                    }
                    shown = true;
                    foreach (var waiting in held)
                        yield return waiting;
                    held.Clear();
                    yield return plan.Current;
                }
                if (!stopped)
                {
                    foreach (var waiting in held)
                        yield return waiting;
                    yield break;
                }
            }
            await foreach (var update in other.GetStreamingResponseAsync(messages, options, cancellationToken))
                yield return update;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                other.Dispose();
            base.Dispose(disposing);
        }
    }
}
