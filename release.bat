@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSPROJ=ClaudeUsageTray\ClaudeUsageTray.csproj"

echo.
echo ══════════════════════════════════════
echo  Claude Usage Tray — Release Helper
echo ══════════════════════════════════════

:: ─────────────────────────────────────────────
:: 1. 코드 변경사항 커밋 및 push
:: ─────────────────────────────────────────────
echo.
echo ── [1/6] Git 상태 확인 ─────────────────
git fetch origin >nul 2>&1

for /f %%C in ('git status --porcelain ^| find /c /v ""') do set "DIRTY_COUNT=%%C"

if not "%DIRTY_COUNT%"=="0" (
    echo  변경사항 %DIRTY_COUNT%개 발견:
    echo.
    git status --short
    echo.
    set /p "DO_COMMIT=커밋하고 push 하시겠습니까? (Y/N): "
    if /i "!DO_COMMIT!"=="Y" (
        set /p "COMMIT_MSG=커밋 메시지: "
        if "!COMMIT_MSG!"=="" set "COMMIT_MSG=chore: update"
        git add -A
        git commit -m "!COMMIT_MSG!"
        if !ERRORLEVEL! NEQ 0 (
            echo [ERROR] Git commit 실패
            pause & exit /b 1
        )
        git push origin master
        if !ERRORLEVEL! NEQ 0 (
            echo [ERROR] Git push 실패
            pause & exit /b 1
        )
        echo  [OK] 커밋 및 push 완료
    ) else (
        echo  [!] 미커밋 변경사항은 릴리즈 커밋에 포함됩니다.
    )
) else (
    echo  [OK] 워킹 디렉토리 깨끗함

    :: push 안 된 커밋 확인
    for /f %%N in ('git rev-list --count origin/master..HEAD 2^>nul') do set "AHEAD=%%N"
    if not "!AHEAD!"=="0" (
        echo  [!] push 안 된 커밋 !AHEAD!개 있음
        set /p "DO_PUSH=지금 push 하시겠습니까? (Y/N): "
        if /i "!DO_PUSH!"=="Y" (
            git push origin master
            if !ERRORLEVEL! NEQ 0 (
                echo [ERROR] Git push 실패
                pause & exit /b 1
            )
            echo  [OK] Push 완료
        )
    ) else (
        echo  [OK] origin/master 와 동기화됨
    )
)

:: ─────────────────────────────────────────────
:: 2. 현재 버전 읽기 및 bump 선택
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

for /f "tokens=1,2,3 delims=." %%A in ("%CUR_VERSION%") do (
    set "V_MAJOR=%%A"
    set "V_MINOR=%%B"
    set "V_PATCH=%%C"
)
set /a NEXT_PATCH=%V_PATCH%+1
set /a NEXT_MINOR=%V_MINOR%+1
set /a NEXT_MAJOR=%V_MAJOR%+1

echo.
echo ── [2/6] 버전 선택 ─────────────────────
echo  현재: v%CUR_VERSION%
echo.
echo    [1] patch  →  v%V_MAJOR%.%V_MINOR%.!NEXT_PATCH!
echo    [2] minor  →  v%V_MAJOR%.!NEXT_MINOR!.0
echo    [3] major  →  v!NEXT_MAJOR!.0.0
echo    [4] 직접 입력
echo.
set /p "BUMP_TYPE=선택 (1/2/3/4): "

if "%BUMP_TYPE%"=="1" (
    set "NEW_VERSION=%V_MAJOR%.%V_MINOR%.!NEXT_PATCH!"
) else if "%BUMP_TYPE%"=="2" (
    set "NEW_VERSION=%V_MAJOR%.!NEXT_MINOR!.0"
) else if "%BUMP_TYPE%"=="3" (
    set "NEW_VERSION=!NEXT_MAJOR!.0.0"
) else if "%BUMP_TYPE%"=="4" (
    set /p "NEW_VERSION=새 버전 입력 (예: 1.16.0): "
) else (
    echo [ERROR] 잘못된 선택입니다.
    pause & exit /b 1
)

set "TAG=v%NEW_VERSION%"

echo.
echo  %CUR_VERSION%  →  %NEW_VERSION%
echo.
set /p "CONFIRM=릴리즈 진행하시겠습니까? (Y/N): "
if /i not "%CONFIRM%"=="Y" (
    echo 취소되었습니다.
    pause & exit /b 0
)

