@echo off
cd /d "%~dp0"

echo Building Main App...
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    pause & exit /b 1
)

echo.
echo Build succeeded. Launching...
set "MAIN_OUT=ClaudeUsageTray\bin\Release\net9.0-windows10.0.17763.0"
taskkill /f /im ClaudeUsageTray.exe >nul 2>&1
start "" "%MAIN_OUT%\ClaudeUsageTray.exe"
pause
