# Builds OCCT native DLLs into native/bin via CMake.
<#
.SYNOPSIS
    Builds the OCCT native libraries required by DrawVerify into native/bin.
.DESCRIPTION
    Reads OCCT source from native/occt, configures with native/CMakeLists.txt,
    builds (Release), and copies the resulting TK*.dll into native/bin.
    Requires a working CMake (>= 3.16) and a C++17 compiler toolchain.
.NOTES
    See docs/deployment.md for prerequisites and expected outputs.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$BuildDir = "native/build",
    [string]$OutputDir = "native/bin"
)

$ErrorActionPreference = "Stop"

Write-Host "DrawVerify: OCCT native build is scaffolded but not yet implemented."
Write-Host "Steps to complete:"
Write-Host "  1. cmake -S native -B $BuildDir -DOpenCASCADE_INSTALL_PREFIX=$OutputDir"
Write-Host "  2. cmake --build $BuildDir --config $Configuration"
Write-Host "  3. Copy TK*.dll from build output into $OutputDir"