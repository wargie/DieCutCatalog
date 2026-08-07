[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][int]$VersionCode,
    [Parameter(Mandatory)][string]$ApkPath,
    [string]$ReleaseName,
    [string]$Notes = "",
    [switch]$Required,
    [string]$Server = "root@45.43.137.142",
    [string]$IdentityFile = "$HOME/.ssh/id_ed25519_diecut_catalog",
    [string]$RemoteRoot = "/var/lib/docker/volumes/diecut-catalog_document-storage/_data/updates/android"
)

$ErrorActionPreference = "Stop"
$package = Get-Item -LiteralPath $ApkPath
if ($package.Name -notmatch '^[A-Za-z0-9._-]+\.apk$') { throw "Некорректное имя APK." }
if ($VersionCode -le 0) { throw "VersionCode должен быть положительным целым числом." }

$normalizedVersion = $Version.Trim().TrimStart('v', 'V')
$parsedVersion = $null
if (-not [Version]::TryParse($normalizedVersion, [ref]$parsedVersion)) { throw "Некорректный номер версии: $Version" }
if ([string]::IsNullOrWhiteSpace($ReleaseName)) { $ReleaseName = "DieCut Catalog Android $normalizedVersion" }

$manifest = [ordered]@{
    version = $normalizedVersion
    versionCode = $VersionCode
    required = [bool]$Required
    releaseName = $ReleaseName
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    fileName = $package.Name
    sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    size = $package.Length
    notes = $Notes
}

$tempManifest = Join-Path ([IO.Path]::GetTempPath()) ("diecut-android-latest-{0}.json" -f [Guid]::NewGuid().ToString("N"))
try {
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $tempManifest -Encoding utf8NoBOM
    $sshArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) { $sshArgs += @("-i", $IdentityFile) }

    & ssh @sshArgs $Server "mkdir -p '$RemoteRoot'"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось подготовить Android-каталог обновлений." }

    & scp @sshArgs $package.FullName "${Server}:${RemoteRoot}/$($package.Name).upload"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось загрузить APK." }

    & scp @sshArgs $tempManifest "${Server}:${RemoteRoot}/latest.json.upload"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось загрузить Android-манифест." }

    & ssh @sshArgs $Server "mv '$RemoteRoot/$($package.Name).upload' '$RemoteRoot/$($package.Name)' && mv '$RemoteRoot/latest.json.upload' '$RemoteRoot/latest.json' && chmod 644 '$RemoteRoot/$($package.Name)' '$RemoteRoot/latest.json'"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось активировать Android-обновление." }

    Write-Host "Опубликовано Android-обновление $normalizedVersion ($($package.Name))."
    Write-Host "SHA-256: $($manifest.sha256)"
}
finally {
    Remove-Item -LiteralPath $tempManifest -Force -ErrorAction SilentlyContinue
}
