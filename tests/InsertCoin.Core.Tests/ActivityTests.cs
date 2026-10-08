using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class ActivityTests
{
    [Theory]
    [InlineData(Size.Small, 15)]
    [InlineData(Size.Medium, 30)]
    [InlineData(Size.Big, 60)]
    public void An_activity_pays_like_a_problem_of_its_size(Size size, int minutes)
    {
        var m = new Machine();

        var paid = Assert.Single(m.Send(new ActivityDone("the gym", size, 1)).OfType<ActivityPaid>());

        Assert.Equal(Min(minutes), paid.Paid.CreditedMs);
        Assert.Equal(1, paid.Total);
        Assert.Equal(Min(minutes), m.State.TimeLeftMs(m.Rules));
        Assert.Equal(1, m.State.Today!.Activities["the gym"]);
        Assert.Equal(1, m.State.Week.Activities);
    }

    [Fact]
    public void An_activity_counts_once_a_day()
    {
        var m = new Machine();
        m.Send(new ActivityDone("a walk", Size.Small, 1));

        Assert.Empty(m.Send(new ActivityDone("a walk", Size.Small, 1)));

        Assert.Equal(Min(15), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void Half_now_and_the_rest_with_a_better_proof_the_same_day()
    {
        var m = new Machine();

        var half = m.Send(new ActivityDone("the gym", Size.Medium, 0.5)).OfType<ActivityPaid>().Single();
        var rest = m.Send(new ActivityDone("the gym", Size.Medium, 1)).OfType<ActivityPaid>().Single();

        Assert.Equal(Min(15), half.Paid.CreditedMs);
        Assert.Equal(0.5, rest.Share);
        Assert.Equal(1, rest.Total);
        Assert.Equal(Min(30), m.State.TimeLeftMs(m.Rules));
        Assert.Equal(1, m.State.Week.Activities);           // one activity, paid in two goes
    }

    [Fact]
    public void Nothing_convincing_pays_nothing()
    {
        var m = new Machine();

        Assert.Empty(m.Send(new ActivityDone("a hike", Size.Big, 0)));
        Assert.Empty(m.State.Today!.Activities);
    }

    [Fact]
    public void An_activity_is_under_the_days_cap_like_everything_else()
    {
        var m = new Machine();                              // a Wednesday: 1:30
        m.Focus(TimeSpan.FromHours(4));                     // 1:00 of it

        var paid = m.Send(new ActivityDone("a hike", Size.Big, 1)).OfType<ActivityPaid>().Single();

        Assert.Equal(Min(30), paid.Paid.CreditedMs);
        Assert.Equal(Min(30), paid.Paid.OverCapMs);
    }

    [Fact]
    public void A_new_day_counts_it_again()
    {
        var m = new Machine();
        m.Send(new ActivityDone("a walk", Size.Small, 1));
        m.Now = new DateTimeOffset(2026, 10, 1, 9, 0, 0, T0.Offset);

        Assert.Single(m.Send(new ActivityDone("a walk", Size.Small, 1)).OfType<ActivityPaid>());
        Assert.Equal(1, Assert.Single(m.State.Days).Activities["a walk"]);
    }
}
