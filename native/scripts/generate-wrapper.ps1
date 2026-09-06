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
$swigLibrary = if($env:SWIG_LIB){$env:SWIG_LIB}else{((& swig -swiglib|Out-String).Trim())}
if(-not(Test-Path (Join-Path $swigLibrary 'swig.swg'))){
  $swigLibrary = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Packages/SWIG*/swigwin-*/Lib/swig.swg') -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty DirectoryName
}
if(-not $swigLibrary -or -not(Test-Path (Join-Path $swigLibrary 'swig.swg'))){throw 'ENVIRONMENT_MISSING: SWIG standard library was not found. Set SWIG_LIB to the directory containing swig.swg.'}
$swigIncludeArguments = @("-I$swigLibrary")
foreach($directory in @('csharp','std')){
  $directoryPath = Join-Path $swigLibrary $directory
  if(Test-Path $directoryPath){$swigIncludeArguments += "-I$directoryPath"}
}
foreach($path in @($Interface,(Join-Path $HeaderDirectory 'DrawVerify/Occt/Facade.hxx'))){if(-not(Test-Path $path)){throw "BUILD_FAILED: required input is missing: $path"}}
New-Item -Force -ItemType Directory -Path $NativeOutputDirectory,$ManagedOutputDirectory|Out-Null
& swig -cpperraswarn -c++ -csharp -namespace DrawVerify.OcctWrapper.Generated -dllimport DrawVerifyOcctNative "-I$HeaderDirectory" $swigIncludeArguments -outdir $ManagedOutputDirectory -o (Join-Path $NativeOutputDirectory DrawVerifyOcct_wrap.cxx) $Interface
if($LASTEXITCODE -ne 0){throw 'BUILD_FAILED: SWIG generation failed.'}
