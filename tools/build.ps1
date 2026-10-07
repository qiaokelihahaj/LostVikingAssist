$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    if (!(Test-Path -LiteralPath 'vendor\StormLib.dll') -or !(Test-Path -LiteralPath 'vendor\CascLib.dll')) {
        & (Join-Path $PSScriptRoot 'fetch-vendor.ps1')
    }
    & dotnet run --project 'tests\LostVikingAssist.Tests\LostVikingAssist.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & dotnet publish 'src\LostVikingAssist\LostVikingAssist.csproj' -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeAllContentForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' -o 'dist\app'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath 'README.md','LICENSE' -Destination 'dist\app'
    Copy-Item -LiteralPath 'docs\usage.md' -Destination 'dist\app\使用说明.md'
    New-Item -ItemType Directory -Force -Path 'dist\app\docs','dist\app\vendor' | Out-Null
    Copy-Item -LiteralPath 'docs\usage.md','docs\development.md' -Destination 'dist\app\docs'
    Copy-Item -LiteralPath 'vendor\README.md','vendor\StormLib-LICENSE.txt','vendor\CascLib-LICENSE.txt' -Destination 'dist\app\vendor'
    Get-FileHash -LiteralPath 'dist\app\LostVikingAssist.exe' | ForEach-Object { "$($_.Hash)  LostVikingAssist.exe" } | Set-Content -LiteralPath 'dist\app\SHA256.txt'
    $packageFiles = @('LostVikingAssist.exe','README.md','使用说明.md','LICENSE','SHA256.txt','docs','vendor') | ForEach-Object { Join-Path 'dist\app' $_ }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath 'dist\LostVikingAssist-win-x64.zip' -Force
    Write-Output 'Ready: dist\app\LostVikingAssist.exe'
    Write-Output 'Package: dist\LostVikingAssist-win-x64.zip'
} finally { Pop-Location }
