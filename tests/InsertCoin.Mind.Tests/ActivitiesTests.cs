using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

public class ActivitiesTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Attachment Photo = new("gym.jpg", "image/jpeg", [1, 2, 3]);

    /// <summary>Chance as the test wants it: every roll comes out the same.</summary>
    private sealed class Roll(double value) : Random
    {
        public override double NextDouble() => value;
    }

    /// <summary>The cabinet's side: what it paid, by activity, today.</summary>
    private sealed class Cabinet
    {
        public Dictionary<string, double> Paid { get; } = [];
        public List<(string Name, Size Size, double Share)> Payments { get; } = [];
        public int Asked { get; set; }

        public Payout? Pay(string name, Size size, double share)
        {
            var before = Paid.GetValueOrDefault(name);
            if (share <= before)
                return null;
            Paid[name] = share;
            Payments.Add((name, size, share - before));
            return new Payout(1, 0, 1, 0, 0);
        }
    }

    private static (Activities Activities, Cabinet Cabinet) Make(TempStore temp, bool? answer = true, double roll = 0.99, bool blind = false,
                                                                 TimeSpan? atThePc = null, IReadOnlyList<Attachment>? inFront = null)
    {
        var cabinet = new Cabinet();
        var activities = new Activities(temp.Store, new FakeClock(Friday), new Roll(roll))
        {
            Confirm = (_, _, _) =>
            {
                cabinet.Asked++;
                return Task.FromResult(answer);
            },
            Pay = cabinet.Pay,
            PaidToday = name => cabinet.Paid.GetValueOrDefault(name),
            AtThePc = () => atThePc ?? TimeSpan.Zero,
            Blind = () => blind,
            InFront = () => inFront ?? [],
        };
        return (activities, cabinet);
    }

    [Fact]
    public async Task A_new_activity_is_sized_by_her_confirmed_kept_and_paid()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp);

        var said = await activities.Log("the gym", Size.Medium, null, null);

        Assert.Equal("Counted: the gym, 2 coins.", said);
        Assert.Equal(1, cabinet.Asked);
        Assert.Equal([("the gym", Size.Medium, 1.0)], cabinet.Payments);
        Assert.Equal(Size.Medium, Assert.Single(activities.Known).Size);
    }

    [Fact]
    public async Task One_she_knows_keeps_its_size_whatever_she_says_now()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp);
        await activities.Log("the gym", Size.Medium, null, null);
        cabinet.Paid.Clear();                               // another day

        await activities.Log("The Gym", Size.Big, null, null);

        Assert.Equal(Size.Medium, cabinet.Payments[^1].Size);
        Assert.Single(activities.Known);
    }

    [Fact]
    public async Task A_new_activity_needs_its_size()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp);

        var said = await activities.Log("knitting", null, null, null);

        Assert.StartsWith("It's new", said);
        Assert.Equal(0, cabinet.Asked);
        Assert.Empty(activities.Known);
    }

    [Theory]
    [InlineData(false, "not yet")]
    [InlineData(null, "didn't answer")]
    public async Task Not_confirmed_it_counts_nothing_and_a_new_one_isnt_kept(bool? answer, string told)
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, answer);

        var said = await activities.Log("a walk", Size.Small, null, null);

        Assert.Contains(told, said);
        Assert.Empty(cabinet.Payments);
        Assert.Empty(activities.Known);
    }

    [Fact]
    public async Task It_counts_once_a_day()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp);
        await activities.Log("a walk", Size.Small, null, null);

        Assert.Equal("a walk already counted today.", await activities.Log("a walk", null, null, null));
        Assert.Single(cabinet.Payments);
        Assert.Equal(1, cabinet.Asked);
    }

    [Fact]
    public async Task Logged_on_the_screen_it_needs_no_card()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp);
        await activities.Log("a walk", Size.Small, null, null);
        cabinet.Paid.Clear();

        await activities.Log("a walk", null, null, null, confirmed: true);

        Assert.Equal(1, cabinet.Asked);
        Assert.Equal(2, cabinet.Payments.Count);
    }

    [Fact]
    public async Task When_the_cabinet_wants_proof_it_waits_for_a_picture()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0);

        var said = await activities.Log("the gym", Size.Medium, null, null);

        Assert.Contains("judge_proof", said);
        Assert.Empty(cabinet.Payments);
        Assert.Equal(["the gym"], activities.Waiting);
        Assert.Contains("is already waiting for proof", await activities.Log("the gym", null, null, null));
    }

    [Theory]
    [InlineData(Conviction.Sure, 1.0)]
    [InlineData(Conviction.Unsure, 0.5)]
    public async Task A_picture_pays_by_how_sure_she_is(Conviction conviction, double share)
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0, inFront: [Photo]);
        await activities.Log("the gym", Size.Medium, null, null);

        activities.Judge("the gym", conviction, "a gym's weights room");

        Assert.Equal([("the gym", Size.Medium, share)], cabinet.Payments);
        Assert.Equal(conviction == Conviction.Sure ? [] : ["the gym"], activities.Waiting);
        Assert.Equal([conviction], temp.Store.Convictions(10));
    }

    [Fact]
    public async Task Half_now_and_a_clearer_picture_pays_the_rest()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0, inFront: [Photo]);
        await activities.Log("the gym", Size.Medium, null, null);

        activities.Judge("the gym", Conviction.Unsure, "a blurry room");
        activities.Judge("the gym", Conviction.Sure, "the gym's sign and a treadmill");

        Assert.Equal([0.5, 0.5], cabinet.Payments.Select(p => p.Share));
        Assert.Empty(activities.Waiting);
    }

    [Fact]
    public async Task Doubtful_pays_nothing_and_a_better_picture_still_can()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0, inFront: [Photo]);
        await activities.Log("a hike", Size.Big, null, null);

        var said = activities.Judge("a hike", Conviction.Doubtful, "a sofa");

        Assert.Contains("without lecturing", said);
        Assert.Empty(cabinet.Payments);
        Assert.Equal(["a hike"], activities.Waiting);
    }

    [Fact]
    public async Task No_picture_in_front_of_her_no_judgement()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0);
        await activities.Log("the gym", Size.Medium, null, null);

        Assert.Contains("no picture in front of you", activities.Judge("the gym", Conviction.Sure, "nothing"));
        Assert.Empty(cabinet.Payments);
        Assert.Empty(temp.Store.Convictions(10));
    }

    [Fact]
    public void Nothing_waiting_by_that_name_no_judgement()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, inFront: [Photo]);

        Assert.Contains("Nothing by that name", activities.Judge("the gym", Conviction.Sure, "a gym"));
        Assert.Empty(cabinet.Payments);
    }

    [Fact]
    public async Task A_photo_taken_another_day_counts_half_at_most()
    {
        using var temp = new TempStore();
        var old = Photo with { Taken = new DateTime(2026, 9, 30, 18, 0, 0) };
        var (activities, cabinet) = Make(temp, roll: 0, inFront: [old]);
        await activities.Log("a walk", Size.Small, null, null);

        var said = activities.Judge("a walk", Conviction.Sure, "a park path");

        Assert.Contains("another day", said);
        Assert.Equal(0.5, Assert.Single(cabinet.Payments).Share);
    }

    [Fact]
    public async Task With_a_model_that_cant_see_their_word_counts()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0, blind: true);

        await activities.Log("the gym", Size.Medium, null, null);

        Assert.Single(cabinet.Payments);
        Assert.Empty(activities.Waiting);
    }

    [Fact]
    public async Task Longer_than_they_have_been_away_doesnt_add_up_and_wants_proof()
    {
        using var temp = new TempStore();
        var (activities, cabinet) = Make(temp, roll: 0.99, atThePc: TimeSpan.FromHours(3));

        await activities.Log("a long walk", Size.Medium, 120, null);

        Assert.Empty(cabinet.Payments);
        Assert.Equal(["a long walk"], activities.Waiting);
    }

    [Fact]
    public void Proof_is_never_wanted_for_what_cant_be_photographed_and_always_when_it_doesnt_add_up()
    {
        var meditating = new Activity("meditating", Size.Small, Provable: false, Friday);
        var walk = new Activity("a walk", Size.Small, Provable: true, Friday);

        Assert.False(Activities.Wanted(meditating, [], addsUp: false, roll: 0));
        Assert.True(Activities.Wanted(walk, [], addsUp: false, roll: 0.99));
    }

    [Fact]
    public void Chance_goes_by_size_and_halves_or_doubles_with_their_record()
    {
        var gym = new Activity("the gym", Size.Medium, true, Friday);
        Conviction[] good = [.. Enumerable.Repeat(Conviction.Sure, 6)];
        Conviction[] bad = [Conviction.Doubtful, Conviction.Unsure];

        Assert.True(Activities.Wanted(gym, [], true, 0.3));         // medium: about one in three
        Assert.False(Activities.Wanted(gym, [], true, 0.4));
        Assert.False(Activities.Wanted(gym, good, true, 0.3));      // a good record: half as often
        Assert.True(Activities.Wanted(gym, bad, true, 0.6));        // after a doubtful one: twice as often
        Assert.True(Activities.Wanted(gym with { Size = Size.Big }, [], true, 0.6));
        Assert.False(Activities.Wanted(gym with { Size = Size.Small }, [], true, 0.2));
    }

    [Fact]
    public async Task Her_cabinet_block_lists_what_counts_what_counted_and_what_waits()
    {
        using var temp = new TempStore();
        var (activities, _) = Make(temp);
        Assert.Contains("none yet", activities.Describe());
        await activities.Log("the gym", Size.Medium, null, null);
        temp.Store.KeepActivity(new Activity("meditating", Size.Small, false, Friday.AddMinutes(1)));
        temp.Store.Claim(new DateOnly(2026, 10, 2), "meditating", Friday);

        var text = activities.Describe();

        Assert.Contains("the gym (medium), meditating (small, no proof)", text);
        Assert.Contains("Counted today: the gym.", text);
        Assert.Contains("Waiting for a picture as proof today: meditating.", text);
    }

    [Fact]
    public async Task While_activities_dont_earn_her_cabinet_block_says_nothing_of_them()
    {
        using var temp = new TempStore();
        var (activities, _) = Make(temp);
        await activities.Log("the gym", Size.Medium, null, null);

        activities.Earn = () => false;

        Assert.Null(activities.Describe());
        activities.Earn = () => true;
        Assert.Contains("the gym (medium)", activities.Describe());
    }

    [Fact]
    public async Task Forgetting_the_player_forgets_their_activities_and_record()
    {
        using var temp = new TempStore();
        var (activities, _) = Make(temp, roll: 0, inFront: [Photo]);
        await activities.Log("the gym", Size.Medium, null, null);
        activities.Judge("the gym", Conviction.Sure, "a gym");

        temp.Store.ForgetPlayer();

        Assert.Empty(activities.Known);
        Assert.Empty(temp.Store.Convictions(10));
    }
}
