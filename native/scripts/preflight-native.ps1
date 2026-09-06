[CmdletBinding()]
param([string]$Configuration='Release', [switch]$RunTests)
$ErrorActionPreference = 'Stop'

function Fail([string]$Category, [string]$Message) {
  throw "$Category`: $Message"
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$occtSource = Join-Path $repositoryRoot 'native/occt'
$occtInstall = Join-Path $repositoryRoot 'native/out/occt'
$nativeBuild = Join-Path $repositoryRoot 'native/build/drawverify-occt'
$fixture = Join-Path $nativeBuild 'fixtures/cube.step'

foreach ($command in @('cmake', 'swig', 'dotnet')) {
  if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
    Fail 'ENVIRONMENT_MISSING' "$command is not available."
  }
}
if ($null -eq (Get-Command 'cl' -ErrorAction SilentlyContinue) -and $null -eq (Get-Command 'c++' -ErrorAction SilentlyContinue)) {
  Fail 'ENVIRONMENT_MISSING' 'No supported C++ compiler was found (cl or c++).'
}
foreach ($file in @('CMakeLists.txt', 'LICENSE_LGPL_21.txt', 'OCCT_LGPL_EXCEPTION.txt')) {
  if (-not (Test-Path (Join-Path $occtSource $file))) {
    Fail 'ENVIRONMENT_MISSING' "native/occt/$file is missing. Run: git submodule update --init --recursive"
  }
}
$version = Get-Content (Join-Path $occtSource 'adm/cmake/version.cmake') -Raw
if ($version -notmatch 'OCC_VERSION_MAJOR 8' -or $version -notmatch 'OCC_VERSION_MINOR 0' -or $version -notmatch 'OCC_VERSION_MAINTENANCE 0') {
  Fail 'ENVIRONMENT_MISSING' 'native/occt is not OCCT 8.0.0.'
}
if (-not (Test-Path $nativeBuild)) { Fail 'BUILD_FAILED' "Native build directory is absent: $nativeBuild" }
if (-not (Test-Path $fixture)) { Fail 'BUILD_FAILED' "Deterministic fixture is absent: $fixture" }

$bridge = if ($IsWindows) { Join-Path $nativeBuild "$Configuration/DrawVerifyOcctNative.dll" } else { Join-Path $nativeBuild 'libDrawVerifyOcctNative.so' }
if (-not (Test-Path $bridge)) { Fail 'NATIVE_LOAD_FAILED' "Native bridge is absent: $bridge" }
$runtimeDirectory = if ($IsWindows) {
  $flatBin = Join-Path $occtInstall 'bin'
  if (Test-Path $flatBin) { $flatBin } else {
    Get-ChildItem (Join-Path $occtInstall 'win64') -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { Join-Path $_.FullName 'bin' } |
      Where-Object { Test-Path $_ } | Select-Object -First 1
  }
} else { Join-Path $occtInstall 'lib' }
if (-not (Test-Path $runtimeDirectory)) { Fail 'NATIVE_LOAD_FAILED' "OCCT runtime libraries are absent: $runtimeDirectory" }

if ($RunTests) {
  ctest --test-dir $nativeBuild -C $Configuration --output-on-failure
  if ($LASTEXITCODE) { Fail 'PROJECTION_FAILED' 'Native managed smoke test failed.' }
  $runtimeId = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }
  $nativeRuntime = Join-Path $repositoryRoot "native/out/runtime/$runtimeId/native"
  if (-not (Test-Path $nativeRuntime)) { Fail 'NATIVE_LOAD_FAILED' "Staged native runtime is absent: $nativeRuntime" }
  $testOutput = Join-Path $repositoryRoot "tests/DrawVerify.OcctService.Tests/bin/$Configuration/net9.0"
  New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
  Get-ChildItem -LiteralPath $nativeRuntime -Force | Copy-Item -Destination $testOutput -Recurse -Force
  $previousPath = $env:Path
  try {
    $env:Path = "$nativeRuntime$([IO.Path]::PathSeparator)$previousPath"
    dotnet test (Join-Path $repositoryRoot 'tests/DrawVerify.OcctService.Tests/DrawVerify.OcctService.Tests.csproj') -c $Configuration --no-restore -m:1
    if ($LASTEXITCODE) { Fail 'STEP_READ_FAILED' 'Managed OCCT integration tests failed.' }
  }
  finally {
    $env:Path = $previousPath
  }
}

Write-Host READY
