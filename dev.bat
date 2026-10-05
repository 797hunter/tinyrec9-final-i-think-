@echo off
rem dev.bat - close TinyRec9, rebuild it into dist\, then start it. Used while testing.
setlocal
cd /d "%~dp0"
set "PATH=%SystemRoot%\System32;%SystemRoot%;%ProgramFiles%\dotnet;%LocalAppData%\Microsoft\dotnet;%PATH%"
taskkill /im TinyRec9.exe /f >nul 2>nul
set "DN=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
if not exist "%DN%" set "DN=dotnet"
echo dev build - %date% %time% > build.log
"%DN%" publish App\TinyRec9.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist >> build.log 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> build.log
  exit /b 1
)
echo BUILD OK >> build.log
start "" "%~dp0dist\TinyRec9.exe"
exit /b 0
