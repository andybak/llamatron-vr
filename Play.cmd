@echo off
cd /d "%~dp0"
if not exist "%~dp0.tools\python\python.exe" (
  echo Run Setup.cmd first.
  pause
  exit /b 1
)
if not exist "%~dp0artifacts\install.json" (
  echo Setup has not completed. Run Setup.cmd first.
  pause
  exit /b 1
)
"%~dp0.tools\python\python.exe" "%~dp0tools\launch_native.py" %*
set "playResult=%errorlevel%"
if not "%playResult%"=="0" pause
exit /b %playResult%
