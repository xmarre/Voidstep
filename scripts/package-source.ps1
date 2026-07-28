param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Stage = Join-Path $Root "artifacts/source-stage/Voidstep-v$Version-SOURCE"
$Zip = Join-Path $Root "dist/Voidstep-v$Version-SOURCE.zip"
Remove-Item (Split-Path -Parent $Stage) -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage, (Split-Path -Parent $Zip) | Out-Null
$items = @("src", "tests", "module", "docs", "scripts", ".github", "references/reference-manifest.json", "Voidstep.sln", "build.ps1", "README.md", "CHANGELOG.md", "LICENSE", ".gitignore", ".gitattributes", ".coderabbit.yaml")
foreach ($item in $items) {
    $source = Join-Path $Root $item
    if (Test-Path $source) { Copy-Item $source $Stage -Recurse -Force }
}
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $Zip -CompressionLevel Optimal
$hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "$hash  $([IO.Path]::GetFileName($Zip))"
