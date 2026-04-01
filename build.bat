@echo off
cd /d "%~dp0"
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build succeeded. Run: ClaudeUsageTray\bin\Release\net8.0-windows\ClaudeUsageTray.exe
)
pause
