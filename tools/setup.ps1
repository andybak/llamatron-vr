param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$toolsRoot = Join-Path $projectRoot '.tools'
$downloads = Join-Path $toolsRoot 'downloads'
New-Item -ItemType Directory -Force -Path $downloads, 'artifacts', 'native/generated' | Out-Null
Start-Transcript -Path 'artifacts/setup.log' -Force | Out-Null
$setupLock = $null

function Download([string]$Url, [string]$Destination, [string]$Sha256 = '') {
    if (-not (Test-Path -LiteralPath $Destination)) {
        Write-Host "[LLSETUP] Downloading $(Split-Path $Destination -Leaf)"
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile "$Destination.partial"
        Move-Item -LiteralPath "$Destination.partial" -Destination $Destination -Force
    }
    if ($Sha256 -and (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Checksum mismatch: $Destination. Remove this cached download and run setup again."
    }
}

function Run([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Program $Arguments" }
}

try {
    $setupLock = [System.IO.File]::Open((Join-Path $projectRoot 'artifacts/setup.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'A 64-bit Windows PC is required.' }
    $configuration = $null
    if (Test-Path -LiteralPath 'artifacts/install.json') {
        $configuration = Get-Content -LiteralPath 'artifacts/install.json' -Raw | ConvertFrom-Json
    }
    if (-not $GamePath -and $configuration -and (Test-Path -LiteralPath $configuration.game)) {
        $GamePath = $configuration.game
    }
    if (-not $GamePath -and (Test-Path -LiteralPath 'game/GAME86.EXE')) {
        $GamePath = Join-Path $projectRoot 'game'
    }
    if (-not $GamePath) {
        Add-Type -AssemblyName System.Windows.Forms
        $folder = New-Object System.Windows.Forms.FolderBrowserDialog
        $folder.Description = 'Select the original DOS Llamatron folder containing GAME86.EXE and LLAMA.EXE'
        if ($folder.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No original game folder selected.' }
        $GamePath = $folder.SelectedPath
        $folder.Dispose()
    }
    $GamePath = (Resolve-Path -LiteralPath $GamePath).Path
    foreach ($name in @('GAME86.EXE', 'LLAMA.EXE', 'LLAMA.INF', 'FS.EXE', 'G3.CMP', 'F.CMP', 'S.CMP', 'LLATRON.SPL', 'SBLASTER.BIN')) {
        if (-not (Test-Path -LiteralPath (Join-Path $GamePath $name))) { throw "Game folder is missing $name : $GamePath" }
    }

    # Release ZIPs already contain every runtime dependency and the compiled app.
    # This path is deliberately offline and never installs a compiler.
    if (Test-Path -LiteralPath 'bundle.json') {
        foreach ($file in @('.tools/python/python.exe', '.tools/python/Lib/site-packages/PIL/Image.py', '.tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe', 'native/build/Release/LlamatronNative.exe')) {
            if (-not (Test-Path -LiteralPath $file)) { throw "Incomplete ZIP: missing $file. Extract the complete distribution again." }
        }
        $python = Join-Path $toolsRoot 'python/python.exe'
        $emulator = Join-Path $toolsRoot 'dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe'
        Run $python @('tools/launch_native.py', '--prepare', '--game', $GamePath, '--emulator', $emulator)
        @{ game = $GamePath; emulator = '.tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe'; native = 'native/build/Release/LlamatronNative.exe' } |
            ConvertTo-Json | Set-Content -LiteralPath 'artifacts/install.json' -Encoding utf8
        Write-Host '[LLSETUP] Game copy prepared. Run Play.cmd to play; no build or downloads were needed.'
        return
    }

    # Private Python and Pillow: no system Python or pip installation is needed.
    $pythonRoot = Join-Path $toolsRoot 'python'
    $python = Join-Path $pythonRoot 'python.exe'
    if (-not (Test-Path -LiteralPath $python)) {
        $archive = Join-Path $downloads 'python-3.12.10-embed-amd64.zip'
        Download 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $pythonRoot -Force
    }
    @('python312.zip', '.', 'Lib/site-packages', '../../tools', 'import site') |
        Set-Content -LiteralPath (Join-Path $pythonRoot 'python312._pth') -Encoding ascii
    $sitePackages = Join-Path $pythonRoot 'Lib/site-packages'
    if (-not (Test-Path -LiteralPath (Join-Path $sitePackages 'PIL/Image.py'))) {
        $release = Invoke-RestMethod -Uri 'https://pypi.org/pypi/Pillow/11.3.0/json'
        $wheel = @($release.urls | Where-Object { $_.filename -match 'cp312-cp312-win_amd64.whl$' })
        if ($wheel.Count -ne 1) { throw 'Cannot locate the pinned Windows x64 Pillow wheel.' }
        $archive = Join-Path $downloads 'pillow-11.3.0-cp312-win_amd64.zip'
        Download $wheel[0].url $archive $wheel[0].digests.sha256
        Expand-Archive -LiteralPath $archive -DestinationPath $sitePackages -Force
    }
    Run $python @('-c', 'from PIL import Image; import ctypes')
    Write-Host '[LLSETUP] Private Python and Pillow ready'

    $emulator = Join-Path $toolsRoot 'dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe'
    if (-not (Test-Path -LiteralPath $emulator)) {
        $archive = Join-Path $downloads 'dosbox-x-mingw64-2026.10.01-portable.zip'
        Download 'https://github.com/joncampbell123/dosbox-x/releases/download/dosbox-x-v2026.10.01/dosbox-x-mingw64-2026.10.01-portable.zip' $archive 'def2d84d4415fc26c77252839b4dafb6b476e3a4204b896292e6b05505ca8c59'
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsRoot 'dosbox-x') -Force
    }
    if (-not (Test-Path -LiteralPath $emulator)) { throw "DOSBox-X archive layout did not contain $emulator" }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $visualStudio = $null
    if (Test-Path -LiteralPath $vswhere) {
        $visualStudio = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }
    if (-not $visualStudio) {
        $installer = Join-Path $downloads 'vs_BuildTools.exe'
        Download 'https://aka.ms/vs/17/release/vs_BuildTools.exe' $installer
        Write-Host '[LLSETUP] Installing Visual Studio 2022 C++ Build Tools. Windows may request administrator approval; this download can be several GB.'
        $install = Start-Process -FilePath $installer -ArgumentList @('--passive', '--wait', '--norestart', '--add', 'Microsoft.VisualStudio.Workload.VCTools', '--includeRecommended') -Wait -PassThru -WindowStyle Hidden
        if ($install.ExitCode -eq 3010) { throw 'Build Tools needs a Windows restart. Restart, then run Setup.cmd again.' }
        if ($install.ExitCode -ne 0) { throw "Build Tools installation failed ($($install.ExitCode))." }
        $visualStudio = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $visualStudio) { throw 'Visual Studio 2022 C++ tools are still unavailable.' }
    }

    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    $cmake = $null
    if ($cmakeCommand) {
        $versionOutput = & $cmakeCommand.Source --version
        if ($versionOutput[0] -match '(\d+\.\d+\.\d+)' -and [version]$Matches[1] -ge [version]'3.21.0') {
            $cmake = $cmakeCommand.Source
        }
    }
    if (-not $cmake) {
        $archive = Join-Path $downloads 'cmake-3.31.8-windows-x86_64.zip'
        $checksums = Join-Path $downloads 'cmake-3.31.8-SHA-256.txt'
        Download 'https://github.com/Kitware/CMake/releases/download/v3.31.8/cmake-3.31.8-SHA-256.txt' $checksums
        $checksumLine = Get-Content -LiteralPath $checksums | Where-Object { $_ -match ' cmake-3.31.8-windows-x86_64.zip$' }
        if (-not $checksumLine) { throw 'CMake checksum listing did not contain the Windows x64 archive.' }
        Download 'https://github.com/Kitware/CMake/releases/download/v3.31.8/cmake-3.31.8-windows-x86_64.zip' $archive ($checksumLine -split '\s+')[0]
        Expand-Archive -LiteralPath $archive -DestinationPath $toolsRoot -Force
        $cmake = Join-Path $toolsRoot 'cmake-3.31.8-windows-x86_64/bin/cmake.exe'
    }

    $loader = Join-Path $projectRoot 'native/generated/openxr_loader.dll'
    if (-not (Test-Path -LiteralPath $loader)) {
        $archive = Join-Path $downloads 'OpenXR-SDK-release-1.1.49.zip'
        Download 'https://github.com/KhronosGroup/OpenXR-SDK/archive/refs/tags/release-1.1.49.zip' $archive
        $sdk = Join-Path $toolsRoot 'OpenXR-SDK-release-1.1.49'
        if (-not (Test-Path -LiteralPath (Join-Path $sdk 'CMakeLists.txt'))) {
            Expand-Archive -LiteralPath $archive -DestinationPath $toolsRoot -Force
        }
        Run $cmake @('-S', $sdk, '-B', '.tools/openxr-build', '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DCMAKE_GENERATOR_INSTANCE=$visualStudio", '-DDYNAMIC_LOADER=ON', '-DBUILD_TESTING=OFF')
        Run $cmake @('--build', '.tools/openxr-build', '--config', 'Release', '--target', 'openxr_loader', '--parallel')
        $builtLoader = Join-Path $toolsRoot 'openxr-build/src/loader/Release/openxr_loader.dll'
        if (-not (Test-Path -LiteralPath $builtLoader)) { throw 'OpenXR loader build did not produce the Release DLL.' }
        Copy-Item -LiteralPath $builtLoader -Destination $loader
    }

    Run $python @('tools/launch_native.py', '--prepare', '--game', $GamePath, '--emulator', $emulator)
    Run $python @('tools/inspect_game.py', $GamePath)
    Run $python @('tools/export_sprites.py', '--palette', 'artifacts/native-runtime/palette.json')
    Run $python @('native/generate_assets.py')
    Run $cmake @('-S', 'native', '-B', 'native/build', '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DCMAKE_GENERATOR_INSTANCE=$visualStudio")
    Run $cmake @('--build', 'native/build', '--config', 'Release', '--parallel')
    @{ game = $GamePath; emulator = '.tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe'; native = 'native/build/Release/LlamatronNative.exe' } |
        ConvertTo-Json | Set-Content -LiteralPath 'artifacts/install.json' -Encoding utf8
    Write-Host '[LLSETUP] Setup complete. The setup emulator is stopped. Run Play.cmd to play.'
} finally {
    if ($setupLock) { $setupLock.Dispose() }
    Stop-Transcript | Out-Null
}
