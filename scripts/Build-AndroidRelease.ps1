[CmdletBinding()]
param(
    [string]$ConfigurationFile = "$HOME/.diecutcatalog/android-signing.json",
    [string]$OutputDirectory = "artifacts/android"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFile = Join-Path $projectRoot "src/DieCutCatalog.Mobile/DieCutCatalog.Mobile.csproj"
if (-not (Test-Path -LiteralPath $ConfigurationFile)) {
    throw "Не найден файл параметров подписи: $ConfigurationFile"
}

$config = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
foreach ($property in "keyStore", "storePassword", "keyAlias", "keyPassword") {
    if ([string]::IsNullOrWhiteSpace($config.$property)) { throw "В параметрах подписи отсутствует $property." }
}
if (-not (Test-Path -LiteralPath $config.keyStore)) { throw "Не найден ключ подписи: $($config.keyStore)" }

[xml]$project = Get-Content -LiteralPath $projectFile
$version = $project.SelectSingleNode('//ApplicationDisplayVersion').InnerText.Trim()
$versionCode = $project.SelectSingleNode('//ApplicationVersion').InnerText.Trim()
if ([string]::IsNullOrWhiteSpace($version) -or [string]::IsNullOrWhiteSpace($versionCode)) {
    throw "В проекте не указана версия Android."
}

$drive = "R:"
if (Test-Path $drive) { throw "Временный диск $drive уже используется." }
$env:ANDROID_HOME = "$env:LOCALAPPDATA/Android/Sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $env:JAVA_HOME = "C:/Program Files/Microsoft/jdk-17.0.20.8-hotspot"
}

& subst $drive $projectRoot
if ($LASTEXITCODE -ne 0) { throw "Не удалось подключить временный диск для Android-сборки." }
try {
    $arguments = @(
        "publish", "$drive/src/DieCutCatalog.Mobile/DieCutCatalog.Mobile.csproj",
        "-c", "Release", "-f", "net9.0-android",
        "-p:AndroidPackageFormats=apk",
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=$($config.keyStore)",
        "-p:AndroidSigningStorePass=$($config.storePassword)",
        "-p:AndroidSigningKeyAlias=$($config.keyAlias)",
        "-p:AndroidSigningKeyPass=$($config.keyPassword)"
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Android release build завершился с ошибкой." }
}
finally {
    & subst $drive /d | Out-Null
}

$publishDirectory = Join-Path $projectRoot "src/DieCutCatalog.Mobile/bin/Release/net9.0-android/publish"
$apk = Get-ChildItem -LiteralPath $publishDirectory -Filter "*-Signed.apk" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $apk) { throw "Подписанный APK не найден после сборки." }

$targetDirectory = Join-Path $projectRoot $OutputDirectory
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
$target = Join-Path $targetDirectory "DieCutCatalog-Android-$version.apk"
Copy-Item -LiteralPath $apk.FullName -Destination $target -Force
Write-Host "Подписанный Android-релиз $version ($versionCode): $target"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash)"