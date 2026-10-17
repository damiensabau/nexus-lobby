#Requires -RunAsAdministrator
# Publie le service et l'installe comme service Windows (démarrage automatique, compte SYSTEM).
param(
    [string]$InstallDir = "$env:ProgramFiles\VpnClient"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$serviceName = 'VpnClientService'

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

dotnet publish "$root\src\VpnClient.Service" -c Release -r win-x64 --self-contained false -o "$InstallDir\Service"
dotnet publish "$root\src\VpnClient.App" -c Release -r win-x64 --self-contained false -o "$InstallDir\App"

New-Service -Name $serviceName `
    -BinaryPathName "`"$InstallDir\Service\VpnClientService.exe`"" `
    -DisplayName 'NEXUS LOBBY Service' `
    -Description 'Gère les tunnels WireGuard et OpenVPN de NEXUS LOBBY.' `
    -StartupType Automatic | Out-Null

# Redémarre automatiquement le service en cas de plantage.
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
Start-Service $serviceName

Write-Host "Service installé. Interface : $InstallDir\App\VpnClient.App.exe"
