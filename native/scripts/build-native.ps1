[CmdletBinding()]
param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/build-occt.ps1" -Configuration $Configuration
& "$PSScriptRoot/generate-wrapper.ps1"
cmake -S native -B native/build/drawverify-occt -DOCCT_INSTALL_DIR="$PWD/native/out/occt"
if($LASTEXITCODE){throw 'BUILD_FAILED: native façade configuration failed.'}
cmake --build native/build/drawverify-occt --config $Configuration --parallel
if($LASTEXITCODE){throw 'BUILD_FAILED: native façade build failed.'}
& "$PSScriptRoot/stage-native-runtime.ps1" -Configuration $Configuration
dotnet build src/DrawVerify.OcctService/DrawVerify.OcctService.csproj -c $Configuration
if($LASTEXITCODE){throw 'BUILD_FAILED: DrawVerify.Occt managed build failed.'}
