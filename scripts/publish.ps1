# Publie l'app et le service (autonomes : .NET inclus, rien à installer en plus) dans dist\,
# puis construit l'installateur si Inno Setup 6 est présent (ou avec -Installer pour l'exiger).
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
$out = Join-Path $dist "NexusLobby"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

foreach ($project in @(
        @{ Path = "src\VpnClient.App"; Dir = "App" },
        @{ Path = "src\VpnClient.Service"; Dir = "Service" })) {
    dotnet publish (Join-Path $root $project.Path) -c Release -r $Runtime --self-contained true `
        -p:Version=$Version -p:PublishReadyToRun=true -o (Join-Path $out $project.Dir)
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué pour $($project.Path)" }
}

# Garde-fou : le mode démo ne doit pas se retrouver dans la version publiée.
if (Test-Path (Join-Path $out "Service\appsettings.Development.json")) {
    throw "appsettings.Development.json (mode démo) présent dans la publication"
}

Write-Host "Publication : $out"

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue)?.Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$out" "/O$dist" (Join-Path $root "installer\NexusLobby.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup a échoué" }
    Write-Host "Installateur : $dist\NexusLobby-Setup-$Version.exe"
}
elseif ($Installer) {
    throw "Inno Setup 6 introuvable (https://jrsoftware.org/isinfo.php)"
}
else {
    Write-Host "Inno Setup absent : installateur non construit (seulement les fichiers publiés)."
}
