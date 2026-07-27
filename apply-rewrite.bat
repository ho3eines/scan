@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Applies scan-rewrite.patch from this folder to the current Git repository.
REM Usage:
REM   apply-rewrite.bat              - apply patch, commit locally, ask before push
REM   apply-rewrite.bat main         - checkout/pull main first, then apply
REM   apply-rewrite.bat main push    - checkout/pull main first, apply, commit, and push
REM
REM The patch is applied with Git's 3-way merge mode:
REM   git apply --3way --index --ignore-whitespace scan-rewrite.patch

set "SCRIPT_DIR=%~dp0"
set "PATCH_FILE=%SCRIPT_DIR%scan-rewrite.patch"
set "TARGET_BRANCH=%~1"
set "PUSH_MODE=%~2"
set "COMMIT_MESSAGE=Apply complete scan-system rewrite"

if not exist "%PATCH_FILE%" (
    echo ERROR: Patch file not found: "%PATCH_FILE%"
    exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo ERROR: This script must be run inside a Git repository.
    exit /b 1
)

for /f "delims=" %%R in ('git rev-parse --show-toplevel') do set "REPO_ROOT=%%R"
cd /d "%REPO_ROOT%" || exit /b 1

if not "%TARGET_BRANCH%"=="" (
    echo Switching to %TARGET_BRANCH% ...
    git checkout "%TARGET_BRANCH%" || exit /b 1
    echo Pulling latest %TARGET_BRANCH% ...
    git pull --ff-only origin "%TARGET_BRANCH%" || exit /b 1
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
echo Repository: %REPO_ROOT%
echo Branch:     %CURRENT_BRANCH%
echo Patch:      %PATCH_FILE%
echo.

git diff --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Working tree has unstaged changes. Commit or stash them first.
    git status --short
    exit /b 1
)

git diff --cached --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Index has staged changes. Commit or reset them first.
    git status --short
    exit /b 1
)

echo Checking whether patch is already applied ...
git apply --reverse --check --ignore-whitespace "%PATCH_FILE%" >nul 2>&1
if not errorlevel 1 (
    echo Patch already appears to be applied. No changes made.
    git status --short
    exit /b 0
)

echo Applying patch with 3-way merge ...
git apply --3way --index --ignore-whitespace "%PATCH_FILE%"
if errorlevel 1 (
    echo.
    echo ERROR: Patch apply failed. Resolve conflicts if Git created any, then run:
    echo   git add -A
    echo   git commit -m "%COMMIT_MESSAGE%"
    exit /b 1
)

git status --short

git diff --cached --quiet --ignore-submodules --
if not errorlevel 1 (
    echo Patch applied but there are no staged changes. Nothing to commit.
    exit /b 0
)

echo Creating commit ...
git commit -m "%COMMIT_MESSAGE%" || exit /b 1

if /i "%PUSH_MODE%"=="push" goto PUSH_NOW
if /i "%PUSH_MODE%"=="--push" goto PUSH_NOW
if /i "%PUSH_MODE%"=="/push" goto PUSH_NOW

echo.
choice /C YN /N /M "Push %CURRENT_BRANCH% to origin now? [Y/N] "
if errorlevel 2 goto DONE

:PUSH_NOW
if "%CURRENT_BRANCH%"=="" (
    echo ERROR: Cannot push because the current HEAD is detached.
    exit /b 1
)
echo Pushing %CURRENT_BRANCH% ...
git push origin "%CURRENT_BRANCH%" || exit /b 1

:DONE
echo Done.
exit /b 0
