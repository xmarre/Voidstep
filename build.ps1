param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ReferenceRoot = "",
    [switch]$AllowNugetReferenceFallback
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Version = "1.0.1"
$Solution = Join-Path $Root "Voidstep.sln"
$RuntimeProject = Join-Path $Root "src/Voidstep/Voidstep.csproj"
$TestProject = Join-Path $Root "tests/Voidstep.Core.Tests/Voidstep.Core.Tests.csproj"
$ManifestPath = Join-Path $Root "references/reference-manifest.json"
$Artifacts = Join-Path $Root "artifacts"
$BuildOut = Join-Path $Artifacts "build"
$Stage = Join-Path $Artifacts "stage"
$Dist = Join-Path $Root "dist"
$ModuleSource = Join-Path $Root "module/Voidstep"
$ZipPath = Join-Path $Dist "Voidstep-v$Version-Bannerlord-1.3.15.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 8.0 or newer is required. Install it and rerun .\build.ps1."
}

if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:BANNERLORD_REFERENCE_DIR)) {
        $ReferenceRoot = $env:BANNERLORD_REFERENCE_DIR
    } else {
        $ReferenceRoot = Join-Path $Root "references/runtime"
    }
}
$ReferenceRoot = [IO.Path]::GetFullPath($ReferenceRoot)

function Test-ReferenceManifest {
    param([string]$RootPath, [string]$Manifest)
    if (-not (Test-Path $Manifest -PathType Leaf)) { throw "Reference manifest missing: $Manifest" }
    $lock = Get-Content $Manifest -Raw | ConvertFrom-Json
    $missing = New-Object System.Collections.Generic.List[string]
    $mismatch = New-Object System.Collections.Generic.List[string]
    foreach ($property in $lock.files.PSObject.Properties) {
        $name = $property.Name
        $expected = [string]$property.Value
        $path = Join-Path $RootPath $name
        if (-not (Test-Path $path -PathType Leaf)) {
            $missing.Add($name)
            continue
        }
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected.ToLowerInvariant()) {
            $mismatch.Add("$name expected $expected actual $actual")
        }
    }
    if ($missing.Count -gt 0) { throw "Missing reference files:`n$($missing -join "`n")" }
    if ($mismatch.Count -gt 0) { throw "Reference hash mismatch:`n$($mismatch -join "`n")" }
    Write-Host "Validated authoritative Bannerlord 1.3.15 / TOR 1.16 reference hashes in $RootPath"
}

$HaveRuntimeReferences = Test-Path (Join-Path $ReferenceRoot "TaleWorlds.MountAndBlade.dll") -PathType Leaf
if ($HaveRuntimeReferences) {
    Test-ReferenceManifest -RootPath $ReferenceRoot -Manifest $ManifestPath
    $Python = Get-Command python -ErrorAction SilentlyContinue
    if ($Python) {
        & $Python.Source (Join-Path $Root "scripts/validate_api_surface.py") $ReferenceRoot
        if ($LASTEXITCODE -ne 0) { throw "Bannerlord API surface validation failed." }
    } else {
        Write-Warning "Python is unavailable; SHA-256 validation passed, but metadata signature validation was skipped."
    }
} elseif (-not $AllowNugetReferenceFallback) {
    throw "Authoritative references are absent from '$ReferenceRoot'. Set BANNERLORD_REFERENCE_DIR or pass -ReferenceRoot. Use -AllowNugetReferenceFallback only for non-release source validation."
} else {
    Write-Warning "Using pinned NuGet reference assemblies. This mode is not accepted for a release build."
}

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $BuildOut, $Stage, $Dist | Out-Null

$BuildProperties = @("/p:ContinuousIntegrationBuild=true")
if ($HaveRuntimeReferences) {
    $BuildProperties += "/p:BannerlordReferenceDir=$ReferenceRoot"
}

Write-Host "Restoring managed dependencies..."
dotnet restore $Solution
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host "Running pure logic tests..."
dotnet test $TestProject -c $Configuration --no-restore @BuildProperties
if ($LASTEXITCODE -ne 0) { throw "Logic tests failed with exit code $LASTEXITCODE" }

Write-Host "Compiling Voidstep..."
dotnet build $RuntimeProject -c $Configuration --no-restore -o $BuildOut @BuildProperties
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

$RuntimeDll = Join-Path $BuildOut "Voidstep.dll"
$CoreDll = Join-Path $BuildOut "Voidstep.Core.dll"
if (-not (Test-Path $RuntimeDll)) { throw "Compiler did not produce Voidstep.dll" }
if (-not (Test-Path $CoreDll)) { throw "Compiler did not produce Voidstep.Core.dll" }

$StageModules = Join-Path $Stage "Modules"
$StageModule = Join-Path $StageModules "Voidstep"
Copy-Item $ModuleSource $StageModule -Recurse -Force
$StageBin = Join-Path $StageModule "bin/Win64_Shipping_Client"
New-Item -ItemType Directory -Force -Path $StageBin | Out-Null
Copy-Item $RuntimeDll (Join-Path $StageBin "Voidstep.dll") -Force
Copy-Item $CoreDll (Join-Path $StageBin "Voidstep.Core.dll") -Force

Get-ChildItem $StageBin -File | Where-Object {
    $_.Name -notin @("Voidstep.dll", "Voidstep.Core.dll")
} | Remove-Item -Force

if (-not (Test-Path (Join-Path $StageModule "SubModule.xml"))) { throw "Staged module lacks SubModule.xml" }
$Forbidden = Get-ChildItem $StageModule -Recurse -File | Where-Object {
    $_.Name -like "TaleWorlds.*.dll" -or $_.Name -eq "TOR_Core.dll" -or $_.Name -like "MCM*.dll" -or $_.Name -like "Bannerlord.MBOptionScreen*.dll"
}
if ($Forbidden) { throw "Forbidden dependency DLLs entered the release module: $($Forbidden.FullName -join ', ')" }

Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $StageModules -DestinationPath $ZipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = @(
        "Modules/Voidstep/SubModule.xml",
        "Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.dll",
        "Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.Core.dll",
        "Modules/Voidstep/README.txt"
    )
    foreach ($entry in $required) {
        if ($entries -notcontains $entry) { throw "Release ZIP verification failed: missing $entry" }
    }
    foreach ($entry in $entries) {
        $leaf = [IO.Path]::GetFileName($entry)
        if ($leaf -like "TaleWorlds.*.dll" -or $leaf -eq "TOR_Core.dll" -or $leaf -like "MCM*.dll" -or $leaf -like "Bannerlord.MBOptionScreen*.dll") {
            throw "Release ZIP contains forbidden dependency: $entry"
        }
    }
} finally {
    $zip.Dispose()
}

$ZipHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$DllHash = (Get-FileHash $RuntimeDll -Algorithm SHA256).Hash.ToLowerInvariant()
$CoreHash = (Get-FileHash $CoreDll -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    "$ZipHash  $([IO.Path]::GetFileName($ZipPath))",
    "$DllHash  Voidstep.dll",
    "$CoreHash  Voidstep.Core.dll"
) | Set-Content (Join-Path $Dist "SHA256SUMS.txt") -Encoding UTF8

Write-Host "Build and tests passed."
Write-Host "Release ZIP: $ZipPath"
Write-Host "Release ZIP SHA-256: $ZipHash"
Write-Host "Voidstep.dll SHA-256: $DllHash"
