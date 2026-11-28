@echo off
cd /d "%~dp0"
set NEED=1
dotnet --list-sdks 2>nul | findstr /b "8." >nul && set NEED=0

if "%NEED%"=="1" (
  echo Downloading the .NET SDK, please wait...
  winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
) else (
  echo .NET SDK found. Checking for a newer version...
  winget upgrade --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements >nul 2>&1
)

set DOTNET=dotnet
where dotnet >nul 2>nul
if errorlevel 1 set "DOTNET=C:\Program Files\dotnet\dotnet.exe"

echo Building Schedule Loader...
if exist dist rmdir /s /q dist
"%DOTNET%" publish Launcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist > publish.log 2>&1
if errorlevel 1 (
  type publish.log
  echo.
  echo FAILED. The full output is saved in publish.log
) else (
  echo.
  echo DONE. Your app is ONE file: dist\ScheduleLoader.exe
  echo Upload that file to a GitHub Release and people can just download and run it.
)

echo.
pause
