@echo off
setlocal
if not exist "%~dp0installer\install.ps1" (
    echo ERROR: The installer script is missing. Extract the complete release ZIP before running install.cmd.
    set "installExitCode=1"
    goto done
)
if not exist "%~dp0payload\FSDL.exe" (
    echo ERROR: Windows runtime payload missing. Download the packaged ZIP from Releases, not GitHub's Source code ZIP.
    set "installExitCode=1"
    goto done
)
if not exist "%~dp0payload\probes" (
    echo ERROR: Android probe payload missing. Re-extract the complete release ZIP.
    set "installExitCode=1"
    goto done
)
powershell.exe -NoProfile -File "%~dp0installer\install.ps1" %*
set "installExitCode=%errorlevel%"
:done
if not "%installExitCode%"=="0" (
    echo.
    echo OpenTabletDisplay installation failed with exit code %installExitCode%.
    echo If created, check "%LOCALAPPDATA%\FSDL\logs\install.log" for details.
)
echo.
if /i not "%OPEN_TABLET_DISPLAY_NO_PAUSE%"=="1" pause
exit /b %installExitCode%
