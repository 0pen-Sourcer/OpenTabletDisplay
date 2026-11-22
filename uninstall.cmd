@echo off
setlocal
powershell.exe -NoProfile -File "%~dp0installer\uninstall.ps1" %*
exit /b %errorlevel%
