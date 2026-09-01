[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$BuildDir = 'native/build/occt',
    [string]$InstallDir = 'native/out/occt',
    [int]$ParallelJobs = 2
)
if ($ParallelJobs -lt 1) {
    throw 'ParallelJobs must be at least 1.'
}
$ErrorActionPreference='Stop'

if ($InstallDir -eq '$InstallDir') {
    throw 'InstallDir was passed as the literal $InstallDir. Pass a real path such as native/out/occt.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = Join-Path $repositoryRoot 'native/occt'
function Resolve-BuildPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}
$buildPath = Resolve-BuildPath $BuildDir
$installPath = Resolve-BuildPath $InstallDir
foreach($f in @('CMakeLists.txt','LICENSE_LGPL_21.txt','OCCT_LGPL_EXCEPTION.txt')){if(-not(Test-Path (Join-Path $source $f))){throw "ENVIRONMENT_MISSING: native/occt/$f is missing. Run: git submodule update --init --recursive"}}
$v=Get-Content (Join-Path $source 'adm\cmake\version.cmake') -Raw;if($v-notmatch 'OCC_VERSION_MAJOR 8' -or $v-notmatch 'OCC_VERSION_MINOR 0' -or $v-notmatch 'OCC_VERSION_MAINTENANCE 0'){throw 'ENVIRONMENT_MISSING: native/occt is not OCCT 8.0.0.'}
cmake --fresh -S $source -B $buildPath -DBUILD_LIBRARY_TYPE=Shared -DBUILD_MODULE_Draw=OFF -DBUILD_MODULE_Visualization=OFF -DBUILD_MODULE_ApplicationFramework=OFF -DBUILD_MODULE_FoundationClasses=OFF -DBUILD_MODULE_ModelingData=OFF -DBUILD_MODULE_ModelingAlgorithms=OFF -DBUILD_MODULE_DataExchange=OFF '-DBUILD_ADDITIONAL_TOOLKITS=TKDESTEP;TKHLR;TKMesh' -DUSE_FREETYPE=OFF -DUSE_XLIB=OFF "-DCMAKE_INSTALL_PREFIX=$installPath"
if($LASTEXITCODE){throw 'BUILD_FAILED: OCCT configuration failed.'}
cmake --build $buildPath --config $Configuration --parallel $ParallelJobs
if($LASTEXITCODE){throw 'BUILD_FAILED: OCCT build failed.'}
cmake --install $buildPath --config $Configuration
if($LASTEXITCODE){throw 'BUILD_FAILED: OCCT install failed.'}
