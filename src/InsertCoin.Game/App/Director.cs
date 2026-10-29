using InsertCoin.Core;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;

namespace InsertCoin.Game.App;

/// <summary>
/// Turns ledger events into moments: cabinet animation, sound, and — for things that happen mid-game — the corner
/// ticket. The only place that knows both the rules and the presentation.
/// </summary>
public sealed class Director(CabinetView cabinet, Func<CompanionView?> companionNow, AudioBank audio, AlertTicket ticket, Func<Rules> rules, Func<bool> inGameTickets)
{
    public void Play(LedgerEvent e)
    {
        var companion = companionNow();
        switch (e)
        {
            case CoinsEarned c:
                var kind = c.Task.Difficulty switch { Difficulty.Hard => CoinKind.Gold, Difficulty.Medium => CoinKind.Silver, _ => CoinKind.Bronze };
                Drop(kind, c.Paid.Coins, star: c.StarToken);
                var jackpot = c.Task.Difficulty == Difficulty.Hard;
                if (jackpot)
                    audio.Play("jackpot");
                cabinet.Lights.Burst(LightMode.Celebrate, jackpot ? 3.5 : 1.8);
                companion?.Play(jackpot ? Moment.Cheer : Moment.Pump);
                cabinet.Screen.Flash(jackpot ? "JACKPOT" : Gain(c.Paid, $"+{c.Tokens}"),
                                     Truncate(c.Task.Title, 40) + (c.StarToken ? $"\nSTREAK x{c.Streak} BONUS" : "") + Capped(c.Paid), 3.0);
                break;
            case FocusPaid f when f.Paid.EarnedMs >= 30_000:  // less than two minutes of focus: nothing to show
                Drop(CoinKind.Bronze, f.Paid.Coins, star: false);
                if (f.Paid.Coins == 0 && f.Paid.CreditedMs > 0)
                    audio.Play("coin", pitch: 1.3f, delay: 0.25);                          // the fuel bar fills, no coin yet
                cabinet.Lights.Burst(LightMode.Celebrate, 1.2);
                companion?.Play(Moment.Glance);
                cabinet.Screen.Flash(Gain(f.Paid, $"+{Clock(f.Paid.EarnedMs)}"), $"FOCUS {Minutes(f.FocusedMs)}" + Capped(f.Paid), 3.0);
                break;
            case ActivityPaid a:
                Drop(a.Size switch { Size.Big => CoinKind.Gold, Size.Medium => CoinKind.Silver, _ => CoinKind.Bronze }, a.Paid.Coins, star: false);
                if (a.Paid.Coins == 0 && a.Paid.CreditedMs > 0)
                    audio.Play("coin", pitch: 1.3f, delay: 0.25);
                audio.Play("paid", delay: 0.1);
                cabinet.Lights.Burst(LightMode.Celebrate, 1.8);
                companion?.Play(Moment.Pump);
                cabinet.Screen.Flash(a.Total < 1 ? "HALF" : "STAMPED", Truncate(a.Name, 28) + "\n" + Gain(a.Paid, $"+{Clock(a.Paid.EarnedMs)}") + Capped(a.Paid), 3.0);
                break;
            case StreakStar s:
                Drop(CoinKind.Star, s.Paid.Coins, star: true);
                audio.Play("paid", delay: 0.1);
                cabinet.Lights.Burst(LightMode.Celebrate, 1.8);
                companion?.Play(Moment.Pump);
                cabinet.GetTree().CreateTimer(3.2).Timeout += () =>      // after the earning that made the day good
                    cabinet.Screen.Flash(Gain(s.Paid, "+1"), $"STREAK x{s.Streak} BONUS" + Capped(s.Paid), 3.0);
                break;
            case CapReached r:
                audio.Play("cashbox", delay: 0.6);
                cabinet.GetTree().CreateTimer(3.2).Timeout += () => cabinet.Screen.Flash("CAP", $"TODAY'S PLAY EARNED\n{r.CapMs / 3_600_000}:{r.CapMs / 60_000 % 60:00}", 3.0);
                break;
            case DebtPaid p:
                audio.Play("paid", delay: 0.4);
                companion?.Play(p.DebtLeftMs == 0 ? Moment.Cheer : Moment.Glance);
                if (p.DebtLeftMs == 0)
                    cabinet.Printer.Paid();
                cabinet.Screen.Flash(p.DebtLeftMs == 0 ? "PAID" : "-IOU", p.DebtLeftMs == 0 ? "DEBT FREE" : "PAYING OFF DEBT", 2.5);
                break;
            case TokenInserted:
                audio.Play("token");
                cabinet.Tray.TakeOne();
                cabinet.FlashSlot();
                companion?.Play(Moment.Glance);
                break;
            case LowTime { Level: Warning.FiveMinutes }:
                audio.Alert("warn5");
                cabinet.Lights.Burst(LightMode.Alarm, 3);
                companion?.Play(Moment.Alarm);
                cabinet.Screen.Flash("5:00", "LEFT - LAST TOKEN", 3.0);
                Ticket("5:00 LEFT", "Your last token is burning");
                break;
            case LowTime { Level: Warning.OneMinute }:
                audio.Alert("warn1");
                cabinet.Lights.Burst(LightMode.Alarm, 4);
                companion?.Play(Moment.Alarm);
                cabinet.Screen.Flash("1:00", "LEFT - SAVE YOUR GAME", 3.0);
                Ticket("1:00 LEFT", "Save your game");
                break;
            case CreditsOut:
                audio.Alert("credits_out");
                Ticket("OUT OF TIME", "Playing on credit: x1.5");
                companion?.Play(Moment.Worry);
                Continue(10);
                break;
            case IouPrinted i:
                audio.Alert("printer");
                cabinet.Printer.PrintWhole();
                companion?.Play(Moment.Worry);
                cabinet.Screen.Flash($"IOU #{i.TicketNumber}", "PLAYING ON CREDIT", 2.5);
                Ticket($"IOU #{i.TicketNumber}", $"{i.DebtMs / 60_000} min owed" + (rules().Earns.LeetCode ? " - solve to pay" : ""));
                break;
            case SessionStarted s:
                audio.Play("game_on");
                companion?.Play(Moment.Glance);
                cabinet.Screen.Flash("GAME ON", Truncate(string.Join(" + ", s.GameNames), 28), 2.5);
                break;
            case SessionEnded end:
                audio.Play("game_over");
                companion?.Play(Moment.Glance);
                audio.Play("printer", delay: 0.9);
                cabinet.GetTree().CreateTimer(0.9).Timeout += () => cabinet.Receipt.Print(end.Receipt, rules());
                cabinet.Screen.Flash("GAME OVER", $"PLAYED {Minutes(end.Receipt.PlayedMs)}\nIOU {end.Receipt.IousPrinted}", 4.0);
                break;
            case Paused:
                companion?.Play(Moment.Doze);
                break;
            case Resumed:
                companion?.Play(Moment.Wake);
                break;
            case LateNight l:
                audio.Alert("remind");
                companion?.Play(Moment.Worry);
                var times = l.Multiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                cabinet.Screen.Flash("LATE NIGHT", $"PAST BEDTIME\nPLAY COSTS x{times}", 4.0);
                Ticket("LATE NIGHT", $"Past your bedtime: play costs x{times} until {rules().DayStartsAt.Hours:00}:00");
                break;
            case Faded f:
                audio.Play("cashbox");
                cabinet.Tray.Set((int)(f.LeftMs / rules().TokenMs));
                cabinet.Screen.FlashCount(f.FadedMs + f.LeftMs, f.LeftMs, $"NEW DAY\n{Clock(f.FadedMs)} FADED OVERNIGHT", 4.5);
                break;
        }
    }

