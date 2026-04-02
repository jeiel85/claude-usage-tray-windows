@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSPROJ=ClaudeUsageTray\ClaudeUsageTray.csproj"

echo.
echo ======================================
echo  Claude Usage Tray - Release Helper
echo ======================================

:: 1. Commit and push code changes
echo.
echo -- [1/6] Git status check --
git fetch origin >nul 2>&1

for /f %%C in ('git status --porcelain ^| find /c /v ""') do set "DIRTY_COUNT=%%C"

if not "%DIRTY_COUNT%"=="0" (
    echo   %DIRTY_COUNT% uncommitted change(s) found:
    echo.
    git status --short
    echo.
    set /p "DO_COMMIT=Commit and push now? (Y/N): "
    if /i "!DO_COMMIT!"=="Y" (
        set /p "COMMIT_MSG=Commit message: "
        if "!COMMIT_MSG!"=="" set "COMMIT_MSG=chore: update"
        git add -A
        git commit -m "!COMMIT_MSG!"
        if !ERRORLEVEL! NEQ 0 ( echo [ERROR] Git commit failed & pause & exit /b 1 )
        git push origin master
        if !ERRORLEVEL! NEQ 0 ( echo [ERROR] Git push failed & pause & exit /b 1 )
        echo   [OK] Committed and pushed
    ) else (
        echo   [!] Uncommitted changes will be included in the release commit.
    )
) else (
    echo   [OK] Working directory clean
    for /f %%N in ('git rev-list --count origin/master..HEAD 2^>nul') do set "AHEAD=%%N"
    if not "!AHEAD!"=="0" (
        echo   [!] !AHEAD! unpushed commit(s)
        set /p "DO_PUSH=Push now? (Y/N): "
        if /i "!DO_PUSH!"=="Y" (
            git push origin master
            if !ERRORLEVEL! NEQ 0 ( echo [ERROR] Git push failed & pause & exit /b 1 )
            echo   [OK] Pushed
        )
    ) else (
        echo   [OK] In sync with origin/master
    )
)

:: 2. Read current version and select bump type
set "CUR_VERSION="
for /f "tokens=*" %%L in ('findstr /i "<Version>" "%CSPROJ%"') do (
    set "LINE=%%L"
    set "LINE=!LINE:*<Version>=!"
    for /f "delims=<" %%V in ("!LINE!") do set "CUR_VERSION=%%V"
)
if "%CUR_VERSION%"=="" (
    echo [ERROR] Could not read version from csproj.
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
echo -- [2/6] Select version bump --
echo   Current: v%CUR_VERSION%
echo.
echo   [1] patch  ->  v%V_MAJOR%.%V_MINOR%.!NEXT_PATCH!
echo   [2] minor  ->  v%V_MAJOR%.!NEXT_MINOR!.0
echo   [3] major  ->  v!NEXT_MAJOR!.0.0
echo   [4] custom input
echo.
set /p "BUMP_TYPE=Select (1/2/3/4): "

if "%BUMP_TYPE%"=="1" (
    set "NEW_VERSION=%V_MAJOR%.%V_MINOR%.!NEXT_PATCH!"
) else if "%BUMP_TYPE%"=="2" (
    set "NEW_VERSION=%V_MAJOR%.!NEXT_MINOR!.0"
) else if "%BUMP_TYPE%"=="3" (
    set "NEW_VERSION=!NEXT_MAJOR!.0.0"
) else if "%BUMP_TYPE%"=="4" (
    set /p "NEW_VERSION=Enter version (e.g. 1.16.0): "
) else (
    echo [ERROR] Invalid selection.
    pause & exit /b 1
)

set "TAG=v%NEW_VERSION%"

echo.
echo   %CUR_VERSION%  ->  %NEW_VERSION%
echo.
set /p "CONFIRM=Proceed with release? (Y/N): "
if /i not "%CONFIRM%"=="Y" (
    echo Cancelled.
    pause & exit /b 0
)

gh release view %TAG% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [ERROR] Release %TAG% already exists.
    pause & exit /b 1
)

:: 3. Update csproj version + CHANGELOG
echo.
echo -- [3/6] Version update --
python -c "import re,sys;new=sys.argv[1];f=open('ClaudeUsageTray/ClaudeUsageTray.csproj',encoding='utf-8');c=f.read();f.close();c=re.sub(r'<Version>.*?</Version>',f'<Version>{new}</Version>',c);c=re.sub(r'<AssemblyVersion>.*?</AssemblyVersion>',f'<AssemblyVersion>{new}</AssemblyVersion>',c);f=open('ClaudeUsageTray/ClaudeUsageTray.csproj','w',encoding='utf-8');f.write(c);f.close();print(f'  csproj -> {new}')" "%NEW_VERSION%"
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] csproj update failed & pause & exit /b 1 )

python -c "import re,sys;from datetime import date;ver=sys.argv[1];today=date.today().strftime('%%Y-%%m-%%d');header=f'## [{ver}] - {today}';f=open('CHANGELOG.md',encoding='utf-8');c=f.read();f.close();(print(f'  CHANGELOG: {header} already exists'),sys.exit(0)) if header in c else (c.__setitem__(0,None),open('CHANGELOG.md','w',encoding='utf-8').write(c.replace('---\n\n## [',f'---\n\n{header}\n\n<!-- ko -->\n### \uc218\uc815\n- \n<!-- /ko -->\n\n<!-- en -->\n### Fixed\n- \n<!-- /en -->\n\n---\n\n## [',1)),print(f'  CHANGELOG: {header} added'),sys.exit(2))" "%NEW_VERSION%"

if %ERRORLEVEL% EQU 2 (
    echo.
    echo   Edit CHANGELOG.md with release notes, then run release.bat again.
    start notepad CHANGELOG.md
    pause & exit /b 0
)
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] CHANGELOG update failed & pause & exit /b 1 )

:: 4. Publish
echo.
echo -- [4/6] Publish --
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj ^
    -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -o publish/ --nologo
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Publish failed & pause & exit /b 1 )

:: 5. Git commit + tag + push
echo.
echo -- [5/6] Git commit / tag / push --
git add -A
git diff --cached --quiet
if %ERRORLEVEL% NEQ 0 (
    git commit -m "chore: bump version to %NEW_VERSION%"
    if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Git commit failed & pause & exit /b 1 )
)
git tag %TAG%
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Git tag failed & pause & exit /b 1 )
git push origin master --tags
if %ERRORLEVEL! NEQ 0 ( echo [ERROR] Git push failed & pause & exit /b 1 )
echo   [OK] Pushed

:: 6. GitHub Release
echo.
echo -- [6/6] GitHub Release --
set "NOTES_FILE=%TEMP%\release_notes_%NEW_VERSION%.md"
python -c "import re,sys;f=open('CHANGELOG.md',encoding='utf-8');c=f.read();f.close();m=re.search(r'## \['+re.escape(sys.argv[1])+r'\].*?(?=\n## |\Z)',c,re.DOTALL);print(m.group().strip() if m else 'No release notes.')" "%NEW_VERSION%" > "%NOTES_FILE%"

gh release create %TAG% publish\ClaudeUsageTray.exe --title "%TAG%" --notes-file "%NOTES_FILE%"
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] GitHub Release failed & pause & exit /b 1 )
del "%NOTES_FILE%" >nul 2>&1

echo.
echo ======================================
echo  Done!
echo  https://github.com/jeiel85/claude-usage-tray-windows/releases/tag/%TAG%
echo ======================================
pause
