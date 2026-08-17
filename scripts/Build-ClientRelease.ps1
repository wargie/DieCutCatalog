[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repoRoot "artifacts/release"
$packageName = "DieCutCatalog-$Version-$Runtime"
$publishRoot = Join-Path $releaseRoot $packageName
$updaterRoot = Join-Path $releaseRoot "$packageName-updater"
$archivePath = Join-Path $releaseRoot "$packageName.zip"

function Assert-WorkspacePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Отказ от изменения пути вне репозитория: $fullPath"
    }
}

Assert-WorkspacePath $publishRoot
Assert-WorkspacePath $updaterRoot
Assert-WorkspacePath $archivePath

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
foreach ($path in @($publishRoot, $updaterRoot)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }

dotnet publish (Join-Path $repoRoot "src/DieCutCatalog.Desktop/DieCutCatalog.Desktop.csproj") `
    --configuration Release --runtime $Runtime --self-contained true --no-restore `
    --maxcpucount:1 -p:UseSharedCompilation=false -p:NodeReuse=false `
    -p:PublishSingleFile=true -p:DebugType=None --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать клиент." }

dotnet publish (Join-Path $repoRoot "src/DieCutCatalog.Updater/DieCutCatalog.Updater.csproj") `
    --configuration Release --runtime $Runtime --self-contained true --no-restore `
    --maxcpucount:1 -p:UseSharedCompilation=false -p:NodeReuse=false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output $updaterRoot
if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать updater." }

Copy-Item -LiteralPath (Join-Path $updaterRoot "DieCutCatalog.Updater.exe") -Destination $publishRoot -Force
Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

Remove-Item -LiteralPath $updaterRoot -Recurse -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
Write-Host "Готово: $archivePath"
Write-Host "SHA-256: $hash"
