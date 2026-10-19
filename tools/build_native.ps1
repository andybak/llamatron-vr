$ErrorActionPreference = 'Stop'
python native/generate_assets.py
if ($LASTEXITCODE -ne 0) { throw 'Native asset preparation failed' }
cmake -S native -B native/build -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed' }
cmake --build native/build --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
Write-Output 'Native executable: native/build/Release/LlamatronNative.exe'
