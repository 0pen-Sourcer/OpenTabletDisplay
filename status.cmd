@echo off
setlocal
powershell.exe -NoProfile -File "%~dp0installer\status.ps1" %*
pause
exit /b %errorlevel%
