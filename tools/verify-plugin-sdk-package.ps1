$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

$repository = Split-Path -Parent $PSScriptRoot
$run = Join-Path ([System.IO.Path]::GetTempPath()) ("FocusRgb.PluginSdk." + [guid]::NewGuid().ToString('N'))
$feedA = Join-Path $run 'feed-a'
$feedB = Join-Path $run 'feed-b'
$consumer = Join-Path $run 'consumer'
$packages = Join-Path $run 'packages'
New-Item -ItemType Directory -Path $feedA, $feedB, $consumer | Out-Null

Invoke-DotNet pack (Join-Path $repository 'src\FocusRgb.Contracts\FocusRgb.Contracts.csproj') -c Release --no-build -o $feedA
Invoke-DotNet pack (Join-Path $repository 'src\FocusRgb.PluginSdk\FocusRgb.PluginSdk.csproj') -c Release --no-build -o $feedA
Invoke-DotNet pack (Join-Path $repository 'src\FocusRgb.Contracts\FocusRgb.Contracts.csproj') -c Release --no-build -o $feedB
Invoke-DotNet pack (Join-Path $repository 'src\FocusRgb.PluginSdk\FocusRgb.PluginSdk.csproj') -c Release --no-build -o $feedB

$extractA = Join-Path $run 'package-a'
$extractB = Join-Path $run 'package-b'
[System.IO.Compression.ZipFile]::ExtractToDirectory(
    (Join-Path $feedA 'FocusRgb.PluginSdk.0.1.0.nupkg'), $extractA)
[System.IO.Compression.ZipFile]::ExtractToDirectory(
    (Join-Path $feedB 'FocusRgb.PluginSdk.0.1.0.nupkg'), $extractB)
$contentsA = Get-ChildItem $extractA -Recurse -File | Sort-Object FullName | ForEach-Object {
    $_.FullName.Substring($extractA.Length) + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash
}
$contentsB = Get-ChildItem $extractB -Recurse -File | Sort-Object FullName | ForEach-Object {
    $_.FullName.Substring($extractB.Length) + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash
}
if (Compare-Object $contentsA $contentsB) { throw 'Plugin SDK package contents are not reproducible.' }

Copy-Item (Join-Path $repository 'tests\FocusRgb.PluginSdk.ConsumerFixture\*') $consumer
Invoke-DotNet restore (Join-Path $consumer 'FocusRgb.PluginSdk.ConsumerFixture.csproj') --source $feedA --packages $packages
Invoke-DotNet build (Join-Path $consumer 'FocusRgb.PluginSdk.ConsumerFixture.csproj') -c Release --no-restore

Write-Host 'Verified reproducible FocusRgb.PluginSdk 0.1.0 package contents.'
Write-Host "Clean-room consumer: $consumer"
