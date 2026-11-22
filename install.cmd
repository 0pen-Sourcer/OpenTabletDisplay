@echo off
setlocal
if not exist "%~dp0installer\install.ps1" (
    echo ERROR: The installer script is missing. Extract the complete release ZIP before running install.cmd.
    set "installExitCode=1"
    goto done
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\install.ps1" %*
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
