[CmdletBinding()]
param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/build-native.ps1" -Configuration $Configuration
cmake --build native/build/drawverify-occt --config $Configuration --parallel
if($LASTEXITCODE){throw 'BUILD_FAILED: DrawVerify native façade build failed.'}
& "$PSScriptRoot/preflight-native.ps1" -Configuration $Configuration -RunTests
