using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class MissionTests
{
    private static readonly Earning FreePlay = new(false, false, false);

    /// <summary>A profile set up with missions (a new one, from the first run on).</summary>
    private static Machine New(Rules? rules = null)
    {
        var m = new Machine(rules: rules);
        m.State = m.State with { Missions = new MissionProgress() };
        return m;
    }

    [Theory]
    [InlineData(Missions.ConnectAnAi)]
    [InlineData(Missions.SayHello)]
    [InlineData(Missions.EarnACredit)]
    [InlineData(Missions.AskForAReminder)]
    [InlineData(Missions.OpenMemory)]
    [InlineData(Missions.HearTheirVoice)]
    public void Each_mission_ticks_and_pays_a_quarter_of_an_hour_once(string id)
    {
        var m = New();

        var first = m.Send(new MissionDone(id));
        var again = m.Send(new MissionDone(id));

        Assert.Equal(id, Assert.Single(first.OfType<MissionCleared>()).Mission.Id);
        Assert.Equal(Min(15), Assert.Single(first.OfType<MissionPaid>()).Paid.CreditedMs);
        Assert.Empty(again);
        Assert.Equal(Min(15), m.State.TimeLeftMs(m.Rules));
        Assert.Contains(id, m.State.Missions!.Paid);
    }

    [Fact]
    public void All_six_pay_an_hour_and_a_half()
    {
        var m = New();

        foreach (var mission in Missions.All)
            m.Send(new MissionDone(mission.Id));

        Assert.Equal(Min(90), m.State.TimeLeftMs(m.Rules));
        Assert.Empty(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: false));
    }

    [Fact]
    public void A_reward_is_paid_past_the_days_cap_and_doesnt_count_towards_it()
    {
        var m = New();
        m.Focus(TimeSpan.FromHours(6));                  // the whole 1:30 of a Wednesday, and EARN A CREDIT with it

        var events = m.Send(new MissionDone(Missions.SayHello));

        Assert.Equal(Min(15), Assert.Single(events.OfType<MissionPaid>()).Paid.CreditedMs);
        Assert.Empty(events.OfType<CapReached>());
        Assert.Equal(Min(90), m.State.Today!.EarnedMs);
        Assert.Equal(Min(90 + 15 + 15), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_reward_pays_a_debt_first()
    {
        var m = New();
        m.State = m.State with { DebtMs = Min(10) };

        var events = m.Send(new MissionDone(Missions.OpenMemory));

        Assert.Equal(Min(10), Assert.Single(events.OfType<DebtPaid>()).PaidMs);
        Assert.Equal(Min(5), Assert.Single(events.OfType<MissionPaid>()).Paid.CreditedMs);
        Assert.Equal(0, m.State.DebtMs);
        Assert.Equal(Min(5), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void Earn_a_credit_ticks_with_five_minutes_of_focus_in_a_day()
    {
        var m = New();

        var short_ = m.Focus(TimeSpan.FromMinutes(4));
        var more = m.Focus(TimeSpan.FromMinutes(1));

        Assert.Empty(short_.OfType<MissionCleared>());
        Assert.Equal(Missions.EarnACredit, Assert.Single(more.OfType<MissionCleared>()).Mission.Id);
        Assert.Single(more.OfType<MissionPaid>());
    }

    [Fact]
    public void Earn_a_credit_ticks_with_a_solve_or_an_activity()
    {
        var solved = New();
        var done = New();

        solved.Solve("two-sum");
        done.Send(new ActivityDone("gym", Size.Medium, 1));

        Assert.Contains(Missions.EarnACredit, solved.State.Missions!.Paid);
        Assert.Contains(Missions.EarnACredit, done.State.Missions!.Paid);
    }

    [Fact]
    public void Earn_a_credit_ticks_only_from_a_way_to_earn_that_is_on()
    {
        var m = New(new Rules { Earns = new Earning(Focus: false, Activities: true, LeetCode: false) });

        m.Focus(TimeSpan.FromMinutes(30));
        m.Solve("two-sum");                              // LeetCode's last answer, as it was switched off

        Assert.DoesNotContain(Missions.EarnACredit, m.State.Missions!.Done);
    }

    [Fact]
    public void Under_free_play_a_mission_ticks_and_pays_nothing_and_earn_a_credit_isnt_there()
    {
        var m = New(new Rules { Earns = FreePlay });

        var hello = m.Send(new MissionDone(Missions.SayHello));
        var credit = m.Send(new MissionDone(Missions.EarnACredit));

        Assert.Single(hello.OfType<MissionCleared>());
        Assert.Empty(hello.OfType<MissionPaid>());
        Assert.Empty(credit);
        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
        Assert.Equal(["ai", "hello", "memory", "reminder", "voice"], Missions.Listed(m.Rules.Earns).Select(x => x.Id).Order());
        Assert.DoesNotContain(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: false), x => x.Id == Missions.EarnACredit);
    }

    [Fact]
    public void Switching_to_credits_pays_what_cleared_under_free_play_once_and_brings_earn_a_credit_back()
    {
        var m = New(new Rules { Earns = FreePlay });
        m.Send(new MissionDone(Missions.SayHello));
        m.Send(new MissionDone(Missions.OpenMemory));

        m.Change(new Rules());
        var back = m.CheckAt(m.Now.AddSeconds(5));

        Assert.Equal([Missions.SayHello, Missions.OpenMemory], back.OfType<MissionPaid>().Select(p => p.Mission.Id));
        Assert.Equal(Min(30), m.State.TimeLeftMs(m.Rules));
        Assert.Contains(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: false), x => x.Id == Missions.EarnACredit);
    }

    [Fact]
    public void However_often_the_mode_is_switched_a_mission_pays_once_and_what_was_paid_stays()
    {
        var m = New();
        m.Send(new MissionDone(Missions.SayHello));

        for (var i = 0; i < 4; i++)
        {
            m.Change(new Rules { Earns = FreePlay });    // every way to earn unticked
            m.CheckAt(m.Now.AddSeconds(5));
            Assert.Equal(Min(15), m.State.TimeLeftMs(m.Rules));       // kept, out of sight
            m.Change(new Rules());
            m.CheckAt(m.Now.AddSeconds(5));
        }

        Assert.Single(m.Events.OfType<MissionPaid>());
        Assert.Equal(Min(15), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_profile_set_up_before_missions_has_none_and_its_play_time_is_unchanged()
    {
        var m = new Machine();                           // Missions null: as a profile from before them loads
        m.Focus(TimeSpan.FromMinutes(60));
        var before = m.State.TimeLeftMs(m.Rules);

        var events = m.Send(new MissionDone(Missions.SayHello));

        Assert.Empty(events);
        Assert.Null(m.State.Missions);
        Assert.Equal(before, m.State.TimeLeftMs(m.Rules));
        Assert.Equal(Min(15), before);                   // focus paid only for itself
        Assert.Empty(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: false));
    }

    [Fact]
    public void Where_the_voice_takes_a_download_the_list_is_done_without_it_and_it_still_pays()
    {
        var m = New();
        foreach (var mission in Missions.All.Where(x => x.Id != Missions.HearTheirVoice))
            m.Send(new MissionDone(mission.Id));

        Assert.Empty(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: true));
        Assert.Equal(Missions.HearTheirVoice, Assert.Single(Missions.Left(m.State.Missions, m.Rules.Earns, voiceExtra: false)).Id);
        Assert.Single(m.Send(new MissionDone(Missions.HearTheirVoice)).OfType<MissionPaid>());
        Assert.Equal(Min(90), m.State.TimeLeftMs(m.Rules));
    }
}
