@echo off
rem Demo profile: Steam ignored, separate save. Click the cabinet, then F1/F2/F3 = solve Easy/Medium/Hard,
rem F5 = fake game on/off, F6 = clock x60, F7 = AFK on/off, F8 = she suggests standing up (a word first), F9 = wipe demo state,
rem Esc = quit. After a code change, build first (build.cmd or dotnet build): this only starts it.
start "" "<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0src\InsertCoin.Game" -- --data="%APPDATA%\InsertCoin-demo" --no-steam
