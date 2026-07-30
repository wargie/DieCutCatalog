param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$DriveLetter = "R"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$androidSdk = Join-Path $env:LOCALAPPDATA "Android\Sdk"
$javaSdk = Get-ChildItem "C:\Program Files\Microsoft" -Directory -Filter "jdk-17*" |
    Sort-Object Name -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path (Join-Path $androidSdk "platform-tools\adb.exe"))) {
    throw "Android SDK not found: $androidSdk"
}
if (-not $javaSdk) {
    throw "Microsoft OpenJDK 17 not found."
}

$drive = "$DriveLetter`:"
if (Get-PSDrive $DriveLetter -ErrorAction SilentlyContinue) {
    throw "Drive $drive is already in use. Choose another with -DriveLetter."
}

subst $drive $repo
try {
    Push-Location "$drive\"
    dotnet build DieCutCatalog.Mobile.sln `
        --configuration $Configuration `
        -p:AndroidSdkDirectory="$androidSdk" `
        -p:JavaSdkDirectory="$javaSdk"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
    subst $drive /d
}