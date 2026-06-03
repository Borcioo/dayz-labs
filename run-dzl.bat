@echo off
REM ============================================================================
REM  run-dzl.bat — build + launch dzl as the NORMAL (non-elevated) user.
REM
REM  Why: if your terminal / Windows Terminal profile runs elevated, `dotnet run`
REM  inherits admin -> dzl + WorkDrive mount P: in the ADMIN session, which the
REM  (non-admin) game and Explorer can't see. Launching the built exe through
REM  explorer.exe drops it to the normal user level regardless of how this .bat
REM  was started, so P: mounts where the game sees it.
REM
REM  Usage: double-click it, or run it from any terminal (admin or not).
REM ============================================================================
setlocal
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\Dzl.Tray\Dzl.Tray.csproj"
set "EXE=%ROOT%src\Dzl.Tray\bin\Debug\net8.0-windows\Dzl.Tray.exe"

echo [dzl] Building...
dotnet build "%PROJ%" -c Debug -v quiet -nologo
if errorlevel 1 (
  echo [dzl] Build FAILED.
  pause
  exit /b 1
)

if not exist "%EXE%" (
  echo [dzl] Built exe not found at:
  echo       %EXE%
  pause
  exit /b 1
)

echo [dzl] Launching as normal user via explorer (de-elevated)...
REM explorer.exe runs in the non-elevated shell context, so the child inherits
REM the NORMAL user token even if this script is elevated. (explorer returns a
REM non-zero exit code by design; that's expected and harmless.)
explorer.exe "%EXE%"

echo [dzl] Launched. (If an OLD admin instance is still running, close it first
echo       via Task Manager so the new non-admin one can take over.)
endlocal
