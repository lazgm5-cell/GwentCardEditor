@echo off
setlocal
if not exist "%~dp0publish-0.3.109\GwentCardEditor.exe" (
  echo GwentCardEditor.exe not found. Run Build.ps1 first.
  pause
  exit /b 1
)
if "%~1"=="" (
  start "" "%~dp0publish-0.3.109\GwentCardEditor.exe"
) else (
  start "" "%~dp0publish-0.3.109\GwentCardEditor.exe" %*
)
