[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$PackagePath,

    [string]$ReleaseName,
    [string]$Notes = "",
    [string]$Server = "root@45.43.137.142",
    [string]$IdentityFile = "$HOME/.ssh/id_ed25519_diecut_catalog",
    [string]$RemoteRoot = "/var/lib/docker/volumes/diecut-catalog_document-storage/_data/updates"
)

$ErrorActionPreference = "Stop"
$package = Get-Item -LiteralPath $PackagePath
$fileName = $package.Name
if ($fileName -notmatch '^[A-Za-z0-9._-]+\.zip$') {
    throw "Имя архива должно содержать только латинские буквы, цифры, точку, дефис или подчёркивание."
}

$normalizedVersion = $Version.Trim().TrimStart('v', 'V')
$parsedVersion = $null
if (-not [Version]::TryParse($normalizedVersion, [ref]$parsedVersion)) {
    throw "Некорректный номер версии: $Version"
}
if ([string]::IsNullOrWhiteSpace($ReleaseName)) {
    $ReleaseName = "DieCut Catalog $normalizedVersion"
}

$manifest = [ordered]@{
    version = $normalizedVersion
    releaseName = $ReleaseName
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    fileName = $fileName
    sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    size = $package.Length
    notes = $Notes
}

$tempManifest = Join-Path ([IO.Path]::GetTempPath()) ("diecut-latest-{0}.json" -f [Guid]::NewGuid().ToString("N"))
try {
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $tempManifest -Encoding utf8NoBOM
    $sshArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) { $sshArgs += @("-i", $IdentityFile) }

    & ssh @sshArgs $Server "mkdir -p '$RemoteRoot'"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось подготовить каталог обновлений на сервере." }

    & scp @sshArgs $package.FullName "${Server}:${RemoteRoot}/${fileName}.upload"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось загрузить архив обновления." }

    & scp @sshArgs $tempManifest "${Server}:${RemoteRoot}/latest.json.upload"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось загрузить манифест обновления." }

    & ssh @sshArgs $Server "mv '$RemoteRoot/${fileName}.upload' '$RemoteRoot/$fileName' && mv '$RemoteRoot/latest.json.upload' '$RemoteRoot/latest.json' && chmod 644 '$RemoteRoot/$fileName' '$RemoteRoot/latest.json'"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось активировать обновление на сервере." }

    Write-Host "Опубликовано обновление $normalizedVersion ($fileName)."
    Write-Host "SHA-256: $($manifest.sha256)"
}
finally {
    Remove-Item -LiteralPath $tempManifest -Force -ErrorAction SilentlyContinue
}