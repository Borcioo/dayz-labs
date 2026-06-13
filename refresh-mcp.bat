@echo off
REM ============================================================================
REM  refresh-mcp.bat — kill the running dzl MCP server, then rebuild it (Release).
REM
REM  Why: the MCP server is a stdio child process the MCP client (Claude) spawns
REM  and runs from the RELEASE build. A code change does NOT take effect until the
REM  old process is killed (it holds the old code in memory and locks the .exe) and
REM  the Release exe is rebuilt. After this runs, reconnect in your client
REM  (/mcp in Claude Code) so it respawns a fresh server from the new exe.
REM
REM  Usage: double-click it, or run from any terminal.
REM ============================================================================
setlocal
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\Dzl.Mcp\Dzl.Mcp.csproj"

echo [mcp] Killing running Dzl.Mcp.exe instances...
taskkill /F /IM Dzl.Mcp.exe >nul 2>&1
if errorlevel 1 (
  echo [mcp] None were running.
) else (
  echo [mcp] Killed.
)

REM Give Windows a moment to release the .exe file handle before rebuilding.
ping -n 2 127.0.0.1 >nul

echo [mcp] Rebuilding (Release)...
dotnet build "%PROJ%" -c Release -v quiet -nologo
if errorlevel 1 (
  echo [mcp] Build FAILED.
  pause
  exit /b 1
)

echo [mcp] Done. Now run /mcp in your client (e.g. Claude Code) to reconnect.
endlocal