    /// <summary>Coins into the tray, each landing a little higher; with a star, the last one is it.</summary>
    private void Drop(CoinKind kind, int count, bool star)
    {
        var coins = Enumerable.Repeat(kind, Math.Max(0, count - (star && count > 0 ? 1 : 0))).ToList();
        if (star && count > 0)
            coins.Add(CoinKind.Star);
        cabinet.Tray.Drop(coins);
        for (var i = 0; i < coins.Count; i++)
            audio.Play("coin", pitch: 1f + 0.07f * i, delay: 0.18 * i + 0.25);
    }

    /// <summary>The big line for an earning: as usual, or what today's cap let through.</summary>
    private static string Gain(Payout paid, string usual) =>
        paid.OverCapMs <= 0 ? usual : paid.CreditedMs > 0 ? $"+{Clock(paid.CreditedMs)}" : "CAP";

    private static string Capped(Payout paid) => paid.OverCapMs > 0 ? "\nTODAY'S CAP" : "";

    /// <summary>"7:30", "1:05:00".</summary>
    private static string Clock(long ms)
    {
        var total = ms / 1000;
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
    }

    private void Ticket(string title, string detail)
    {
        if (inGameTickets())
            ticket.Show(title, detail);
    }

    /// <summary>Classic arcade continue countdown (cosmetic; debt already counts).</summary>
    private void Continue(int from)
    {
        cabinet.Lights.SetMode(LightMode.Alarm);
        for (var n = from; n >= 1; n--)
        {
            var shown = n;
            cabinet.GetTree().CreateTimer(from - n + 1.2).Timeout += () =>
            {
                cabinet.Screen.Flash($"{shown}", "CONTINUE?", 1.05);
                audio.Play("continue_tick");
            };
        }
    }

    private static string Minutes(long ms) => ms >= 3_600_000 ? $"{ms / 3_600_000}H {ms / 60_000 % 60:00}M" : $"{ms / 60_000}M";
    private static string Truncate(string s, int max) => s.Length <= max ? s.ToUpperInvariant() : s[..(max - 3)].ToUpperInvariant() + "...";
}
