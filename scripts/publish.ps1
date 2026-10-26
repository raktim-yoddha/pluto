# Builds out/Pluto: the pet (WinUI 3, self-contained: no .NET or Windows App SDK install needed) plus the NativeAOT
# hook relay. NativeAOT needs Visual Studio's "Desktop development with C++" workload. scripts/pack.ps1 turns it into
# the release (Setup.exe, portable zip, update packages).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
Remove-Item "$out\Pluto", "$out\hook" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\Pluto.Hook" -c Release -o "$out\hook"
dotnet publish "$root\src\Pluto" -c Release -p:SelfContained=true -o "$out\Pluto"
Remove-Item "$out\Pluto\pluto-hook.*" -ErrorAction SilentlyContinue
Copy-Item "$out\hook\pluto-hook.exe" "$out\Pluto\"
Write-Host "Done: $out\Pluto"