:: 이미 존재하는 태그 확인
gh release view %TAG% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [ERROR] %TAG% 릴리즈가 이미 존재합니다.
    pause & exit /b 1
)

:: ─────────────────────────────────────────────
:: 3. csproj 버전 업데이트 + CHANGELOG 확인
:: ─────────────────────────────────────────────
echo.
echo ── [3/6] 버전 업데이트 ─────────────────
python -c "
import re, sys
new = sys.argv[1]
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', encoding='utf-8') as f:
    content = f.read()
content = re.sub(r'<Version>.*?</Version>', f'<Version>{new}</Version>', content)
content = re.sub(r'<AssemblyVersion>.*?</AssemblyVersion>', f'<AssemblyVersion>{new}</AssemblyVersion>', content)
with open('ClaudeUsageTray/ClaudeUsageTray.csproj', 'w', encoding='utf-8') as f:
    f.write(content)
print(f'  csproj → {new}')
" "%NEW_VERSION%"
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] csproj 업데이트 실패 & pause & exit /b 1 )

python -c "
import re, sys
from datetime import date
ver = sys.argv[1]
today = date.today().strftime('%Y-%m-%d')
header = f'## [{ver}] - {today}'
with open('CHANGELOG.md', encoding='utf-8') as f:
    content = f.read()
if header in content:
    print(f'  CHANGELOG: {header} 이미 존재함')
else:
    content = content.replace('---\n\n## [', f'---\n\n{header}\n\n<!-- ko -->\n### 수정\n- \n<!-- /ko -->\n\n<!-- en -->\n### Fixed\n- \n<!-- /en -->\n\n---\n\n## [', 1)
    with open('CHANGELOG.md', 'w', encoding='utf-8') as f:
        f.write(content)
    print(f'  CHANGELOG: {header} 섹션 추가됨')
    sys.exit(2)
" "%NEW_VERSION%"

if %ERRORLEVEL% EQU 2 (
    echo.
    echo  CHANGELOG.md 에 릴리즈 노트를 작성해주세요.
    echo  저장 후 release.bat 을 다시 실행하세요.
    start notepad CHANGELOG.md
    pause & exit /b 0
)
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] CHANGELOG 업데이트 실패 & pause & exit /b 1 )

:: ─────────────────────────────────────────────
:: 4. Publish
:: ─────────────────────────────────────────────
echo.
echo ── [4/6] Publish ───────────────────────
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj ^
    -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -o publish/ --nologo
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Publish 실패 & pause & exit /b 1 )

:: ─────────────────────────────────────────────
:: 5. Git commit + tag + push
:: ─────────────────────────────────────────────
echo.
echo ── [5/6] Git commit / tag / push ───────
git add -A
git diff --cached --quiet
if %ERRORLEVEL% NEQ 0 (
    git commit -m "chore: bump version to %NEW_VERSION%"
    if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Git commit 실패 & pause & exit /b 1 )
)
git tag %TAG%
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Git tag 실패 & pause & exit /b 1 )
git push origin master --tags
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Git push 실패 & pause & exit /b 1 )
echo  [OK] push 완료

:: ─────────────────────────────────────────────
:: 6. GitHub Release
:: ─────────────────────────────────────────────
echo.
echo ── [6/6] GitHub Release ────────────────
set "NOTES_FILE=%TEMP%\release_notes_%NEW_VERSION%.md"
python -c "
import re, sys
with open('CHANGELOG.md', encoding='utf-8') as f:
    content = f.read()
match = re.search(r'## \[' + re.escape(sys.argv[1]) + r'\].*?(?=\n## |\Z)', content, re.DOTALL)
print(match.group().strip() if match else 'No release notes found.')
" "%NEW_VERSION%" > "%NOTES_FILE%"

gh release create %TAG% publish\ClaudeUsageTray.exe --title "%TAG%" --notes-file "%NOTES_FILE%"
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] GitHub Release 생성 실패 & pause & exit /b 1 )
del "%NOTES_FILE%" >nul 2>&1

echo.
echo ══════════════════════════════════════
echo  Done!
echo  https://github.com/jeiel85/claude-usage-tray-windows/releases/tag/%TAG%
echo ══════════════════════════════════════
pause
