@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSPROJ=ClaudeUsageTray\ClaudeUsageTray.csproj"
set "AUTO_BUMP=%~1"
set "SKIP_CONFIRM=%~2"

:: ─────────────────────────────────────────────
:: 1. 현재 버전 읽기
:: ─────────────────────────────────────────────
set "CUR_VERSION="
for /f "tokens=*" %%L in ('findstr /i "<Version>" "%CSPROJ%"') do (
    set "LINE=%%L"
    set "LINE=!LINE:*<Version>=!"
    for /f "delims=<" %%V in ("!LINE!") do set "CUR_VERSION=%%V"
)

:: ─────────────────────────────────────────────
:: 2. 버전 파싱
:: ─────────────────────────────────────────────
for /f "tokens=1,2,3 delims=." %%A in ("%CUR_VERSION%") do (
    set "V_MAJOR=%%A"
    set "V_MINOR=%%B"
    set "V_PATCH=%%C"
)

if "!AUTO_BUMP!"=="" (
    echo.
    echo  현재 버전: v%CUR_VERSION%
    echo.
    echo  버전 유형 선택: [1] patch, [2] minor, [3] major, [4] 직접 입력
    set /p "BUMP_TYPE=선택 (1/2/3/4): "
) else (
    if /i "!AUTO_BUMP!"=="patch" set "BUMP_TYPE=1"
    if /i "!AUTO_BUMP!"=="minor" set "BUMP_TYPE=2"
    if /i "!AUTO_BUMP!"=="major" set "BUMP_TYPE=3"
)

if "%BUMP_TYPE%"=="1" (
    set /a V_PATCH=%V_PATCH%+1
    set "NEW_VERSION=%V_MAJOR%.%V_MINOR%.!V_PATCH!"
) else if "%BUMP_TYPE%"=="2" (
    set /a V_MINOR=%V_MINOR%+1
    set "NEW_VERSION=%V_MAJOR%.!V_MINOR!.0"
) else if "%BUMP_TYPE%"=="3" (
    set /a V_MAJOR=%V_MAJOR%+1
    set "NEW_VERSION=!V_MAJOR!.0.0"
) else (
    if "!AUTO_BUMP!"=="" (
        set /p "NEW_VERSION=새 버전 입력: "
    ) else (
        set "NEW_VERSION=!AUTO_BUMP!"
    )
)

set "TAG=v%NEW_VERSION%"
echo  %CUR_VERSION% -^> %NEW_VERSION% 릴리즈 준비 중...

if /i not "%SKIP_CONFIRM%"=="--yes" (
    set /p "CONFIRM=계속하시겠습니까? (Y/N): "
    if /i not "!CONFIRM!"=="Y" exit /b 0
)

:: ─────────────────────────────────────────────
:: 3. 버전 업데이트 (Python 활용)
:: ─────────────────────────────────────────────
python -c "
import re, sys
cur, new = sys.argv[1], sys.argv[2]
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', encoding='utf-8') as f:
    content = f.read()
content = re.sub(r'<Version>.*?</Version>', f'<Version>{new}</Version>', content)
content = re.sub(r'<AssemblyVersion>.*?</AssemblyVersion>', f'<AssemblyVersion>{new}</AssemblyVersion>', content)
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', \'w\', encoding=\'utf-8\') as f:
    f.write(content)
" "%CUR_VERSION%" "%NEW_VERSION%"

:: ─────────────────────────────────────────────
:: 4. Git 작업
:: ─────────────────────────────────────────────
git add -A
git commit -m "chore: bump version to %NEW_VERSION%"
git tag %TAG%
:: 모든 태그(--tags) 대신 현재 태그만 명시적으로 푸시
git push origin master %TAG%

echo.
echo 릴리즈 완료! GitHub Actions 확인: https://github.com/jeiel85/claude-usage-tray-windows/actions
timeout /t 5
