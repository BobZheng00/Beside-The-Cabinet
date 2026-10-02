@echo off
rem Builds the standalone app into builds\InsertCoin\ (needs Godot export templates for the same version).
setlocal
set GODOT=<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
if not exist "%~dp0builds\InsertCoin" mkdir "%~dp0builds\InsertCoin"
"%GODOT%" --headless --path "%~dp0src\InsertCoin.Game" --export-release "Windows Desktop" "%~dp0builds\InsertCoin\InsertCoin.exe"
