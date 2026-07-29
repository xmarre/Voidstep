param([string]$Version = "1.0.7")
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Dist = Join-Path $Root "dist"
$Zip = Join-Path $Dist "Voidstep-v$Version-SOURCE.zip"
$Checksums = Join-Path $Dist "SHA256SUMS.txt"
$SourceIdentity = Join-Path $Dist "SOURCE_SHA256.txt"

Push-Location $Root
try {
    New-Item -ItemType Directory -Force -Path $Dist | Out-Null
    Remove-Item $Zip -Force -ErrorAction SilentlyContinue
    & git archive --format=zip --output=$Zip HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive failed while creating the source package." }

    $hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if (Test-Path $SourceIdentity -PathType Leaf) {
        $identity = (Get-Content $SourceIdentity -Raw).Trim()
        if ($identity -notmatch '^([0-9a-fA-F]{64})\s+source-input@([0-9a-fA-F]{40})$') {
            throw "SOURCE_SHA256.txt has an invalid format."
        }
        if ($matches[1].ToLowerInvariant() -ne $hash) {
            throw "Source package does not match the exact source snapshot hashed before compilation."
        }
    }

    $line = "$hash  $([IO.Path]::GetFileName($Zip))"
    $existing = @()
    if (Test-Path $Checksums -PathType Leaf) {
        $existing = @(Get-Content $Checksums | Where-Object { $_ -notmatch [regex]::Escape([IO.Path]::GetFileName($Zip)) + '$' })
    }
    @($existing + $line) | Set-Content $Checksums -Encoding ascii
    Write-Host $line
}
finally {
    Pop-Location
}
