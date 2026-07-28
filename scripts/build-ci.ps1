param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version = "1.0.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Project = Join-Path $Root "src/Voidstep/Voidstep.csproj"
$Tests = Join-Path $Root "tests/Voidstep.Core.Tests/Voidstep.Core.Tests.csproj"
$ModuleTemplate = Join-Path $Root "module/Voidstep"
$Artifacts = Join-Path $Root "artifacts"
$StageRoot = Join-Path $Artifacts "ci-module/Modules/Voidstep"
$Dist = Join-Path $Root "dist"
$Zip = Join-Path $Dist "Voidstep-v$Version-Bannerlord-1.3.15.zip"
$SourceArchive = Join-Path $Artifacts "Voidstep-source-input.zip"
$SourceHashFile = Join-Path $Dist "SOURCE_SHA256.txt"
$Checksums = Join-Path $Dist "SHA256SUMS.txt"

function Assert-File([string]$Path) {
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

Push-Location $Root
try {
    Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $Dist -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Artifacts, $Dist, $StageRoot | Out-Null

    # Hash the exact tracked source snapshot before restore or compilation.
    & git archive --format=zip --output=$SourceArchive HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive failed." }
    $sourceHash = (Get-FileHash $SourceArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sourceHash  source-input@$(& git rev-parse HEAD)" | Set-Content $SourceHashFile -Encoding ascii

    & dotnet restore $Tests --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Test restore failed." }
    & dotnet restore $Project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed." }

    & dotnet test $Tests -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Logic tests failed." }

    & python scripts/run_logic_mirror_tests.py
    if ($LASTEXITCODE -ne 0) { throw "Independent logic mirror failed." }
    & python scripts/verify_source_invariants.py
    if ($LASTEXITCODE -ne 0) { throw "Source invariant validation failed." }

    & dotnet build $Project -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Runtime build failed." }

    Copy-Item (Join-Path $ModuleTemplate "*") $StageRoot -Recurse -Force
    $Bin = Join-Path $StageRoot "bin/Win64_Shipping_Client"
    New-Item -ItemType Directory -Force -Path $Bin | Out-Null

    $RuntimeDll = Join-Path $Root "src/Voidstep/bin/$Configuration/net472/Voidstep.dll"
    $CoreDll = Join-Path $Root "src/Voidstep.Core/bin/$Configuration/netstandard2.0/Voidstep.Core.dll"
    Assert-File $RuntimeDll
    Assert-File $CoreDll
    Copy-Item $RuntimeDll $Bin -Force
    Copy-Item $CoreDll $Bin -Force

    $forbidden = Get-ChildItem $StageRoot -Recurse -File | Where-Object {
        $_.Name -like "TaleWorlds.*.dll" -or
        $_.Name -eq "TOR_Core.dll" -or
        $_.Name -like "MCM.*.dll" -or
        $_.Name -like "Bannerlord.MBOptionScreen*.dll"
    }
    if ($forbidden) {
        throw "Forbidden dependency entered the package: $($forbidden.FullName -join ', ')"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        (Join-Path $Artifacts "ci-module"),
        $Zip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Assert-File $Zip
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $required = @(
            "Modules/Voidstep/SubModule.xml",
            "Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.dll",
            "Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.Core.dll"
        )
        foreach ($entry in $required) {
            if ($entries -notcontains $entry) { throw "Package is missing: $entry" }
        }
        if ($entries | Where-Object { $_ -match '(^|/)(TaleWorlds\..*|TOR_Core|MCM\..*|Bannerlord\.MBOptionScreen.*)\.dll$' }) {
            throw "Package contains a forbidden dependency."
        }
    }
    finally {
        $archive.Dispose()
    }

    $runtimeHash = (Get-FileHash $RuntimeDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $coreHash = (Get-FileHash $CoreDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipHash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    @(
        "$zipHash  $([IO.Path]::GetFileName($Zip))",
        "$runtimeHash  Voidstep.dll",
        "$coreHash  Voidstep.Core.dll",
        "$sourceHash  source-input@$(& git rev-parse HEAD)"
    ) | Set-Content $Checksums -Encoding ascii

    Write-Host "Built and verified $Zip"
    Get-Content $Checksums
}
finally {
    Pop-Location
}
