@echo off
setlocal
cd /d "%~dp0"
if exist "IPA-Desktop.exe" goto launch
call Build.cmd
if errorlevel 1 exit /b 1
:launch
start "" "%~dp0IPA-Desktop.exe"
