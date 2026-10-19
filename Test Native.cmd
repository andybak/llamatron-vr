@echo off
cd /d "%~dp0"
rem Close previous native test instances after checking their full executable paths.
"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -Command "$testPaths = @('%~dp0native\build\Release\LlamatronNative.exe', '%~dp0native\build-vr\Release\LlamatronNative.exe'); Get-Process -Name LlamatronNative -ErrorAction SilentlyContinue | Where-Object { $_.Path -in $testPaths } | Stop-Process -ErrorAction Stop"
if not exist "%~dp0artifacts\native-xr-logs" mkdir "%~dp0artifacts\native-xr-logs"
set "VR_LOG_PATH=%~dp0artifacts\native-xr-logs"
set "XR_LOADER_DEBUG=error"
echo Starting native VR. Startup details: artifacts\native-user-test.log
"%~dp0native\build\Release\LlamatronNative.exe" --vr --control > "%~dp0artifacts\native-user-test.log" 2>&1
if errorlevel 1 pause
