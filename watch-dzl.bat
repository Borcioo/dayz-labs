@echo off
REM ============================================================================
REM  watch-dzl.bat — run the tray under `dotnet watch` for fast UI iteration.
REM
REM  Save a .xaml or .cs file and the change is applied automatically: code
REM  edits hot-reload live where possible; XAML / structural ("rude") edits
REM  trigger a quick rebuild + relaunch. Either way you skip the manual
REM  build + run-dzl cycle.
REM
REM  ELEVATION: Hot Reload needs `dotnet watch` to stay the PARENT of the app,
REM  so this CANNOT de-elevate via explorer.exe the way run-dzl.bat does. Just
REM  DOUBLE-CLICK this file (or run it from a NON-elevated terminal) — that runs
REM  at your normal user level, so P: mounts where the game/Explorer can see it.
REM  Launching it from an ELEVATED terminal mounts P: in the admin session.
REM
REM  Stop with Ctrl+C in this console window.
REM ============================================================================
setlocal
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\Dzl.Tray\Dzl.Tray.csproj"
REM Auto-restart on "rude" edits (new controls, changed signatures) instead of prompting.
set "DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1"

echo [dzl] Hot Reload watch — edit .xaml / .cs and save to apply changes live.
echo [dzl] Ctrl+C here to stop.
echo.
dotnet watch --project "%PROJ%" run
endlocal
