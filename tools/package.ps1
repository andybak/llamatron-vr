param([ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version = 'local')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$packages = Join-Path $projectRoot 'artifacts/packages'
$name = "LlamatronVR-windows-x64-$Version"
$stage = Join-Path $packages $name
if (Test-Path -LiteralPath $stage) { throw "Package staging directory already exists: $stage. Use a new version." }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($folder in @('tools', '.tools/dosbox-x/mingw-build', '.tools/python', 'native/build/Release', 'third-party', 'docs')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage $folder) | Out-Null
}
foreach ($file in @('Setup.cmd', 'Play.cmd', 'Test Native.cmd', 'README.md')) {
    Copy-Item -LiteralPath $file -Destination $stage
}
foreach ($file in @('setup.ps1', 'game_session.py', 'launch_native.py', 'build_hook.py', 'inspect_game.py', 'capture_native_frame.py', 'debug_bridge.py', 'debug_command.py', 'ring_reader.py')) {
    Copy-Item -LiteralPath (Join-Path 'tools' $file) -Destination (Join-Path $stage 'tools')
}
Copy-Item -Path '.tools/python/*' -Destination (Join-Path $stage '.tools/python') -Recurse
Copy-Item -LiteralPath '.tools/dosbox-x/mingw-build/mingw-sdl2' -Destination (Join-Path $stage '.tools/dosbox-x/mingw-build') -Recurse
Copy-Item -LiteralPath '.tools/dosbox-x/COPYING' -Destination (Join-Path $stage 'third-party/DOSBox-X-COPYING.txt')
Copy-Item -LiteralPath 'native/vendor/openxr/LICENSE' -Destination (Join-Path $stage 'third-party/OpenXR-LICENSE.txt')
Copy-Item -LiteralPath 'native/README.md' -Destination (Join-Path $stage 'native/README.md')
Copy-Item -LiteralPath 'docs/native-effects.md' -Destination (Join-Path $stage 'docs/native-effects.md')
Copy-Item -LiteralPath 'native/build/Release/LlamatronNative.exe' -Destination (Join-Path $stage 'native/build/Release')

# The app is /MT, but the official embedded loader can use the MSVC shared CRT.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$runtimes = @(& $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC/Redist/MSVC/*/x64/Microsoft.VC143.CRT/msvcp140.dll')
if ($LASTEXITCODE -ne 0 -or -not $runtimes.Count) { throw 'Cannot find the redistributable MSVC runtime for packaging.' }
$crt = $runtimes | Sort-Object { [version](($_ -split '[\\/]')[-4]) } | Select-Object -Last 1
Copy-Item -Path (Join-Path (Split-Path $crt -Parent) '*.dll') -Destination (Join-Path $stage 'native/build/Release')
Copy-Item -Path (Join-Path (Split-Path $crt -Parent) '*.dll') -Destination (Join-Path $stage '.tools/python') -Force
@{ format = 1; build = $Version } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'bundle.json') -Encoding utf8
@'
This ZIP includes a native renderer with extracted Llamatron sprite assets.
It does not contain the original or patched DOS game executable. Setup requires
the user's separate copy of the original DOS Llamatron game.

Python 3.12.10: https://www.python.org/ftp/python/3.12.10/
License: .tools/python/LICENSE.txt
Pillow 11.3.0: https://pypi.org/project/pillow/11.3.0/
Licenses: .tools/python/Lib/site-packages/pillow-11.3.0.dist-info/licenses/
DOSBox-X 2026.10.01: https://github.com/joncampbell123/dosbox-x/tree/dosbox-x-v2026.10.01
License: third-party/DOSBox-X-COPYING.txt
Source: https://github.com/joncampbell123/dosbox-x/archive/refs/tags/dosbox-x-v2026.10.01.zip
OpenXR loader 1.1.49: https://github.com/KhronosGroup/OpenXR-SDK/tree/release-1.1.49
License: third-party/OpenXR-LICENSE.txt
Microsoft Visual C++ runtime DLLs: https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/
'@ | Set-Content -LiteralPath (Join-Path $stage 'third-party/NOTICES.txt') -Encoding utf8

$archive = Join-Path $packages "$name.zip"
# Compress-Archive omits hidden files on some hosts. ZipFile includes .tools.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -LiteralPath (Join-Path $packages "$name.sha256") -Encoding ascii
Write-Host "[LLPACKAGE] $archive"
