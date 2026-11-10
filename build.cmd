@echo off
setlocal
cd /d "%~dp0SystemMonitor"
set DOTNET_NOLOGO=1
dotnet publish -c Release -r win-x64 --self-contained true -o "%~dp0dist" %*
if errorlevel 1 pause
