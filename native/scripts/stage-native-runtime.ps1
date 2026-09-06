[CmdletBinding()]
param([string]$Configuration='Release')
$ErrorActionPreference='Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$nativeBuild = Join-Path $repositoryRoot 'native/build/drawverify-occt'
$occtInstall = Join-Path $repositoryRoot 'native/out/occt'
$runtimeId = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }
$destination = Join-Path $repositoryRoot "native/out/runtime/$runtimeId/native"

if (-not (Test-Path $nativeBuild)) { throw "BUILD_FAILED: native façade build directory is absent: $nativeBuild" }
if (-not (Test-Path $occtInstall)) { throw "BUILD_FAILED: OCCT shared installation is absent: $occtInstall" }

New-Item -ItemType Directory -Force -Path $destination | Out-Null
$bridgeName = if ($IsWindows) { 'DrawVerifyOcctNative.dll' } elseif ($IsMacOS) { 'libDrawVerifyOcctNative.dylib' } else { 'libDrawVerifyOcctNative.so' }
$bridge = Get-ChildItem -Path $nativeBuild -Filter $bridgeName -Recurse | Select-Object -First 1
if ($null -eq $bridge) { throw "BUILD_FAILED: native bridge was not produced: $bridgeName" }
Copy-Item -Force $bridge.FullName $destination

$libraryDirectory = if ($IsWindows) {
  $flatBin = Join-Path $occtInstall 'bin'
  if (Test-Path $flatBin) { $flatBin } else {
    $windowsBin = Get-ChildItem (Join-Path $occtInstall 'win64') -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { Join-Path $_.FullName 'bin' } |
      Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $windowsBin) { throw "BUILD_FAILED: OCCT Windows runtime directory is absent under $occtInstall" }
    $windowsBin
  }
} else { Join-Path $occtInstall 'lib' }
if (-not (Test-Path $libraryDirectory)) { throw "BUILD_FAILED: OCCT runtime library directory is absent: $libraryDirectory" }
$pattern = if ($IsWindows) { '*.dll' } elseif ($IsMacOS) { '*.dylib' } else { '*.so*' }
$libraries = Get-ChildItem -Path $libraryDirectory -Filter $pattern
if ($libraries.Count -eq 0) { throw "BUILD_FAILED: no OCCT shared runtime libraries found in $libraryDirectory" }
Copy-Item -Force $libraries.FullName $destination
Write-Host "Staged DrawVerifyOcctNative and $($libraries.Count) OCCT shared libraries in $destination"
