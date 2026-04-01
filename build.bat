@echo off
cd /d "%~dp0"
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build succeeded. Launching...
    taskkill /f /im ClaudeUsageTray.exe >nul 2>&1
    start "" "ClaudeUsageTray\bin\Release\net9.0-windows10.0.17763.0\ClaudeUsageTray.exe"
) else (
    echo.
    echo Build failed.
)
pause
