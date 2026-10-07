# Reproduce the native dependencies from fixed upstream releases.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    New-Item -ItemType Directory -Force -Path 'vendor','.local\dependency-cache' | Out-Null
    $downloads = @(
        @('https://github.com/ladislav-zezula/StormLib/releases/download/v9.40/stormlib_dll.zip', '.local\dependency-cache\stormlib_dll.zip', 'B2C9635E7B63EDEE1BD7C82E7DC180D739F3ACCB2B8994804C7774E464CE89AE'),
        @('https://github.com/ladislav-zezula/CascLib/archive/refs/tags/3.0.zip', '.local\dependency-cache\casclib-source.zip', 'A2D72255918BCAE23CEC9715FD7DE383C058B23130D60638AE77DA24E1D98F35')
    )
    foreach ($download in $downloads) {
        if (!(Test-Path -LiteralPath $download[1])) { Invoke-WebRequest -Uri $download[0] -OutFile $download[1] }
        if ((Get-FileHash -LiteralPath $download[1]).Hash -ne $download[2]) { throw "Upstream archive hash mismatch: $($download[1])" }
    }
    Expand-Archive -LiteralPath '.local\dependency-cache\stormlib_dll.zip' -DestinationPath '.local\dependency-cache\stormlib' -Force
    Expand-Archive -LiteralPath '.local\dependency-cache\casclib-source.zip' -DestinationPath '.local\dependency-cache\casclib-src' -Force
    Copy-Item -LiteralPath '.local\dependency-cache\stormlib\x64\StormLib.dll' -Destination 'vendor\StormLib.dll'
    $resourcePath = '.local\dependency-cache\casclib-src\CascLib-3.0\src\DllMain.rc'
    (Get-Content -LiteralPath $resourcePath -Raw).Replace('#include "afxres.h"', '#include "windows.h"') | Set-Content -LiteralPath $resourcePath
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $visualStudioPath = & $vswherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$visualStudioPath) { throw 'Visual Studio C++ Build Tools is required.' }
    $buildToolPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
    & $buildToolPath '.local\dependency-cache\casclib-src\CascLib-3.0\CascLib_dll.vcxproj' /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:CharacterSet=Unicode /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 /verbosity:quiet /nologo
    if ($LASTEXITCODE -ne 0) { throw 'CascLib build failed.' }
    Copy-Item -LiteralPath '.local\dependency-cache\casclib-src\CascLib-3.0\bin\CascLib_dll\x64\Release\CascLib.dll' -Destination 'vendor\CascLib.dll'
    Copy-Item -LiteralPath '.local\dependency-cache\casclib-src\CascLib-3.0\LICENSE' -Destination 'vendor\CascLib-LICENSE.txt'
    Invoke-WebRequest 'https://raw.githubusercontent.com/ladislav-zezula/StormLib/v9.40/LICENSE' -OutFile 'vendor\StormLib-LICENSE.txt'
} finally { Pop-Location }
