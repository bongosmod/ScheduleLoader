@echo off
rem Builds the mods and prepares (or publishes) the online mod release. See publish-mods.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-mods.ps1"
echo.
pause
