# Regenerates the SWIG C# wrapper into src/DrawVerify.OcctWrapper/Generated.
<#
.SYNOPSIS
    Runs SWIG against native/swig/occcsharp.i for C# and writes the generated
    C# wrapper + C++ glue into src/DrawVerify.OcctWrapper/Generated.
.DESCRIPTION
    Requires SWIG (>= 4.x) supporting the C# backend. Output is created by SWIG
    and must never be edited by hand. The OCCT version used must be recorded in
    docs/deployment.md and src/DrawVerify.OcctWrapper/README.md.
.NOTES
    Generated output is gitignored; regenerate as part of the build pipeline.
#>
[CmdletBinding()]
param(
    [string]$SwigModule = "native/swig/occcsharp.i",
    [string]$OutputDir = "src/DrawVerify.OcctWrapper/Generated"
)

$ErrorActionPreference = "Stop"

Write-Host "DrawVerify: SWIG wrapper generation is scaffolded but not yet implemented."
Write-Host "Steps to complete:"
Write-Host "  1. swig -c++ -csharp -module occcsharp -outdir $OutputDir -o $OutputDir/occcsharp.cxx $SwigModule"
Write-Host "  2. Verify generated .cs files land under $OutputDir"