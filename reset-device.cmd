@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\reset-device.ps1" %*
exit /b %errorlevel%
