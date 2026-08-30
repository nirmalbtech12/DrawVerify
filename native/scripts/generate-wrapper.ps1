[CmdletBinding()]
param([string]$Interface='native/DrawVerifyOcct/swig/DrawVerifyOcct.i',[string]$HeaderDirectory='native/DrawVerifyOcct/include',[string]$NativeOutputDirectory='native/DrawVerifyOcct/generated',[string]$ManagedOutputDirectory='src/DrawVerify.OcctWrapper/Generated')
$ErrorActionPreference='Stop'
if(-not(Get-Command swig -ErrorAction SilentlyContinue)){throw 'ENVIRONMENT_MISSING: SWIG 4.x is required.'}
if((& swig -version|Out-String)-notmatch 'SWIG Version 4\.'){throw 'ENVIRONMENT_MISSING: SWIG 4.x is required.'}
foreach($path in @($Interface,(Join-Path $HeaderDirectory 'DrawVerify/Occt/Facade.hxx'))){if(-not(Test-Path $path)){throw "BUILD_FAILED: required input is missing: $path"}}
New-Item -Force -ItemType Directory -Path $NativeOutputDirectory,$ManagedOutputDirectory|Out-Null
& swig -c++ -csharp -namespace DrawVerify.OcctWrapper.Generated -dllimport DrawVerifyOcctNative -I$HeaderDirectory -outdir $ManagedOutputDirectory -o (Join-Path $NativeOutputDirectory DrawVerifyOcct_wrap.cxx) $Interface
if($LASTEXITCODE -ne 0){throw 'BUILD_FAILED: SWIG generation failed.'}
