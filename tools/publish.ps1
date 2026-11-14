[CmdletBinding()]
param(
    [string]$Output = "deploy\publish",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found"
}

$target = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$target = [System.IO.Path]::GetFullPath($target)

$deploy = [System.IO.Path]::GetFullPath((Join-Path $root "deploy"))
$inDeploy = $target.StartsWith($deploy + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $target) {
    $entries = @(Get-ChildItem -LiteralPath $target -Force)
    if (-not $inDeploy -and $entries.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $target "wwwroot\_framework"))) {
        throw "$target is not empty and is not a previous publish output"
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}
New-Item -ItemType Directory -Path $target -Force | Out-Null

if (-not $SkipTests) {
    dotnet test src/Shadowline.Tests -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "tests failed"
    }
}

dotnet publish src/Shadowline.Web -c $Configuration -o $target -p:CompressionEnabled=false
if ($LASTEXITCODE -ne 0) {
    throw "publish failed"
}

$site = Join-Path $target "wwwroot"

$terrain = Join-Path $root "deploy\ldem\out"
if (Test-Path -LiteralPath (Join-Path $terrain "index.json")) {
    $terrainTarget = Join-Path $site "data\ldem"
    New-Item -ItemType Directory -Path $terrainTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $terrain "*") -Destination $terrainTarget -Force
}
else {
    Write-Warning "No terrain pack in $terrain, the 5 m relief will be missing (run tools\ldem\pack_ldem.py)"
}
foreach ($required in @("index.html", "_framework", "data\finder.bin.gz", "js\moonscene.js")) {
    if (-not (Test-Path -LiteralPath (Join-Path $site $required))) {
        throw "missing $required in $site"
    }
}

$files = @(Get-ChildItem -LiteralPath $site -Recurse -File)
$sizeMb = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$largest = $files | Sort-Object -Property Length -Descending | Select-Object -First 1

$revision = "unknown"
try {
    $branch = & git branch --show-current
    $commit = & git rev-parse --short HEAD
    $changes = & git status --porcelain
    $revision = "$branch $commit" + $(if ($changes) { " with uncommitted changes" } else { "" })
}
catch {
}

Write-Host ""
Write-Host "Site:     $site"
Write-Host "Files:    $($files.Count), $sizeMb MB"
Write-Host "Revision: $revision"
if ($files.Count -gt 1000) {
    Write-Warning "More than 1000 files: the Cloudflare dashboard upload will refuse it, use Wrangler"
}
if ($largest.Length -gt 25MB) {
    Write-Warning "$($largest.FullName) is larger than 25 MB, the Cloudflare Pages file limit"
}
Write-Host "Upload:   Cloudflare dashboard, Workers & Pages, shadowline, Create deployment, folder above"
Write-Host "Or:       npx wrangler pages deploy `"$site`" --project-name shadowline"
