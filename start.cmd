@echo off
setlocal
rem Double-click to play. Builds, then launches Simple FPS in the Godot 4.7.1 mono build kept in the
rem MinerCraft folder.
rem Launching with --path never rebuilds, so the build has to come first.
rem Anything passed to this script lands after the bare "--" as a game flag.

set "EXE=%~dp0..\MinerCraft\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
rem From a git worktree in .claude\worktrees\<name>, the MinerCraft folder is three levels further up.
if not exist "%EXE%" set "EXE=%~dp0..\..\..\..\MinerCraft\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"

if not exist "%EXE%" (
  echo Godot 4.7.1 mono not found at:
  echo   %EXE%
  pause
  exit /b 1
)

dotnet build "%~dp0SimpleFps.Game" --nologo -v q
if errorlevel 1 (
  echo Build failed, not launching.
  pause
  exit /b 1
)

"%EXE%" --path "%~dp0SimpleFps.Game" -- %*
rem Keeps the window open after a crash, so the error can be read.
if errorlevel 1 pause
