# Builds out/Pluto: the pet as one NativeAOT exe (no .NET install needed; it runs on Windows' shared Windows App Runtime,
# see src/Pluto/WindowsAppRuntime.cs) plus the NativeAOT hook relay. NativeAOT needs Visual Studio's "Desktop development
# with C++" workload. scripts/pack.ps1 turns it into the release (Setup.exe, portable zip, update packages).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
Remove-Item "$out\Pluto", "$out\hook" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\Pluto.Hook" -c Release -o "$out\hook"
dotnet publish "$root\src\Pluto" -c Release -o "$out\Pluto"
# Pluto uses no WebView or background tasks; the Windows App SDK copies their helpers anyway.
Remove-Item "$out\Pluto\pluto-hook.*", "$out\Pluto\*WebView2*", "$out\Pluto\*UniversalBGTask*", "$out\Pluto\*.pdb" -ErrorAction SilentlyContinue
Copy-Item "$out\hook\pluto-hook.exe" "$out\Pluto\"
Write-Host "Done: $out\Pluto"
