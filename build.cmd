@echo off
rem Builds the standalone app into builds\InsertCoin\ (needs Godot export templates for the same version).
setlocal
set GODOT=<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
if not exist "%~dp0builds\InsertCoin" mkdir "%~dp0builds\InsertCoin"
rem her memory search models (both by default; "python tools\models\fetch.py small" beforehand makes a lighter build)
if not exist "%~dp0src\InsertCoin.Game\models" python "%~dp0tools\models\fetch.py"
"%GODOT%" --headless --path "%~dp0src\InsertCoin.Game" --export-release "Windows Desktop" "%~dp0builds\InsertCoin\InsertCoin.exe"
