@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\status.ps1" %*
pause
exit /b %errorlevel%
