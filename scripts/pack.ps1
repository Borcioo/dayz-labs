#requires -Version 5
# Build the dzl installer locally. Usage: scripts\pack.ps1 -Version 0.1.0
param(
  [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$release = Join-Path $root 'artifacts\release'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publish | Out-Null
# Clean the release dir too: vpk refuses to pack a version equal/older than one already present
# there, so a stale prior run (or an old packId) would abort this pack.
if (Test-Path $release) { Remove-Item $release -Recurse -Force }

# Publish all three self-contained into ONE folder. Tray LAST so its WindowsDesktop runtime
# (a superset) wins on any shared file. Identical runtime files collapse to one copy.
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', "-p:Version=$Version", '-o', $publish)
dotnet publish (Join-Path $root 'src\Dzl.Cli')  @common
dotnet publish (Join-Path $root 'src\Dzl.Mcp')  @common
dotnet publish (Join-Path $root 'src\Dzl.Tray') @common

# Friendly apphost names (renaming a published apphost .exe is safe - it locates its managed
# .dll by an embedded name, independent of the .exe filename).
Rename-Item (Join-Path $publish 'Dzl.Cli.exe')  'dzl.exe'      -Force
Rename-Item (Join-Path $publish 'Dzl.Tray.exe') 'dzl-tray.exe' -Force
Rename-Item (Join-Path $publish 'Dzl.Mcp.exe')  'dzl-mcp.exe'  -Force

# Smoke test: the CLI is a console self-contained app sharing the merged runtime - if it runs,
# the merge is sound. (If this fails, fall back to per-frontend subfolders - see plan note.)
& (Join-Path $publish 'dzl.exe') '--help' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dzl.exe --help failed (exit $LASTEXITCODE) - merged publish is broken" }
foreach ($exe in 'dzl.exe','dzl-tray.exe','dzl-mcp.exe') {
  if (-not (Test-Path (Join-Path $publish $exe))) { throw "missing $exe in publish output" }
}

# Pack into a single per-user Setup.exe + the update feed.
# packId 'DayZLabs' (install dir %LocalAppData%\DayZLabs) is deliberately NOT 'dzl': the app stores
# its config at %LocalAppData%\dzl, and Velopack's uninstall wipes the whole install dir — sharing
# it with the config dir would delete the user's settings on uninstall. packTitle stays 'dzl' so the
# Start-menu shortcut / Apps-list entry read 'dzl' (the tool name).
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { dotnet tool install -g vpk --version 1.2.0 }
vpk pack --packId DayZLabs --packVersion $Version --packDir $publish --mainExe dzl-tray.exe `
         --packTitle 'dzl' --packAuthors 'DayZ Labs' -o $release
# $ErrorActionPreference='Stop' does NOT catch native-exe failures, so check vpk's exit explicitly.
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }
Write-Host "Built: $(Join-Path $release 'DayZLabs-win-Setup.exe')"
