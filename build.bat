@echo off
cd /d "%~dp0"

echo Building Main App...
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    pause & exit /b 1
)

echo Building Updater...
dotnet build ClaudeUsageTray.Updater/ClaudeUsageTray.Updater.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo Updater build failed.
    pause & exit /b 1
)

:: Copy Updater to Main App output directory for testing
set "MAIN_OUT=ClaudeUsageTray\bin\Release\net9.0-windows10.0.17763.0"
copy /y "ClaudeUsageTray.Updater\bin\Release\net9.0-windows\ClaudeUsageTray-Updater.exe" "%MAIN_OUT%\"

echo.
echo Build succeeded. Launching...
taskkill /f /im ClaudeUsageTray.exe >nul 2>&1
start "" "%MAIN_OUT%\ClaudeUsageTray.exe"
pause
