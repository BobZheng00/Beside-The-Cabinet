@echo off
rem Builds the standalone app into builds\BesideTheCabinet\ (needs Godot export templates for the same version).
rem GODOT is your Godot 4.7.2 .NET editor's exe (the _console.exe beside it runs the export).
setlocal
if exist "%~dp0private\local.cmd" call "%~dp0private\local.cmd"
if not defined GODOT (
  echo Set GODOT to your Godot 4.7.2 .NET editor ^(its .exe^), or put set "GODOT=..." in private\local.cmd.
  exit /b 1
)
if not exist "%~dp0builds\BesideTheCabinet" mkdir "%~dp0builds\BesideTheCabinet"
rem her local models: memory search and ears (all by default; "python tools\models\fetch.py small" beforehand makes a lighter build)
rem a git worktree (its .git a file) fetches nothing unasked: its runs from source borrow the main checkout's
if exist "%~dp0.git" if not exist "%~dp0.git\" if not exist "%~dp0src\InsertCoin.Game\models" (
  echo This is a git worktree: runs from source borrow the main checkout's models, but an export needs its own.
  echo Fetch them here first ^(python tools\models\fetch.py, a download of about 400 MB^), or build in the main checkout.
  exit /b 1
)
if not exist "%~dp0src\InsertCoin.Game\models" python "%~dp0tools\models\fetch.py"
rem an emptied or half-fetched models folder would ship the app without them: say what's missing and stop
python "%~dp0tools\models\fetch.py" check || exit /b 1
"%GODOT:.exe=_console.exe%" --headless --path "%~dp0src\InsertCoin.Game" --export-release "Windows Desktop" "%~dp0builds\BesideTheCabinet\BesideTheCabinet.exe"
