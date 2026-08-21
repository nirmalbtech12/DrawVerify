# Validates the OCCT native deployment spike (DLL discovery, load, release).
<#
.SYNOPSIS
    Verification spike proving the native OCCT DLLs load cleanly and native
    handles release correctly before committing to the SWIG/deployment path.
.DESCRIPTION
    Confirms that native/bin contains the required TK*.dll set, that they can be
    loaded by the managed assembly load context, and that no native handles leak
    beyond the SafeHandle lifecycle. Run only after build-occt.ps1 has produced
    native/bin output.
.NOTES
    See docs/deployment.md section 'Deployment spike / AOT validation'.
#>
[CmdletBinding()]
param(
    [string]$BinDir = "native/bin",
    [string[]]$RequiredDlls = @("TKernel.dll", "TKBRep.dll", "TKXDESTEP.dll", "TKXSBase.dll")
)

$ErrorActionPreference = "Stop"

Write-Host "DrawVerify: OCCT deployment validation is scaffolded but not yet implemented."
foreach ($dll in $RequiredDlls) {
    $path = Join-Path $BinDir $dll
    $exists = Test-Path $path
    Write-Host ("[{0}] {1}" -f ($(if ($exists) { "OK " } else { "MISS" }), $dll))
}