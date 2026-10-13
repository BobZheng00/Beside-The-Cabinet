@echo off
rem Launches the Beside the Cabinet Godot project detached from this console. Esc closes the cabinet.
if exist "%~dp0private\local.cmd" call "%~dp0private\local.cmd"
if not defined GODOT (
  echo Set GODOT to your Godot 4.7.2 .NET editor ^(its .exe^), or put set "GODOT=..." in private\local.cmd.
  pause
  exit /b 1
)
start "" "%GODOT%" --path "%~dp0src\InsertCoin.Game"
