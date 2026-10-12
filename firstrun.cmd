@echo off
rem A first run, as a new player sees it: a fresh profile every time (%APPDATA%\InsertCoin-firstrun, emptied at each
rem start), Steam ignored. The welcome asks your name, who keeps you company and her mind: paste your own key there;
rem it stays in this profile, encrypted, until the next start empties it. Getting started then has the rest.
rem Only one copy runs at a time: quit the demo first (its tray icon, Quit). After a code change, build first.
tasklist /FI "IMAGENAME eq Godot_v4.7.2-stable_mono_win64.exe" | find /I "Godot" >nul && (
  echo A copy of the cabinet ^(or the Godot editor^) is running. Only one cabinet runs at a time: quit the demo from its
  echo tray icon first, or nothing will appear.
  pause
)
if exist "%APPDATA%\InsertCoin-firstrun" rmdir /s /q "%APPDATA%\InsertCoin-firstrun"
start "" "<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0src\InsertCoin.Game" -- --data="%APPDATA%\InsertCoin-firstrun" --no-steam
