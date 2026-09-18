@echo off
setlocal
REM VSCodeRecent one-shot dev entry point (ASCII only on purpose: cmd.exe reads
REM .cmd files in the OEM codepage, so non-ASCII comments would be mojibake).
REM
REM This wrapper works around two Windows-side gotchas:
REM   1. cmd.exe cannot use a UNC path (\\wsl.localhost\...) as the current
REM      directory, so pushd maps a temporary drive letter first.
REM   2. Running a .ps1 from a UNC path is blocked by the execution policy, so
REM      -ExecutionPolicy Bypass is applied to this single invocation only.
REM      The system execution policy is not changed.
REM
REM Arguments are identical to dev.ps1:
REM   dev.cmd
REM   dev.cmd -Configuration Release
REM   dev.cmd -Platform ARM64
REM   dev.cmd -Stop
REM   dev.cmd -Uninstall

pushd "%~dp0"

REM Prefer PowerShell 7 (pwsh); fall back to Windows PowerShell.
where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
)

set RC=%ERRORLEVEL%
popd
exit /b %RC%
