@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSPROJ=ClaudeUsageTray\ClaudeUsageTray.csproj"

:: ─────────────────────────────────────────────
:: 1. 현재 버전 읽기
:: ─────────────────────────────────────────────
set "CUR_VERSION="
for /f "tokens=*" %%L in ('findstr /i "<Version>" "%CSPROJ%"') do (
    set "LINE=%%L"
    set "LINE=!LINE:*<Version>=!"
    for /f "delims=<" %%V in ("!LINE!") do set "CUR_VERSION=%%V"
)
if "%CUR_VERSION%"=="" (
    echo [ERROR] csproj 에서 버전을 읽지 못했습니다.
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 2. 버전 파싱 (MAJOR.MINOR.PATCH)
:: ─────────────────────────────────────────────
for /f "tokens=1,2,3 delims=." %%A in ("%CUR_VERSION%") do (
    set "V_MAJOR=%%A"
    set "V_MINOR=%%B"
    set "V_PATCH=%%C"
)

echo.
echo ══════════════════════════════════════
echo  현재 버전: v%CUR_VERSION%
echo ══════════════════════════════════════
echo.
echo  버전 유형을 선택하세요:
echo    [1] patch   v%V_MAJOR%.%V_MINOR%.%V_PATCH% → v%V_MAJOR%.%V_MINOR%.
set /a NEXT_PATCH=%V_PATCH%+1
echo                                    v%V_MAJOR%.%V_MINOR%.!NEXT_PATCH!
set /a NEXT_MINOR=%V_MINOR%+1
echo    [2] minor   v%V_MAJOR%.%V_MINOR%.%V_PATCH% → v%V_MAJOR%.!NEXT_MINOR!.0
set /a NEXT_MAJOR=%V_MAJOR%+1
echo    [3] major   v%V_MAJOR%.%V_MINOR%.%V_PATCH% → v!NEXT_MAJOR!.0.0
echo    [4] 직접 입력
echo.
set /p "BUMP_TYPE=선택 (1/2/3/4): "

if "%BUMP_TYPE%"=="1" (
    set /a V_PATCH=%V_PATCH%+1
    set "NEW_VERSION=%V_MAJOR%.%V_MINOR%.!V_PATCH!"
) else if "%BUMP_TYPE%"=="2" (
    set /a V_MINOR=%V_MINOR%+1
    set "NEW_VERSION=%V_MAJOR%.!V_MINOR!.0"
) else if "%BUMP_TYPE%"=="3" (
    set /a V_MAJOR=%V_MAJOR%+1
    set "NEW_VERSION=!V_MAJOR!.0.0"
) else if "%BUMP_TYPE%"=="4" (
    set /p "NEW_VERSION=새 버전 입력 (예: 1.16.0): "
) else (
    echo [ERROR] 잘못된 선택입니다.
    pause & exit /b 1
)

set "TAG=v%NEW_VERSION%"
echo.
echo  %CUR_VERSION% → %NEW_VERSION% 으로 릴리즈합니다.
echo.
set /p "CONFIRM=계속하시겠습니까? (Y/N): "
if /i not "%CONFIRM%"=="Y" (
    echo 취소되었습니다.
    pause & exit /b 0
)

:: ─────────────────────────────────────────────
:: 3. 이미 릴리즈된 태그인지 확인
:: ─────────────────────────────────────────────
gh release view %TAG% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [ERROR] %TAG% 릴리즈가 이미 존재합니다.
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 4. csproj 버전 업데이트
:: ─────────────────────────────────────────────
echo.
echo [1/5] 버전 업데이트: %CUR_VERSION% → %NEW_VERSION%
python -c "
import re, sys
cur, new = sys.argv[1], sys.argv[2]
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', encoding='utf-8') as f:
    content = f.read()
content = re.sub(r'<Version>.*?</Version>', f'<Version>{new}</Version>', content)
content = re.sub(r'<AssemblyVersion>.*?</AssemblyVersion>', f'<AssemblyVersion>{new}</AssemblyVersion>', content)
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', 'w', encoding='utf-8') as f:
    f.write(content)
print('  csproj 업데이트 완료')
" "%CUR_VERSION%" "%NEW_VERSION%"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] csproj 버전 업데이트 실패
    pause & exit /b 1
)

:: CHANGELOG.md 에 새 버전 섹션 헤더 추가 (없을 경우)
python -c "
import sys
from datetime import date

ver = sys.argv[1]
today = date.today().strftime('%Y-%m-%d')
header = f'## [{ver}] - {today}'

with open('CHANGELOG.md', encoding='utf-8') as f:
    content = f.read()

if header in content:
    print(f'  CHANGELOG: {header} 이미 존재함, 건너뜀')
else:
    # --- 다음 줄에 삽입
    content = content.replace('---\n\n## [', f'---\n\n{header}\n\n<!-- ko -->\n### 수정\n- \n<!-- /ko -->\n\n<!-- en -->\n### Fixed\n- \n<!-- /en -->\n\n---\n\n## [', 1)
    with open('CHANGELOG.md', 'w', encoding='utf-8') as f:
        f.write(content)
    print(f'  CHANGELOG: {header} 섹션 추가됨')
    print('  ⚠  릴리즈 노트를 작성한 후 다시 release.bat 을 실행하세요.')
    print('  ⚠  (CHANGELOG.md 의 새 섹션을 채워주세요)')
    sys.exit(2)
" "%NEW_VERSION%"

if %ERRORLEVEL% EQU 2 (
    echo.
    echo CHANGELOG.md 를 열겠습니다. 내용 작성 후 저장하고 release.bat 을 다시 실행하세요.
    start notepad CHANGELOG.md
    pause & exit /b 0
)
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] CHANGELOG 업데이트 실패
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 5. Publish (single-file exe)
:: ─────────────────────────────────────────────
echo.
echo [2/5] Publishing...
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj ^
    -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -o publish/ --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish 실패
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 6. Git commit + tag + push
:: ─────────────────────────────────────────────
echo.
echo [3/5] Git commit ^& tag...
git add -A
git diff --cached --quiet
if %ERRORLEVEL% NEQ 0 (
    git commit -m "chore: bump version to %NEW_VERSION%"
    if %ERRORLEVEL% NEQ 0 (
        echo [ERROR] Git commit 실패
        pause & exit /b 1
    )
)

git tag %TAG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Git tag 실패
    pause & exit /b 1
)

echo.
echo [4/5] Git push...
git push origin master --tags
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Git push 실패
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 7. GitHub Release 생성
:: ─────────────────────────────────────────────
echo.
echo [5/5] GitHub Release 생성...

set "NOTES_FILE=%TEMP%\release_notes_%NEW_VERSION%.md"
python -c "
import re, sys

with open('CHANGELOG.md', encoding='utf-8') as f:
    content = f.read()

match = re.search(r'## \[' + re.escape(sys.argv[1]) + r'\].*?(?=\n## |\Z)', content, re.DOTALL)
if match:
    print(match.group().strip())
else:
    print('No release notes found.')
" "%NEW_VERSION%" > "%NOTES_FILE%"

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
