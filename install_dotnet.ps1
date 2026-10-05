# Installs the .NET 8 SDK into %LOCALAPPDATA%\Microsoft\dotnet (your user folder - no admin rights needed).
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$script = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script
& $script -Channel 8.0 -InstallDir $dir -NoPath
