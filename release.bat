@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ─────────────────────────────────────────────
:: 1. csproj 에서 버전 읽기
:: ─────────────────────────────────────────────
set "CSPROJ=ClaudeUsageTray\ClaudeUsageTray.csproj"
set "VERSION="
for /f "tokens=*" %%L in ('findstr /i "<Version>" "%CSPROJ%"') do (
    set "LINE=%%L"
    set "LINE=!LINE:*<Version>=!"
    for /f "delims=<" %%V in ("!LINE!") do set "VERSION=%%V"
)

if "%VERSION%"=="" (
    echo [ERROR] csproj 에서 버전을 읽지 못했습니다.
    pause & exit /b 1
)

set "TAG=v%VERSION%"
echo.
echo ══════════════════════════════════════
echo  Release: %TAG%
echo ══════════════════════════════════════

:: ─────────────────────────────────────────────
:: 2. 이미 릴리즈된 태그인지 확인
:: ─────────────────────────────────────────────
gh release view %TAG% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [ERROR] %TAG% 릴리즈가 이미 존재합니다.
    echo         csproj 버전을 올린 후 다시 실행하세요.
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 3. Publish (single-file exe)
:: ─────────────────────────────────────────────
echo.
echo [1/4] Publishing...
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj ^
    -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -o publish/ --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish 실패
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 4. Git commit + tag + push
:: ─────────────────────────────────────────────
echo.
echo [2/4] Git commit ^& tag...
git add -A
git status --short
echo.

:: 변경사항이 없으면 커밋 건너뜀
git diff --cached --quiet
if %ERRORLEVEL% NEQ 0 (
    git commit -m "chore: bump version to %VERSION%"
    if %ERRORLEVEL% NEQ 0 (
        echo [ERROR] Git commit 실패
        pause & exit /b 1
    )
)

git tag %TAG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Git tag 실패 (태그가 이미 있을 수 있음)
    pause & exit /b 1
)

echo.
echo [3/4] Git push...
git push origin master --tags
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Git push 실패
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 5. GitHub Release 생성
:: ─────────────────────────────────────────────
echo.
echo [4/4] GitHub Release 생성...

:: CHANGELOG.md 에서 현재 버전 섹션 추출
set "NOTES_FILE=%TEMP%\release_notes_%VERSION%.md"
python -c "
import re, sys

with open('CHANGELOG.md', encoding='utf-8') as f:
    content = f.read()

# 현재 버전 섹션만 추출 (다음 ## 까지)
pattern = r'## \[%VERSION%\].*?(?=\n## |\Z)'
match = re.search(pattern.replace('%VERSION%', sys.argv[1]), content, re.DOTALL)
if match:
    print(match.group().strip())
else:
    print('No release notes found.')
" "%VERSION%" > "%NOTES_FILE%"

gh release create %TAG% ^
    publish\ClaudeUsageTray.exe ^
    --title "%TAG%" ^
    --notes-file "%NOTES_FILE%"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] GitHub Release 생성 실패
    pause & exit /b 1
)

del "%NOTES_FILE%" >nul 2>&1

echo.
echo ══════════════════════════════════════
echo  Done! https://github.com/jeiel85/claude-usage-tray-windows/releases/tag/%TAG%
echo ══════════════════════════════════════
pause
