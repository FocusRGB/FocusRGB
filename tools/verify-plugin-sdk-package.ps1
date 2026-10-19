$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

$repository = Split-Path -Parent $PSScriptRoot
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$run = Join-Path $tempRoot ("FocusRgb.PluginSdk." + [guid]::NewGuid().ToString('N'))
$feedA = Join-Path $run 'feed-a'
$feedB = Join-Path $run 'feed-b'
$artifactsA = Join-Path $run 'artifacts-a'
$artifactsB = Join-Path $run 'artifacts-b'
$consumer = Join-Path $run 'consumer'
$templateHive = Join-Path $run 'template-hive'
$generated = Join-Path $run 'generated-plugin'
$packages = Join-Path $run 'packages'
New-Item -ItemType Directory -Path $feedA, $feedB, $consumer | Out-Null

try {
    Invoke-DotNet pack (Join-Path $repository 'sdk\dotnet\FocusRgb.Contracts\FocusRgb.Contracts.csproj') -c Release -o $feedA "-p:ArtifactsPath=$artifactsA" '-p:ContinuousIntegrationBuild=true'
    Invoke-DotNet pack (Join-Path $repository 'sdk\dotnet\FocusRgb.PluginSdk\FocusRgb.PluginSdk.csproj') -c Release -o $feedA "-p:ArtifactsPath=$artifactsA" '-p:ContinuousIntegrationBuild=true'
    Invoke-DotNet pack (Join-Path $repository 'sdk\dotnet\FocusRgb.Contracts\FocusRgb.Contracts.csproj') -c Release -o $feedB "-p:ArtifactsPath=$artifactsB" '-p:ContinuousIntegrationBuild=true'
    Invoke-DotNet pack (Join-Path $repository 'sdk\dotnet\FocusRgb.PluginSdk\FocusRgb.PluginSdk.csproj') -c Release -o $feedB "-p:ArtifactsPath=$artifactsB" '-p:ContinuousIntegrationBuild=true'

    $extractA = Join-Path $run 'package-a'
    $extractB = Join-Path $run 'package-b'
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        (Join-Path $feedA 'FocusRgb.PluginSdk.1.0.0.nupkg'), $extractA)
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        (Join-Path $feedB 'FocusRgb.PluginSdk.1.0.0.nupkg'), $extractB)
    $contentsA = Get-ChildItem $extractA -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($extractA.Length) + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    }
    $contentsB = Get-ChildItem $extractB -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($extractB.Length) + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    }
    $difference = Compare-Object $contentsA $contentsB
    if ($difference) {
        $difference | Format-Table | Out-String | Write-Host
        throw 'Plugin SDK package contents are not reproducible.'
    }

    Copy-Item (Join-Path $repository 'tests\FocusRgb.PluginSdk.ConsumerFixture\*') $consumer
    Invoke-DotNet restore (Join-Path $consumer 'FocusRgb.PluginSdk.ConsumerFixture.csproj') --source $feedA --packages $packages
    Invoke-DotNet build (Join-Path $consumer 'FocusRgb.PluginSdk.ConsumerFixture.csproj') -c Release --no-restore

    Invoke-DotNet new install (Join-Path $repository 'sdk\templates\dotnet-plugin') --debug:custom-hive $templateHive
    Invoke-DotNet new focusrgb-plugin --name FocusRgb.Plugin.Generated --PluginId generated-output `
        --output $generated --debug:custom-hive $templateHive
    $generatedProject = Join-Path $generated 'FocusRgb.Plugin.Generated.csproj'
    Invoke-DotNet restore $generatedProject --source $feedA --packages $packages
    Invoke-DotNet build $generatedProject -c Release --no-restore
    $generatedManifest = Get-Content (Join-Path $generated 'focusrgb-plugin.json') -Raw | ConvertFrom-Json
    if ($generatedManifest.id -ne 'generated-output' -or $generatedManifest.contractVersion -ne '1.0') {
        throw 'Generated plugin manifest did not contain the requested ID and stable contract.'
    }
    & python (Join-Path $repository 'sdk\conformance\plugin-conformance.py') --plugin-id generated-output -- `
        dotnet (Join-Path $generated 'bin\Release\net10.0\FocusRgb.Plugin.Generated.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Generated plugin failed protocol conformance.' }

    Write-Host 'Verified reproducible SDK packages, independent consumer, and generated plugin.'
}
finally {
    $resolvedRun = [System.IO.Path]::GetFullPath($run)
    if ($resolvedRun.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedRun -Recurse -Force
    }
}
