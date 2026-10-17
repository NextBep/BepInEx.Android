<#
.SYNOPSIS
    Builds the BepInEx IL2CPP runtime and packs the Android distribution
    (config/, core/, plugins/) into bin\BepInEx.Android.zip.
.DESCRIPTION
    The archive is written with forward-slash entry names and contains no
    test plugins or build byproducts. Copy the resulting zip over the
    launcher's app\src\main\assets\BepInEx.Android.zip.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputZip
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "Runtimes\Unity\BepInEx.Unity.IL2CPP\BepInEx.Unity.IL2CPP.csproj"
$outputPath = Join-Path $root "bin\Unity.IL2CPP"
$stageRoot = Join-Path $root "bin\android-package"

if (-not $OutputZip) {
    $OutputZip = Join-Path $root "bin\BepInEx.Android.zip"
}

Write-Host "[1/3] Building $project ($Configuration)..."
$solutionDir = ($root -replace '\\', '/') + '/'
& dotnet build $project -c $Configuration "-p:SolutionDir=$solutionDir" -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

Write-Host "[2/3] Staging Android layout in $stageRoot..."
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
$coreDir = New-Item -ItemType Directory -Path (Join-Path $stageRoot "core") -Force
$configDir = New-Item -ItemType Directory -Path (Join-Path $stageRoot "config") -Force
$pluginsDir = New-Item -ItemType Directory -Path (Join-Path $stageRoot "plugins") -Force

$coreFiles = Get-ChildItem $outputPath -File | Where-Object {
    $_.Name -like "*.dll" -or $_.Name -like "*.dll.config" -or $_.Name -like "BepInEx*.xml"
}
foreach ($file in $coreFiles) { Copy-Item $file.FullName -Destination $coreDir.FullName -Force }

Copy-Item (Join-Path $root "Runtimes\Unity\BepInEx.Unity.IL2CPP\dist\BepInEx.cfg") `
          (Join-Path $configDir.FullName "BepInEx.cfg") -Force
New-Item -ItemType File -Path (Join-Path $pluginsDir.FullName ".keep") -Force | Out-Null

Write-Host "[3/3] Packing $OutputZip (forward-slash entries)..."
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }

$zip = [System.IO.Compression.ZipFile]::Open($OutputZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $entries = 0
    foreach ($file in Get-ChildItem $stageRoot -Recurse -File) {
        $rel = $file.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        $entry = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        $source = [System.IO.File]::OpenRead($file.FullName)
        try {
            $source.CopyTo($entryStream)
        }
        finally {
            $source.Dispose()
            $entryStream.Dispose()
        }
        $entries++
    }
}
finally {
    $zip.Dispose()
}

$size = (Get-Item $OutputZip).Length
Write-Host ("Packed {0} entries, {1:N0} bytes -> {2}" -f $entries, $size, $OutputZip)
