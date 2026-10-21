@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\setup.ps1" %*
if errorlevel 1 (
  echo Setup failed. See artifacts\setup.log for details.
  pause
  exit /b 1
)
echo Setup complete. Run Play.cmd to play.
pause
