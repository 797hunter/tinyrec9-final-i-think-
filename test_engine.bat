@echo off
cd /d "%~dp0"
set "PATH=%SystemRoot%\System32;%SystemRoot%;%ProgramFiles%\dotnet;%LocalAppData%\Microsoft\dotnet;%PATH%"
set "DN=dotnet"
"%DN%" --list-sdks >nul 2>nul || set "DN=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
"%DN%" run --project Core.Tests -c Release > "%~dp0test.log" 2>&1
if /i not "%~1"=="auto" (type "%~dp0test.log" & pause)
