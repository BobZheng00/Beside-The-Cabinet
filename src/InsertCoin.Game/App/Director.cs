using InsertCoin.Core;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>Turns ledger events into cabinet moments. The only place that knows both the rules and the art.</summary>
public sealed class Director(CabinetView cabinet)
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
                var jackpot = c.Task.Difficulty == Difficulty.Hard;
                cabinet.Lights.Burst(LightMode.Celebrate, jackpot ? 3.5 : 1.8);
                cabinet.Screen.Flash(jackpot ? "JACKPOT" : $"+{c.Tokens}", Truncate(c.Task.Title, 26) + (c.StarToken ? $" · STREAK x{c.Streak} ★" : ""), 3.0);
                break;
            case DebtPaid p:
                if (p.DebtLeftMs == 0)
                    cabinet.Printer.Paid();
                cabinet.Screen.Flash(p.DebtLeftMs == 0 ? "PAID" : "-IOU", p.DebtLeftMs == 0 ? "DEBT FREE" : "PAYING OFF DEBT", 2.5);
                break;
            case TokenInserted:
                cabinet.Tray.TakeOne();
                cabinet.FlashSlot();
                break;
            case LowTime { Level: Warning.FiveMinutes }:
                cabinet.Lights.Burst(LightMode.Alarm, 3);
                cabinet.Screen.Flash("5:00", "LEFT · LAST TOKEN", 3.0);
                break;
            case LowTime { Level: Warning.OneMinute }:
                cabinet.Lights.Burst(LightMode.Alarm, 4);
                cabinet.Screen.Flash("1:00", "LEFT · SAVE YOUR GAME", 3.0);
                break;
            case CreditsOut:
                Continue(10);
                break;
            case IouPrinted i:
                cabinet.Printer.PrintWhole();
                cabinet.Screen.Flash($"IOU #{i.TicketNumber}", "PLAYING ON CREDIT", 2.5);
                break;
            case SessionStarted s:
                cabinet.Screen.Flash("GAME ON", Truncate(string.Join(" + ", s.GameNames), 28), 2.5);
                break;
            case SessionEnded end:
                cabinet.Screen.Flash("GAME OVER", $"PLAYED {Minutes(end.Receipt.PlayedMs)} · IOU {end.Receipt.IousPrinted}", 4.0);
                break;
            case WeeklyReset w:
                cabinet.Tray.Set(0);
                cabinet.Screen.Flash("CASH BOX", w.ExpiredMs > 0 ? $"COLLECTED · {Minutes(w.ExpiredMs)} EXPIRED" : "NEW WEEK", 4.0);
                break;
        }
    }

    /// <summary>Classic arcade continue countdown (cosmetic; debt already counts).</summary>
    private void Continue(int from)
    {
        cabinet.Lights.SetMode(LightMode.Alarm);
        for (var n = from; n >= 1; n--)
        {
            var shown = n;
            cabinet.GetTree().CreateTimer(from - n).Timeout += () => cabinet.Screen.Flash($"{shown}", "CONTINUE?", 1.05);
        }
    }

    private static string Minutes(long ms) => ms >= 3_600_000 ? $"{ms / 3_600_000}H {ms / 60_000 % 60:00}M" : $"{ms / 60_000}M";
    private static string Truncate(string s, int max) => s.Length <= max ? s.ToUpperInvariant() : s[..(max - 1)].ToUpperInvariant() + "…";
}
