[CmdletBinding()]
param(
    [ValidateSet('Pack', 'Restore')]
    [string]$Mode = 'Pack',
    [string]$ArtifactPath,
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$RuntimeId,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if ([string]::IsNullOrWhiteSpace($RuntimeId)) {
    $RuntimeId = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
}

function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
    $ArtifactPath = "artifacts/DrawVerify-native-occt-8.0.0-$RuntimeId.zip"
}
$artifact = Resolve-RepositoryPath $ArtifactPath

if ($Mode -eq 'Restore') {
    if (-not (Test-Path $artifact -PathType Leaf)) {
        throw "NATIVE_ARTIFACT_MISSING: artifact was not found: $artifact"
    }

    Expand-Archive -LiteralPath $artifact -DestinationPath $repositoryRoot -Force
    Write-Host "Restored native artifact: $artifact"
    exit 0
}

$requiredPaths = @(
    'native/out/occt',
    "native/out/runtime/$RuntimeId/native",
    'src/DrawVerify.OcctWrapper/Generated',
    'native/DrawVerifyOcct/generated'
)

foreach ($relativePath in $requiredPaths) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path $path -PathType Container)) {
        throw "NATIVE_ARTIFACT_MISSING: required prebuild output is absent: $relativePath"
    }
}

$artifactDirectory = Split-Path $artifact -Parent
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

$stage = Join-Path ([IO.Path]::GetTempPath()) "drawverify-native-artifact-$([guid]::NewGuid())"
try {
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    foreach ($relativePath in $requiredPaths) {
        $source = Join-Path $repositoryRoot $relativePath
        $destination = Join-Path $stage $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -Path $source -Destination $destination -Recurse -Force
    }

    $manifest = [ordered]@{
        Product = 'DrawVerify'
        OcctVersion = '8.0.0'
        RuntimeId = $RuntimeId
        Configuration = $Configuration
    }
    $manifestPath = Join-Path $stage 'native/out/native-artifact-manifest.json'
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $artifact -Force
    Write-Host "Created native artifact: $artifact"
}
finally {
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
