@echo off
rem Demo profile: Steam ignored, separate save. Click the cabinet, then F1/F2/F3 = solve Easy/Medium/Hard,
rem F4 = she asks for your bedtime (a word first, a ticket if unanswered),
rem F5 = fake game on/off, F6 = clock x60, F7 = AFK on/off, F8 = she suggests standing up (a word first, no ticket), F9 = wipe demo state, F10 = her upkeep now (notes, facts, plans, threads without waiting 10 quiet minutes),
rem Esc = quit. After a code change, build first (build.cmd or dotnet build): this only starts it.
if exist "%~dp0private\local.cmd" call "%~dp0private\local.cmd"
if not defined GODOT (
  echo Set GODOT to your Godot 4.7.2 .NET editor ^(its .exe^), or put set "GODOT=..." in private\local.cmd.
  pause
  exit /b 1
)
start "" "%GODOT%" --path "%~dp0src\InsertCoin.Game" -- --data="%APPDATA%\InsertCoin-demo" --no-steam
