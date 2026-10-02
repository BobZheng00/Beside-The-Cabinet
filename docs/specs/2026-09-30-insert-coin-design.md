# INSERT COIN — design spec

*Working title.* A Windows desktop companion: an arcade cabinet that pays out gaming time
for solved LeetCode problems and charges it back while Steam games run.

Status: approved 2026-09-30.

## 1. Goals

- Earn gaming time by solving LeetCode (leetcode.com) problems; spend it while Steam games run.
- Fun and polished to look at and interact with (cozy anime arcade cabinet).
- Soft enforcement only: warnings, sounds and debt. Never kill or block a game.
- Built so it can be published later (possibly on Steam).

Non-goals for v1: non-Steam games, other task sources, multiple skins, mini-games,
Steam achievements, localization.

## 2. Stack and structure

Godot 4 (.NET / C#) for the app; plain .NET libraries for everything else.

```
insert-coin/
  src/
    InsertCoin.Core/       pure rules → events. No Godot, no I/O, no clock.
    InsertCoin.Adapters/   LeetCode client, Steam detector, state/settings storage
    InsertCoin.Game/       Godot project: scenes, skins, shaders, physics, audio, windows, tray
  tests/
    InsertCoin.Core.Tests/       xUnit, fake clock
    InsertCoin.Adapters.Tests/   recorded LeetCode JSON, fake registry
  tools/sfx/               sound-effect generator (.wav output, committed)
```

Seams:

- `ITaskSource` — completed problems. v1: LeetCode public GraphQL
  (`recentAcSubmissionList`, `question(titleSlug){difficulty}`, `matchedUser` for validation).
- `IGameDetector` — running games. v1: Steam registry, `HKCU\Software\Valve\Steam\Apps\<id>\Running == 1`,
  minus an ignore list (default: Wallpaper Engine 431960; the app's own Steam app id once it has one).
  Verified on the dev machine: the per-app flag is correct where process scanning is not
  (Wallpaper Engine runs from `steamapps\common` but has `Running = 0`).
- `Ledger.Step(state, input, now) → (state, events)` — the whole rulebook as a pure function.
  Inputs: game tick, tasks observed, activity, time passing. Events: `CoinsEarned`, `DebtPaid`,
  `TokenInserted`, `LowTime`, `CreditsOut`, `IouPrinted`, `SessionStarted`, `SessionEnded`,
  `WeeklyReset`, `Paused`/`Resumed`.
- The Godot side never computes balances. A `Director` maps events to animation + sound.
  Future listeners (Steam achievements) subscribe to the same events.

Runtime loop: game detector every 5 s; LeetCode every 60 s plus a CHECK button (rate-limited to once / 10 s);
play time measured with a monotonic clock, capped at 30 s per tick (sleep / clock changes never charge);
state saved atomically (temp file + replace, with `.bak`) to `%APPDATA%\InsertCoin\` on every event and
every 60 s during play; single instance via named mutex (second launch focuses the first and exits).

## 3. Rules

Units: 1 token = 15 min = 900 s. State: `credits` (whole tokens), `fuel` (0–900 s on the inserted token),
`debt` (seconds owed, already ×1.5). Invariant: `debt > 0 ⇒ credits = 0 ∧ fuel = 0`.

Earning (each new accepted problem):

- Easy 1 token, Medium 2, Hard 4 (configurable in settings file).
- Star token: +1 on the first counted solve of a day when the streak including today ≥ 3.
- Earned seconds pay debt first. Remainder: whole tokens → credits; partial remainder → fuel
  (overflow above 900 becomes a credit).
- A problem (title slug) counts once ever, and only if accepted at or after install time.
  First run and username change: every currently visible accepted submission is marked seen, no reward.
- Unknown difficulty (lookup failed): the solve stays pending and is retried; never guessed.

Spending (while ≥ 1 non-ignored game runs; simultaneous games are charged once):

- Burn fuel; at 0, auto-insert the next credit (`TokenInserted`). If the session starts with fuel 0 and
  credits > 0, insert immediately.
- `LowTime(5m)` / `LowTime(1m)` when total time left (`credits×900 + fuel`) crosses 300 s / 60 s, also at
  session start if already under. Each re-arms once total time rises above its threshold.
- At 0 with no credits: `CreditsOut` (once per stretch), then debt grows at 1.5 × elapsed.
- `IouPrinted(n)` each time `floor(debt / 900)` increases — every 10 min of overtime. This is the nag.
- AFK pause: no keyboard/mouse/controller input for 10 min → meter stops (`Paused`), resumes on input.
  No retroactive refund.
- Session ends after the game has been absent for 2 consecutive checks (10 s) → `SessionEnded` receipt:
  game names, duration, time drawn from credits/fuel, debt added.

Days and weeks:

- A day starts at 04:00 local time. The week starts Monday 04:00.
- Weekly reset: credits and fuel expire, debt stays. If a session is running at the boundary the reset is
  pending until the session ends or the next earning, whichever is first (reset applies before that earning).
  Missed while off → applied at startup (once, however many weeks passed).
- Streak = consecutive days with ≥ 1 counted solve, ending today or yesterday. HI-SCORE = longest streak.

Honor system by design: quitting the app or editing its save file defeats it; autostart is the only enforcement.

## 4. Presentation

Cabinet window: transparent, borderless, mouse passthrough outside the cabinet silhouette and ticket pile;
draggable, snaps to edges, remembers position; S/M/L scale. Normal z-order by default; rises above other
windows without focus for ~6 s on coin drops, receipts and weekly reset; never while a game runs;
optional Always-on-top.

Anatomy: marquee (title, chasing lights, `COMBO xN`), CRT screen, control panel (CHECK button, joystick to
wiggle), coin door + tray (physics coins: bronze = Easy, silver = Medium, gold = Hard, star = streak),
printer slot (IOU tickets and receipts pile below; a partial ticket sticks out of the slot), props (stool,
stickers, cables).

CRT screen: big `TIME mm:ss` = total time left; `CREDIT nn`; thin fuel bar for the inserted token; status
(`GAME: <name>`, `PAUSED`, `LINK DOWN`, `NO STEAM`). Attract mode when idle: HI-SCORE, this week (solves,
tokens earned, time played, IOUs), `INSERT COIN`. First-run onboarding on the screen: ENTER YOUR INITIALS,
then LeetCode username (validated; `PLAYER NOT FOUND`).

Moments: solve → coins drop (Hard = jackpot lights); debt paid → tickets stamped PAID and shredded before
coins reach the tray; game start → token inserted, clink; 5:00 / 1:00 → beeps; 0:00 → `CONTINUE? 10…9…`
(cosmetic) then printer; game quit → receipt; Monday → `CASH BOX COLLECTED`.

In-game channel: sound first (Windows DND hides toasts during games). Separate small unfocusable topmost
ticket window in a corner (default bottom-right) for LowTime / CreditsOut / IOU, ~5 s. Visible over
borderless games; exclusive fullscreen gets sound only.

Tray (Godot `StatusIndicator`): coin glyph tinted cyan / amber (< 15 min) / magenta (debt); tooltip
`57:47 left · CREDIT 03 · COMBO x4`; menu: Show cabinet, Check now, Always on top, In-game tickets,
Energy saver, Start with Windows (HKCU Run key), Quit.

Art: v1 skin "Cozy" — original 2D layered SVG, cel-shaded, soft dusty purple with glowing pink screen, cute
details; inspired by (not copied from) a reference render. Screen rendered in a SubViewport through a CRT
shader (scanlines, curvature, slight RGB fringe, flicker, phosphor glow); bloom on emissive parts.
Fonts OFL-licensed only.

Sound: ~15 effects generated by `tools/sfx` (sfxr-style synthesis, license-free). Buses: SFX and Alerts
(separate volumes).

Performance: full effects by default. Always: redraw only on change; while a game runs, cabinet
animation and physics pause (1 Hz countdown + 5 s checks only). Energy saver toggle: FPS cap, no bloom,
physics frozen.

Dev console (debug builds): fire any event, fake sessions, 60× clock.

## 5. Skins (architecture in v1, one skin shipped)

A skin = art for each part + palette + CRT shader params + sounds + anchor points (screen rect, coin slot,
tray area, printer slot, marquee). Animations and physics bind to anchors, never to art, so any skin works.
Planned: Cozy (v1), Cyberpunk neon, Retro, Wasteland. Commissioned art can replace skins without code changes.

## 6. Errors

LeetCode down / schema change → `LINK DOWN`, exponential backoff to 15 min, solves counted on recovery.
No Steam / key missing → `NO STEAM`, nothing charged. Corrupt state → restore `.bak`; if also corrupt, start
fresh and keep the bad file as `state.corrupt-<timestamp>.json`. Game accounting never depends on LeetCode.

## 7. Testing

Core: xUnit table tests with a fake clock for every rule in §3. Adapters: LeetCode parsing from recorded
JSON; Steam detector against a fake registry reader. App: spike measurements (transparency, passthrough,
unfocusable popup over a borderless game, CPU/GPU while gaming), dev-console checklist, and one live check
that `Running` returns to 0 after quitting a game.

## 8. Build order

1. Spike: transparent glowing window + passthrough + unfocusable popup + idle cost; choose renderer.
2. Art test: one SVG cabinet in the Cozy style, judged before more art is made.
3. Core + tests. 4. Adapters + tests. 5. Cabinet scenes and effects. 6. Sound. 7. Tray, settings, onboarding.

## 8a. Spike results (2026-09-30, Godot 4.7.2 .NET, the development PC)

| Check | Result |
|---|---|
| Renderer | **Compatibility (OpenGL 3)**. Forward+ (Vulkan) reports no window transparency and draws a black box; 797 MB vs 195 MB working set. |
| Transparent borderless window | Works (Compatibility). |
| Mouse passthrough polygon | Works: outside the silhouette, clicks reach the window underneath. |
| Unfocusable topmost popup | Works: foreground stays on the previously focused window. |
| Idle cost, static | ~1.9 % of one core (0.06 % of machine). |
| Idle cost, glow pulsing continuously | ~6.2 % of one core (0.2 % of machine). |
| Godot SVG import | Drops `<text>` and filters (blur). |
| Usable screen rect | Equals full screen when the taskbar auto-hides → popup keeps a 72 px bottom margin in that case. |

Decisions from the spike:

- **Art pipeline:** skin SVGs are rendered by headless Chrome to transparent PNG at 2× (text, blur shadows and baked
  glow survive), then imported by Godot at 0.5 scale. A `tools/art` script does this per part/layer.
- **Glow:** no engine bloom (none usable with transparency on Compatibility). Glow comes from baked soft layers plus
  additive sprites animated with tweens.
- **Live text** (screen, receipts, tickets) is Godot labels with OFL fonts, not SVG text.
- Game C# project targets `net10.0` (only .NET 9/10 runtimes installed); Core/Adapters stay `net8.0`.

## 9. Future (noted, not in v1)

- More skins (see §5); skin picker.
- A companion character on the stool, later with an LLM behind it as a time-management helper. Research and
  spike results: [../research/2026-10-02-companion-character.md](../research/2026-10-02-companion-character.md).
- Mini-games playable on the CRT via a small `IMiniGame` interface; possibly cost a token to play.
- Steam release: Steamworks.NET, achievements as event listeners (`JACKPOT` first Hard, `DEBT FREE`,
  `7-DAY COMBO`), final name + trademark check, review LeetCode terms (unofficial API).
- More task sources (Codeforces official API) and game detectors (process list for non-Steam games).
- Optional browser extension for instant solve detection.
