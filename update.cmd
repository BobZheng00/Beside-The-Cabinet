@echo off
rem Brings this folder up to GitHub's main after pull requests are merged, then rebuilds: the code run.cmd and demo.cmd
rem start, and the standalone app (build.cmd). Double-click it; close the app first. It changes nothing if this folder
rem isn't on main or has changes of its own.
setlocal
cd /d "%~dp0"
set "BRANCH="
for /f "delims=" %%b in ('git branch --show-current') do set "BRANCH=%%b"
if not "%BRANCH%"=="main" (
  echo This folder isn't on main: nothing was changed.
  echo Stopped.
  pause
  exit /b 1
)
git diff --quiet && git diff --cached --quiet
if errorlevel 1 (
  echo This folder has changes of its own: nothing was changed.
  echo Stopped.
  pause
  exit /b 1
)
tasklist /fi "imagename eq BesideTheCabinet.exe" 2>nul | find /i "BesideTheCabinet.exe" >nul
if not errorlevel 1 (
  echo Beside the Cabinet is open: quit it from the tray, then run this again.
  echo Stopped.
  pause
  exit /b 1
)
rem The pull can rewrite this very file, and cmd reads a batch file as it goes: everything from here on is one block,
rem read whole before it runs, and it ends with exit rather than going back to the file.
(
  echo Bringing main up to GitHub's...
  git pull --ff-only || (echo Stopped. & pause & exit /b 1)
  echo.
  echo Building the code run.cmd and demo.cmd start...
  dotnet build "src\InsertCoin.Game\InsertCoin.Game.csproj" -nologo -v q || (echo Stopped. & pause & exit /b 1)
  echo.
  echo Building the standalone app...
  call "%~dp0build.cmd" || (echo Stopped. & pause & exit /b 1)
  echo.
  echo Up to date:
  git log -1 --oneline
  pause
  exit /b 0
)
