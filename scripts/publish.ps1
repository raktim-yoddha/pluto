# Builds a release folder and zip: the pet (self-contained, one exe) plus the NativeAOT hook relay.
# NativeAOT needs the "Desktop development with C++" workload of Visual Studio (or its Build Tools).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\HiveoryPet.Hook" -c Release -o "$out\hook"
dotnet publish "$root\src\HiveoryPet" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$out\HiveoryPet"
Copy-Item "$out\hook\hiveory-pet-hook.exe" "$out\HiveoryPet\"
Compress-Archive -Path "$out\HiveoryPet\*" -DestinationPath "$out\HiveoryPet-win-x64.zip" -Force
Write-Host "Done: $out\HiveoryPet-win-x64.zip"
