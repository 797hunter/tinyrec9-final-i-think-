@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0build.log"
echo TinyRec 9 build - %date% %time% > "%LOG%"
echo PATH was: %PATH% >> "%LOG%"
set "PATH=%SystemRoot%\System32;%SystemRoot%;%SystemRoot%\System32\WindowsPowerShell\v1.0;%ProgramFiles%\dotnet;%LocalAppData%\Microsoft\dotnet;%PATH%"

rem ---- find a .NET SDK (8 or newer); install one into your user folder if there isn't one (no admin needed)
set "DN=dotnet"
call :hassdk
if errorlevel 1 (
  set "DN=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
  call :hassdk
)
if errorlevel 1 (
  echo Installing the .NET 8 SDK into your user folder - one time, a few minutes...
  echo Installing .NET 8 SDK >> "%LOG%"
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_dotnet.ps1" >> "%LOG%" 2>&1
  set "DN=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
  call :hassdk
)
if errorlevel 1 (
  echo NO SDK >> "%LOG%"
  echo Couldn't find or install the .NET SDK - see build.log
  goto end
)
echo Using: %DN% >> "%LOG%"
"%DN%" --version >> "%LOG%" 2>&1

echo Building TinyRec9.exe...
"%DN%" publish App\TinyRec9.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo Build failed - see build.log
) else (
  echo BUILD OK >> "%LOG%"
  echo Done: dist\TinyRec9.exe
)
:end
if /i not "%~1"=="auto" pause
exit /b 0

:hassdk
"%DN%" --list-sdks > "%TEMP%\tinyrec_sdks.txt" 2>nul
findstr /r /c:"^[89]\." /c:"^1[0-9]\." "%TEMP%\tinyrec_sdks.txt" >nul 2>nul
exit /b %errorlevel%
