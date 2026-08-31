[CmdletBinding()]
param([string]$Interface='native/DrawVerifyOcct/swig/DrawVerifyOcct.i',[string]$HeaderDirectory='native/DrawVerifyOcct/include',[string]$NativeOutputDirectory='native/DrawVerifyOcct/generated',[string]$ManagedOutputDirectory='src/DrawVerify.OcctWrapper/Generated')
$ErrorActionPreference='Stop'
$repositoryRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if(-not[System.IO.Path]::IsPathRooted($Interface)){$Interface=Join-Path $repositoryRoot $Interface}
if(-not[System.IO.Path]::IsPathRooted($HeaderDirectory)){$HeaderDirectory=Join-Path $repositoryRoot $HeaderDirectory}
if(-not[System.IO.Path]::IsPathRooted($NativeOutputDirectory)){$NativeOutputDirectory=Join-Path $repositoryRoot $NativeOutputDirectory}
if(-not[System.IO.Path]::IsPathRooted($ManagedOutputDirectory)){$ManagedOutputDirectory=Join-Path $repositoryRoot $ManagedOutputDirectory}
if(-not(Get-Command swig -ErrorAction SilentlyContinue)){throw 'ENVIRONMENT_MISSING: SWIG 4.x is required.'}
if((& swig -version|Out-String)-notmatch 'SWIG Version 4\.'){throw 'ENVIRONMENT_MISSING: SWIG 4.x is required.'}
foreach($path in @($Interface,(Join-Path $HeaderDirectory 'DrawVerify/Occt/Facade.hxx'))){if(-not(Test-Path $path)){throw "BUILD_FAILED: required input is missing: $path"}}
New-Item -Force -ItemType Directory -Path $NativeOutputDirectory,$ManagedOutputDirectory|Out-Null
& swig -c++ -csharp -namespace DrawVerify.OcctWrapper.Generated -dllimport DrawVerifyOcctNative "-I$HeaderDirectory" -outdir $ManagedOutputDirectory -o (Join-Path $NativeOutputDirectory DrawVerifyOcct_wrap.cxx) $Interface
if($LASTEXITCODE -ne 0){throw 'BUILD_FAILED: SWIG generation failed.'}
