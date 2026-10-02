using InsertCoin.Core;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Desktop;

namespace InsertCoin.Game.App;

/// <summary>
/// Turns ledger events into moments: cabinet animation, sound, and — for things that happen mid-game — the corner
/// ticket. The only place that knows both the rules and the presentation.
/// </summary>
public sealed class Director(CabinetView cabinet, AudioBank audio, AlertTicket ticket, Rules rules, Func<bool> inGameTickets)
{
    public void Play(LedgerEvent e)
    {
        switch (e)
        {
            case CoinsEarned c:
                var kind = c.Task.Difficulty switch { Difficulty.Hard => CoinKind.Gold, Difficulty.Medium => CoinKind.Silver, _ => CoinKind.Bronze };
                var coins = Enumerable.Repeat(kind, c.Tokens - (c.StarToken ? 1 : 0)).ToList();
                if (c.StarToken)
                    coins.Add(CoinKind.Star);
                cabinet.Tray.Drop(coins);
                for (var i = 0; i < coins.Count; i++)
                    audio.Play("coin", pitch: 1f + 0.07f * i, delay: 0.18 * i + 0.25);      // each coin lands a little higher
                var jackpot = c.Task.Difficulty == Difficulty.Hard;
                if (jackpot)
                    audio.Play("jackpot");
                cabinet.Lights.Burst(LightMode.Celebrate, jackpot ? 3.5 : 1.8);
                cabinet.Screen.Flash(jackpot ? "JACKPOT" : $"+{c.Tokens}", Truncate(c.Task.Title, 40) + (c.StarToken ? $"\nSTREAK x{c.Streak} BONUS" : ""), 3.0);
                break;
            case DebtPaid p:
                audio.Play("paid", delay: 0.4);
                if (p.DebtLeftMs == 0)
                    cabinet.Printer.Paid();
                cabinet.Screen.Flash(p.DebtLeftMs == 0 ? "PAID" : "-IOU", p.DebtLeftMs == 0 ? "DEBT FREE" : "PAYING OFF DEBT", 2.5);
                break;
            case TokenInserted:
                audio.Play("token");
                cabinet.Tray.TakeOne();
                cabinet.FlashSlot();
                break;
            case LowTime { Level: Warning.FiveMinutes }:
                audio.Alert("warn5");
                cabinet.Lights.Burst(LightMode.Alarm, 3);
                cabinet.Screen.Flash("5:00", "LEFT - LAST TOKEN", 3.0);
                Ticket("5:00 LEFT", "Your last token is burning");
                break;
            case LowTime { Level: Warning.OneMinute }:
                audio.Alert("warn1");
                cabinet.Lights.Burst(LightMode.Alarm, 4);
                cabinet.Screen.Flash("1:00", "LEFT - SAVE YOUR GAME", 3.0);
                Ticket("1:00 LEFT", "Save your game");
                break;
            case CreditsOut:
                audio.Alert("credits_out");
                Ticket("OUT OF TIME", "Playing on credit: x1.5");
                Continue(10);
                break;
            case IouPrinted i:
                audio.Alert("printer");
                cabinet.Printer.PrintWhole();
                cabinet.Screen.Flash($"IOU #{i.TicketNumber}", "PLAYING ON CREDIT", 2.5);
                Ticket($"IOU #{i.TicketNumber}", $"{i.DebtMs / 60_000} min owed - solve to pay");
                break;
            case SessionStarted s:
                audio.Play("game_on");
                cabinet.Screen.Flash("GAME ON", Truncate(string.Join(" + ", s.GameNames), 28), 2.5);
                break;
            case SessionEnded end:
                audio.Play("game_over");
                audio.Play("printer", delay: 0.9);
                cabinet.GetTree().CreateTimer(0.9).Timeout += () => cabinet.Receipt.Print(end.Receipt, rules);
                cabinet.Screen.Flash("GAME OVER", $"PLAYED {Minutes(end.Receipt.PlayedMs)}\nIOU {end.Receipt.IousPrinted}", 4.0);
                break;
            case WeeklyReset w:
                audio.Play("cashbox");
                cabinet.Tray.Set(0);
                cabinet.Screen.Flash("CASH BOX", w.ExpiredMs > 0 ? $"COLLECTED\n{Minutes(w.ExpiredMs)} EXPIRED" : "NEW WEEK", 4.0);
                break;
        }
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
