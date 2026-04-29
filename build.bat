@echo off
:: WARNING: 에이전트는 로컬 빌드를 수행하지 마세요. (GitHub Actions 사용)
cd /d "%~dp0"

echo Building Main App...
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    pause & exit /b 1
)

echo.
echo Build succeeded. Launching...
:: 실제 빌드 경로에 맞게 수정 (net9.0-windows)
set "MAIN_OUT=ClaudeUsageTray\bin\Release\net9.0-windows"
taskkill /f /im ClaudeUsageTray.exe >nul 2>&1
start "" "%MAIN_OUT%\ClaudeUsageTray.exe"

:: 3초 대기 후 자동 종료 (사용자가 키를 누르면 즉시 종료)
timeout /t 3
