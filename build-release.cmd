@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1 || (echo .NET 10 SDK is required.& exit /b 1)
dotnet publish UnionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false
if errorlevel 1 exit /b %errorlevel%
echo Built: bin\Release\net10.0-windows\win-x64\publish\UnionLauncher.exe
echo Copy the EXE to a permanent trusted location BEFORE registering union://
