@echo off
rem Demo profile: Steam ignored, separate save. Click the cabinet, then F1/F2/F3 = solve Easy/Medium/Hard,
rem F5 = fake game on/off, F6 = clock x60, F7 = AFK on/off, F9 = wipe demo state, Esc = quit.
start "" "<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0src\InsertCoin.Game" -- --data="%APPDATA%\InsertCoin-demo" --no-steam
