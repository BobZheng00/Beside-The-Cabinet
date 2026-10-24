@echo off
rem Brings this folder up to GitHub's main after pull requests are merged, then rebuilds: the code run.cmd and demo.cmd
rem start, and the standalone app (build.cmd). Double-click it; close the app first. It changes nothing if this folder
rem isn't on main or has changes of its own.
setlocal
cd /d "%~dp0"
set "BRANCH="
for /f "delims=" %%b in ('git branch --show-current') do set "BRANCH=%%b"
if not "%BRANCH%"=="main" (
  echo This folder is on "%BRANCH%", not main: nothing was changed.
  goto :fail
)
git diff --quiet && git diff --cached --quiet
if errorlevel 1 (
  echo This folder has changes of its own: nothing was changed.
  goto :fail
)
tasklist /fi "imagename eq BesideTheCabinet.exe" 2>nul | find /i "BesideTheCabinet.exe" >nul
if not errorlevel 1 (
  echo Beside the Cabinet is open: quit it from the tray, then run this again.
  goto :fail
)
echo Bringing main up to GitHub's...
git pull --ff-only || goto :fail
echo.
echo Building the code run.cmd and demo.cmd start...
dotnet build "src\InsertCoin.Game\InsertCoin.Game.csproj" -nologo -v q || goto :fail
echo.
echo Building the standalone app...
call "%~dp0build.cmd" || goto :fail
echo.
echo Up to date:
git log -1 --oneline
pause
exit /b 0
:fail
echo Stopped.
pause
exit /b 1
