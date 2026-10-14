@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\register-file-associations.ps1" -OpenSettings >nul
if errorlevel 1 pause
