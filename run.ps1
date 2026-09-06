[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$appProject = Join-Path $repositoryRoot 'src/DrawVerify.App'
$settings = Get-Content (Join-Path $appProject 'appsettings.json') -Raw | ConvertFrom-Json

$publishPath = [Environment]::ExpandEnvironmentVariables([string]$settings.Publish.Path)
if (-not [IO.Path]::IsPathRooted($publishPath)) { $publishPath = Join-Path $repositoryRoot $publishPath }
$publishPath = [IO.Path]::GetFullPath($publishPath)

$nativeRuntime = Join-Path $repositoryRoot "native/out/runtime/$RuntimeIdentifier/native"
$nativeArtifact = Join-Path $repositoryRoot "artifacts/DrawVerify-native-occt-8.0.0-$RuntimeIdentifier.zip"
$nativeBridge = Join-Path $nativeRuntime 'DrawVerifyOcctNative.dll'

function Invoke-RequiredCommand([string]$FilePath, [string[]]$ArgumentList) {
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($ArgumentList -join ' ')" }
}

if ((Test-Path $nativeRuntime) -and -not (Test-Path $nativeArtifact)) {
    & (Join-Path $repositoryRoot 'native/scripts/manage-native-artifact.ps1') `
        -Mode Pack `
        -RuntimeId $RuntimeIdentifier `
        -Configuration $Configuration
}
if (-not (Test-Path $nativeBridge)) {
    throw "Native OCCT runtime is missing: $nativeBridge. Restore or stage the $RuntimeIdentifier native artifact before publishing."
}

if (Test-Path $publishPath) { Remove-Item -LiteralPath $publishPath -Recurse -Force }
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-RequiredCommand 'dotnet' @('restore', $appProject, '-r', $RuntimeIdentifier)
    Invoke-RequiredCommand 'dotnet' @('publish', $appProject, '-c', $Configuration, '-r', $RuntimeIdentifier, '--self-contained', 'true', '--no-restore', '-o', $publishPath)
}
finally { Pop-Location }

$publishedNative = Join-Path $publishPath 'native'
New-Item -ItemType Directory -Path $publishedNative -Force | Out-Null
Get-ChildItem -LiteralPath $nativeRuntime -Force | Copy-Item -Destination $publishedNative -Recurse -Force

$databaseSource = Join-Path $repositoryRoot 'data/database/drawverify.db'
$databaseTarget = Join-Path $publishPath 'drawverify.db'
if ((Test-Path $databaseSource) -and -not (Test-Path $databaseTarget)) { Copy-Item $databaseSource $databaseTarget }

$executable = Join-Path $publishPath 'DrawVerify.App.exe'
if (-not (Test-Path $executable)) { throw "Published application is missing: $executable" }
Start-Process -FilePath $executable -WorkingDirectory $publishPath
