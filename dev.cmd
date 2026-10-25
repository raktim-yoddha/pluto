@echo off
rem Pluto dev build in one step: closes a running Pluto (only one can run), restores packages, then builds the
rem latest code and starts it. Works from any folder, in cmd or PowerShell.
taskkill /IM Pluto.exe /F >nul 2>&1
dotnet restore "%~dp0Pluto.slnx" || exit /b 1
dotnet run --project "%~dp0src\Pluto\Pluto.csproj" %*
