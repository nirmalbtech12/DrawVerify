[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [int]$ParallelJobs = 2
)
if ($ParallelJobs -lt 1) {
    throw 'ParallelJobs must be at least 1.'
}
$ErrorActionPreference='Stop'
& "$PSScriptRoot/build-occt.ps1" `
    -Configuration $Configuration `
    -ParallelJobs $ParallelJobs
& "$PSScriptRoot/generate-wrapper.ps1"
dotnet restore native/DrawVerifyOcct/tests/ManagedSmoke.csproj
if($LASTEXITCODE){throw 'BUILD_FAILED: managed smoke test restore failed.'}
cmake -S native -B native/build/drawverify-occt -DOCCT_INSTALL_DIR="$PWD/native/out/occt"
if($LASTEXITCODE){throw 'BUILD_FAILED: native façade configuration failed.'}
cmake --build native/build/drawverify-occt `
    --config $Configuration `
    --parallel $ParallelJobs
if($LASTEXITCODE){throw 'BUILD_FAILED: native façade build failed.'}
& "$PSScriptRoot/stage-native-runtime.ps1" -Configuration $Configuration
dotnet build src/DrawVerify.OcctService/DrawVerify.OcctService.csproj -c $Configuration
if($LASTEXITCODE){throw 'BUILD_FAILED: DrawVerify.Occt managed build failed.'}
