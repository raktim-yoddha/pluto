# Builds out/HiveoryPet-win-x64.zip: the pet (WinUI 3, self-contained: no .NET or Windows App SDK install needed)
# plus the NativeAOT hook relay. NativeAOT needs Visual Studio's "Desktop development with C++" workload.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\HiveoryPet.Hook" -c Release -o "$out\hook"
dotnet publish "$root\src\HiveoryPet" -c Release -p:SelfContained=true -o "$out\HiveoryPet"
Remove-Item "$out\HiveoryPet\hiveory-pet-hook.*" -ErrorAction SilentlyContinue
Copy-Item "$out\hook\hiveory-pet-hook.exe" "$out\HiveoryPet\"
Compress-Archive -Path "$out\HiveoryPet\*" -DestinationPath "$out\HiveoryPet-win-x64.zip" -Force
Write-Host "Done: $out\HiveoryPet-win-x64.zip"
