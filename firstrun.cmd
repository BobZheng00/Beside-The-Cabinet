@echo off
rem A first run, as a new player sees it: a fresh profile every time (%APPDATA%\InsertCoin-firstrun, emptied at each
rem start), Steam ignored. The welcome asks your name, who keeps you company and her mind: paste your own key there;
rem it stays in this profile, encrypted, until the next start empties it. Getting started then has the rest.
rem Only one copy runs at a time: quit the demo first (its tray icon, Quit). After a code change, build first.
if exist "%~dp0private\local.cmd" call "%~dp0private\local.cmd"
if not defined GODOT (
  echo Set GODOT to your Godot 4.7.2 .NET editor ^(its .exe^), or put set "GODOT=..." in private\local.cmd.
  pause
  exit /b 1
)
for %%I in ("%GODOT%") do set "GODOT_EXE=%%~nxI"
tasklist /FI "IMAGENAME eq %GODOT_EXE%" | find /I "%GODOT_EXE%" >nul && (
  echo A copy of the cabinet ^(or the Godot editor^) is running. Only one cabinet runs at a time: quit the demo from its
  echo tray icon first, or nothing will appear.
  pause
)
rem the art again if anything changed since the last import (a pull, a branch): a run uses Godot's imported copy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\dev\import.ps1"
if exist "%APPDATA%\InsertCoin-firstrun" rmdir /s /q "%APPDATA%\InsertCoin-firstrun"
start "" "%GODOT%" --path "%~dp0src\InsertCoin.Game" -- --data="%APPDATA%\InsertCoin-firstrun" --no-steam
