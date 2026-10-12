@echo off
rem Builds the standalone app into builds\InsertCoin\ (needs Godot export templates for the same version).
rem GODOT is your Godot 4.7.2 .NET editor's exe (the _console.exe beside it runs the export).
setlocal
if exist "%~dp0private\local.cmd" call "%~dp0private\local.cmd"
if not defined GODOT (
  echo Set GODOT to your Godot 4.7.2 .NET editor ^(its .exe^), or put set "GODOT=..." in private\local.cmd.
  exit /b 1
)
if not exist "%~dp0builds\InsertCoin" mkdir "%~dp0builds\InsertCoin"
rem her local models: memory search and ears (all by default; "python tools\models\fetch.py small" beforehand makes a lighter build)
if not exist "%~dp0src\InsertCoin.Game\models" python "%~dp0tools\models\fetch.py"
"%GODOT:.exe=_console.exe%" --headless --path "%~dp0src\InsertCoin.Game" --export-release "Windows Desktop" "%~dp0builds\InsertCoin\InsertCoin.exe"
