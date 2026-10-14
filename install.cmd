@echo off
rem Installs Seneschal from a Command Prompt. It hands over to install.ps1, so the options are the same: install.cmd -Version 1.0.5
rem Without install.ps1 beside it, it downloads the newest one from GitHub
setlocal
if exist "%~dp0install.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1 | iex"
)
exit /b %errorlevel%
