#Requires -Version 5.1
<#
    Maxim SS-Tools — release build.

    Default: framework-dependent single file (~5 MB). Needs the .NET 8 Desktop
    Runtime on the target machine.

    -SelfContained : bundles the runtime too (~70 MB, no prerequisites).
    -Output <dir>  : where the artefact lands (default: dist).
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$Output = "dist"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src/MaximTool/MaximTool.csproj"
$outDir = Join-Path $PSScriptRoot $Output

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $outDir,
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" })
)

Write-Host "> dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Get-ChildItem $outDir -Filter *.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 2)
    Write-Host ""
    Write-Host "  $($_.FullName)  ($mb MB)" -ForegroundColor Green
}
