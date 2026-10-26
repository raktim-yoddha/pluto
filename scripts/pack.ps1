# Builds a release into out/Releases with Velopack (dotnet tool install -g vpk):
#   Pluto-Setup.exe      one-click install to %LocalAppData%\PlutoApp, a Start menu shortcut, updates on its own
#   Pluto-Portable.zip   unzip anywhere and run Pluto.exe; it updates itself too
#   PlutoApp-*.nupkg, releases.win.json, assets.win.json, RELEASES   the update feed the app reads from the release
# Usage: scripts/pack.ps1 -Version 0.2.0 [-Feed <folder>]   (-Feed packs into another folder, to test updates locally)
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Feed
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$releases = if ($Feed) { $Feed } else { Join-Path $root 'out\Releases' }
$repo = 'https://github.com/raktim-yoddha/pluto'

& "$PSScriptRoot\publish.ps1"

if (-not $Feed) {
    Remove-Item $releases -Recurse -Force -ErrorAction SilentlyContinue
    # The previous release, so Velopack can also make a small delta update from it (none before the first).
    try { vpk download github --repoUrl $repo --token (gh auth token) -o $releases } catch { Write-Host 'No earlier release to diff against.' }
}

# PlutoApp, not Pluto: the install folder must not be %LocalAppData%\Pluto, where settings and the to-do list live
# (uninstalling removes the whole install folder).
vpk pack --packId PlutoApp --packVersion $Version --packDir "$root\out\Pluto" --mainExe Pluto.exe `
    --packTitle Pluto --packAuthors Pluto --icon "$root\src\Pluto\Assets\Logo\pluto.ico" `
    --shortcuts StartMenuRoot --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Move-Item -Force "$releases\PlutoApp-win-Setup.exe" "$releases\Pluto-Setup.exe"
Move-Item -Force "$releases\PlutoApp-win-Portable.zip" "$releases\Pluto-Portable.zip"
Write-Host "Done: $releases"
Get-ChildItem $releases | Select-Object Name, Length | Format-Table -AutoSize
